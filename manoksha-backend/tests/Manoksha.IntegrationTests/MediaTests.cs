using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using SkiaSharp;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Product media (ADR-001 §29): originals uploaded straight to (private) storage, optimized renditions in the public media
/// container, only metadata/paths in PostgreSQL, and customers served renditions — never the original.
/// </summary>
[Collection(ApiCollection.Name)]
public class MediaTests(ManokshaApiFactory factory)
{
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(140, 20, 60));
            using var paint = new SKPaint { Color = SKColors.Gold };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 92).ToArray();
    }

    private async Task<JsonNode> UploadAsync(HttpClient client, Guid productId, byte[] bytes, string contentType, string name)
    {
        var start = await client.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/media/uploads",
            new { fileName = name, contentType, sizeBytes = bytes.Length }).OkJsonAsync();
        start["method"]!.GetValue<string>().Should().Be("PUT");
        var put = new ByteArrayContent(bytes);
        put.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        (await factory.CreateClient().PutAsync(start["uploadUrl"]!.GetValue<string>(), put)).StatusCode.Should().Be(HttpStatusCode.OK, "the browser uploads directly");
        return start;
    }

    private async Task<Guid> ProductAsync()
    {
        var (sku, productId) = await factory.CreatePricedSkuAsync(1500m);
        await factory.StockUpAsync(DevelopmentSeedData.BranchKarimnagar, sku, 2, 700m);
        return productId;
    }

    [Fact]
    public async Task Images_are_optimized_into_webp_renditions_and_shown_in_the_shop_and_reseller_catalog()
    {
        var productId = await ProductAsync();
        var owner = await factory.OwnerClientAsync();
        var original = Jpeg(2400, 1800);

        var start = await UploadAsync(owner, productId, original, "image/jpeg", "saree-front.jpg");
        var media = await owner.PostAsync($"/api/v1/admin/catalog/products/{productId}/media/{start["mediaId"]}/complete", null).OkJsonAsync();
        media["status"]!.GetValue<string>().Should().Be("Ready");
        media["width"]!.GetValue<int>().Should().Be(2400);
        media["originalBytes"]!.GetValue<long>().Should().Be(original.Length);
        var image = media["image"]!;
        image["thumb"]!.GetValue<string>().Should().EndWith("/thumb.webp");
        media["optimizedBytes"]!.GetValue<long>().Should().BeLessThan(original.Length, "customers get smaller optimized files");

        // Renditions: WebP, never upscaled, sized for web/mobile.
        var anonymous = factory.CreateClient();
        foreach (var (name, expectedWidth) in new[] { ("thumb", 400), ("medium", 900), ("large", 1600) })
        {
            var response = await anonymous.GetAsync(new Uri(image[name]!.GetValue<string>()).PathAndQuery);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
            using var decoded = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
            decoded.Width.Should().Be(expectedWidth);
            decoded.Height.Should().Be(expectedWidth * 3 / 4);
        }
        (await anonymous.GetAsync($"/local-files/media-originals/{start["mediaId"]}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "originals are private");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM catalog.product_media WHERE product_id = '{productId}' AND status = 'Ready'")).Should().Be(1);

        var product = await anonymous.GetAsync($"/api/v1/catalog/products/{productId}").OkJsonAsync();
        product["media"]!.AsArray().Should().ContainSingle();
        product["media"]![0]!["image"]!["large"]!.GetValue<string>().Should().EndWith("/large.webp");
        var list = await anonymous.GetAsync("/api/v1/catalog/products?pageSize=100").OkJsonAsync();
        list["items"]!.AsArray().Single(i => i!["productId"]!.GetValue<Guid>() == productId)!["image"]!["thumb"]!.GetValue<string>().Should().EndWith("/thumb.webp");
        var reseller = await factory.NewActiveResellerAsync();
        var catalog = await reseller.GetAsync("/api/v1/reseller/catalog?pageSize=100").OkJsonAsync();
        catalog["items"]!.AsArray().Single(i => i!["productId"]!.GetValue<Guid>() == productId)!["image"].Should().NotBeNull();
    }

    [Fact]
    public async Task Video_is_transcoded_to_a_web_mp4_with_a_poster()
    {
        if (!ToolExists("ffmpeg"))
        {
            return; // video processing needs ffmpeg (present in the API container image)
        }
        var productId = await ProductAsync();
        var owner = await factory.OwnerClientAsync();
        var path = Path.Combine(Path.GetTempPath(), $"mk-{Guid.NewGuid():N}.mov");
        Run("ffmpeg", "-y", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=1920x1080:rate=25", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "3", "-c:v", "libx264", "-c:a", "aac", "-shortest", path);
        var bytes = await File.ReadAllBytesAsync(path);
        File.Delete(path);

        var start = await UploadAsync(owner, productId, bytes, "video/quicktime", "walkthrough.mov");
        var media = await owner.PostAsync($"/api/v1/admin/catalog/products/{productId}/media/{start["mediaId"]}/complete", null).OkJsonAsync();
        media["status"]!.GetValue<string>().Should().Be("Ready");
        media["height"]!.GetValue<int>().Should().Be(720, "videos are delivered at up to 720p");
        media["width"]!.GetValue<int>().Should().Be(1280);
        media["durationSeconds"]!.GetValue<double>().Should().BeApproximately(3, 0.5);
        var video = await factory.CreateClient().GetAsync(new Uri(media["videoUrl"]!.GetValue<string>()).PathAndQuery);
        video.Content.Headers.ContentType!.MediaType.Should().Be("video/mp4");
        media["posterUrl"]!.GetValue<string>().Should().EndWith("/poster.webp");
    }

    [Fact]
    public async Task Bad_files_limits_forged_uploads_and_permissions_are_refused()
    {
        var productId = await ProductAsync();
        var owner = await factory.OwnerClientAsync();
        var url = $"/api/v1/admin/catalog/products/{productId}/media/uploads";

        (await (await owner.PostAsJsonAsync(url, new { fileName = "x.gif", contentType = "image/gif", sizeBytes = 100 })).ErrorCodeAsync()).Should().Be("MEDIA_TYPE_INVALID");
        (await (await owner.PostAsJsonAsync(url, new { fileName = "x.jpg", contentType = "image/jpeg", sizeBytes = 21L * 1024 * 1024 })).ErrorCodeAsync())
            .Should().Be("MEDIA_TOO_LARGE");
        var sales = await factory.UserClientAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar);
        (await sales.PostAsJsonAsync(url, new { fileName = "x.jpg", contentType = "image/jpeg", sizeBytes = 100 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Not uploaded yet.
        var pending = await owner.PostAsJsonAsync(url, new { fileName = "a.jpg", contentType = "image/jpeg", sizeBytes = 1000 }).OkJsonAsync();
        (await (await owner.PostAsync($"/api/v1/admin/catalog/products/{productId}/media/{pending["mediaId"]}/complete", null)).ErrorCodeAsync())
            .Should().Be("MEDIA_NOT_UPLOADED");

        // A forged or altered upload link is rejected.
        var forged = pending["uploadUrl"]!.GetValue<string>().Replace("sig=", "sig=00", StringComparison.Ordinal);
        var content = new ByteArrayContent(Jpeg(10, 10));
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        (await factory.CreateClient().PutAsync(forged, content)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Not an image although declared as one.
        var fake = await UploadAsync(owner, productId, "this is not an image"u8.ToArray(), "image/jpeg", "fake.jpg");
        var failed = await owner.PostAsync($"/api/v1/admin/catalog/products/{productId}/media/{fake["mediaId"]}/complete", null);
        failed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await failed.ErrorCodeAsync()).Should().Be("MEDIA_PROCESSING_FAILED");
        (await factory.ScalarAsync<string>($"SELECT status FROM catalog.product_media WHERE id = '{fake["mediaId"]}'")).Should().Be("Failed");
    }

    [Fact]
    public async Task Owner_orders_and_deletes_media_and_the_first_image_is_the_primary_image()
    {
        var productId = await ProductAsync();
        var owner = await factory.OwnerClientAsync();
        var ids = new List<Guid>();
        foreach (var size in new[] { 800, 1000 })
        {
            var s = await UploadAsync(owner, productId, Jpeg(size, size), "image/jpeg", $"{size}.jpg");
            await owner.PostAsync($"/api/v1/admin/catalog/products/{productId}/media/{s["mediaId"]}/complete", null).OkJsonAsync();
            ids.Add(s["mediaId"]!.GetValue<Guid>());
        }
        await owner.PutAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/media/order", new { mediaIds = new[] { ids[1], ids[0] } }).OkJsonAsync();
        await owner.PutAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/media/{ids[1]}", new { altText = "Front view" }).OkJsonAsync();
        var anonymous = factory.CreateClient();
        var product = await anonymous.GetAsync($"/api/v1/catalog/products/{productId}").OkJsonAsync();
        product["media"]![0]!["id"]!.GetValue<Guid>().Should().Be(ids[1]);
        product["media"]![0]!["altText"]!.GetValue<string>().Should().Be("Front view");

        var thumb = new Uri(product["media"]![0]!["image"]!["thumb"]!.GetValue<string>()).PathAndQuery;
        (await owner.DeleteAsync($"/api/v1/admin/catalog/products/{productId}/media/{ids[1]}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await anonymous.GetAsync($"/api/v1/catalog/products/{productId}").OkJsonAsync())["media"]!.AsArray().Should().ContainSingle();
        (await anonymous.GetAsync(thumb)).StatusCode.Should().Be(HttpStatusCode.NotFound, "deleted media files are removed from storage");
    }

    private static bool ToolExists(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "-version") { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { RedirectStandardError = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        p.ExitCode.Should().Be(0, p.StandardError.ReadToEnd());
    }
}
