using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Manoksha.IntegrationTests.Infrastructure;

public static class ResellerHelpers
{
    public static async Task<JsonNode> CreateResellerAsync(this ManokshaApiFactory factory, string mobile, decimal discountPct = 10m, string? name = null)
    {
        var owner = await factory.OwnerClientAsync();
        return await owner.PostAsJsonAsync("/api/v1/admin/resellers", new
        {
            contactName = name ?? "Reseller " + mobile[^4..], businessName = "Shop " + mobile[^4..], mobile, email = $"r{mobile[^6..]}@example.com",
            addressLine = "12 Main Road", city = "Warangal", state = "Telangana", pin = "506002", resellerDiscountPct = discountPct, reason = "onboarding",
        }).OkJsonAsync();
    }

    /// <summary>Reseller signs in with mobile OTP (first time = activation).</summary>
    public static async Task<HttpResponseMessage> ResellerOtpLoginRawAsync(this ManokshaApiFactory factory, string mobile)
    {
        factory.Clock.Advance(TimeSpan.FromMinutes(2)); // stay clear of the resend cool-down
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "reseller" })).EnsureSuccessStatusCode();
        return await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "reseller", code = ApiClient.LastOtpFor(mobile) });
    }

    public static async Task<HttpClient> ResellerClientAsync(this ManokshaApiFactory factory, string mobile)
    {
        var login = await factory.ResellerOtpLoginRawAsync(mobile);
        var body = await login.ReadJsonAsync();
        login.StatusCode.Should().Be(HttpStatusCode.OK, body.ToJsonString());
        return factory.Authorized(ApiClient.ToTokens(body).AccessToken);
    }

    public static async Task<HttpClient> NewActiveResellerAsync(this ManokshaApiFactory factory, decimal discountPct = 10m)
    {
        var mobile = ApiClient.NewMobile();
        await factory.CreateResellerAsync(mobile, discountPct);
        return await factory.ResellerClientAsync(mobile);
    }

    public static async Task SetRetailPriceAsync(this ManokshaApiFactory factory, Guid skuId, decimal price)
    {
        var owner = await factory.OwnerClientAsync();
        await owner.PutAsJsonAsync($"/api/v1/admin/pricing/skus/{skuId}/retail-price", new { price, reason = "price list" }).OkJsonAsync();
    }

    /// <summary>An ACTIVE, priced SKU available to resellers (optionally not).</summary>
    public static async Task<(Guid SkuId, Guid ProductId)> CreatePricedSkuAsync(this ManokshaApiFactory factory, decimal price, bool forResellers = true)
    {
        var owner = await factory.OwnerClientAsync();
        var category = await owner.PostAsJsonAsync("/api/v1/admin/catalog/categories", new { name = "C " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1, isActive = true, reason = "x" }).OkJsonAsync();
        var product = await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId = category["id"]!.GetValue<Guid>(), name = "Priced " + Guid.NewGuid().ToString("N")[..6], trackingMode = "Quantity",
            availableForRetail = true, availableForReseller = forResellers, reason = "x",
        }).OkJsonAsync(HttpStatusCode.Created);
        var productId = product["id"]!.GetValue<Guid>();
        var detail = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/variants", new { generateBarcode = true, reason = "x" }).OkJsonAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/status", new { status = "Active", reason = "launch" }).OkJsonAsync();
        var skuId = detail["variants"]![0]!["skuId"]!.GetValue<Guid>();
        if (price > 0)
        {
            await factory.SetRetailPriceAsync(skuId, price);
        }
        return (skuId, productId);
    }

    public static async Task<JsonNode> QuoteAsync(this HttpClient reseller, Guid skuId) =>
        (await reseller.PostAsJsonAsync("/api/v1/reseller/price-quote", new { skuIds = new[] { skuId } }).OkJsonAsync())[0]!;
}
