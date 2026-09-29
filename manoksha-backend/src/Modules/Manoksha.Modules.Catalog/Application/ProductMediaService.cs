using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Catalog.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Catalog.Application;

public sealed record StartMediaUploadRequest(string FileName, string ContentType, long SizeBytes);

/// <param name="UploadUrl">PUT the file here with the given Content-Type header (directly to storage; may be relative in development).</param>
public sealed record MediaUploadDto(Guid MediaId, string Kind, string UploadUrl, string Method, string ContentType, DateTimeOffset ExpiresAt);

public sealed record MediaDto(
    Guid Id,
    string Kind,
    string Status,
    int SortOrder,
    string? AltText,
    string OriginalFileName,
    long OriginalBytes,
    int? Width,
    int? Height,
    double? DurationSeconds,
    ImageUrls? Image,
    string? VideoUrl,
    string? PosterUrl,
    long OptimizedBytes,
    string? FailureReason,
    DateTimeOffset CreatedAt);

public sealed record UpdateMediaRequest(string? AltText);

public sealed record ReorderMediaRequest(IReadOnlyList<Guid> MediaIds);

/// <summary>
/// Product images and videos (ADR-001 §29). Upload: the browser PUTs the original straight to private storage with a short-lived
/// URL (large files never pass through the API), then asks the API to finish; the API makes the optimized renditions (WebP sizes;
/// 720p MP4 + poster) in the public media bucket and stores only metadata and object paths in PostgreSQL.
/// </summary>
internal sealed class ProductMediaService(
    ManokshaDbContext db,
    IFileStorage storage,
    IMediaProcessor processor,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ProductMediaService> logger) : ICatalogMedia
{
    public const long MaxImageBytes = 20L * 1024 * 1024;
    public const long MaxVideoBytes = 200L * 1024 * 1024;
    public const int MaxImagesPerProduct = 12;
    public const int MaxVideosPerProduct = 2;
    public static readonly TimeSpan MaxVideoDuration = TimeSpan.FromSeconds(120);
    public const int VideoMaxHeight = 720;
    public static readonly IReadOnlyList<ImageSize> ImageSizes = [new("thumb", 400), new("medium", 900), new("large", 1600)];
    private const string ImmutableCache = "public, max-age=31536000, immutable";

    private static readonly Dictionary<string, (MediaKind Kind, string Extension)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = (MediaKind.Image, ".jpg"),
        ["image/png"] = (MediaKind.Image, ".png"),
        ["image/webp"] = (MediaKind.Image, ".webp"),
        ["video/mp4"] = (MediaKind.Video, ".mp4"),
        ["video/quicktime"] = (MediaKind.Video, ".mov"),
        ["video/webm"] = (MediaKind.Video, ".webm"),
    };

    public async Task<MediaUploadDto> StartUploadAsync(Guid productId, StartMediaUploadRequest r, CancellationToken ct)
    {
        await EnsureProductAsync(productId, ct);
        if (!Allowed.TryGetValue(r.ContentType ?? string.Empty, out var type))
        {
            throw new BusinessRuleException("MEDIA_TYPE_INVALID", "Upload a JPEG, PNG or WebP image, or an MP4, MOV or WebM video.", 400);
        }
        var limit = type.Kind == MediaKind.Image ? MaxImageBytes : MaxVideoBytes;
        if (r.SizeBytes <= 0 || r.SizeBytes > limit)
        {
            throw new BusinessRuleException("MEDIA_TOO_LARGE", $"The file must be at most {limit / (1024 * 1024)} MB.", 400);
        }
        var existing = await db.Set<ProductMedia>().CountAsync(m => m.ProductId == productId && m.Kind == type.Kind
            && (m.Status == MediaStatus.Ready || m.Status == MediaStatus.Uploading), ct);
        var max = type.Kind == MediaKind.Image ? MaxImagesPerProduct : MaxVideosPerProduct;
        if (existing >= max)
        {
            throw new BusinessRuleException("MEDIA_LIMIT_REACHED", $"A product can have at most {max} {(type.Kind == MediaKind.Image ? "images" : "videos")}.", 409);
        }

        var id = Uuid7.NewGuid();
        var key = $"products/{productId:N}/{id:N}/original{type.Extension}";
        var fileName = Path.GetFileName(r.FileName ?? "upload").Trim();
        var media = new ProductMedia(id, productId, type.Kind, key, fileName.Length > 200 ? fileName[..200] : fileName, r.ContentType!.ToLowerInvariant(), r.SizeBytes,
            currentUser.UserId, clock.UtcNow);
        db.Add(media);
        await db.SaveChangesAsync(ct);
        var validFor = TimeSpan.FromMinutes(30);
        var url = await storage.GetUploadUrlAsync(StorageContainers.MediaOriginals, key, media.OriginalContentType, validFor, ct);
        return new MediaUploadDto(id, type.Kind.ToString(), url, "PUT", media.OriginalContentType, clock.UtcNow.Add(validFor));
    }

    /// <summary>Checks the uploaded original and creates the optimized renditions.</summary>
    public async Task<MediaDto> CompleteAsync(Guid productId, Guid mediaId, CancellationToken ct)
    {
        var media = await FindAsync(productId, mediaId, ct);
        if (media.Status == MediaStatus.Ready)
        {
            return ToDto(media);
        }
        var info = await storage.GetInfoAsync(StorageContainers.MediaOriginals, media.OriginalKey, ct)
                   ?? throw new BusinessRuleException("MEDIA_NOT_UPLOADED", "The file has not been uploaded yet.", 409);
        var limit = media.Kind == MediaKind.Image ? MaxImageBytes : MaxVideoBytes;
        if (info.Size > limit)
        {
            await FailAsync(media, $"The file is larger than {limit / (1024 * 1024)} MB.", ct);
        }

        var prefix = $"products/{productId:N}/{mediaId:N}/";
        var renditions = new List<MediaRendition>();
        int width, height;
        double? duration = null;
        try
        {
            if (media.Kind == MediaKind.Image)
            {
                await using var original = await storage.OpenReadAsync(StorageContainers.MediaOriginals, media.OriginalKey, ct);
                var set = processor.ProcessImage(original, ImageSizes);
                foreach (var r in set.Renditions)
                {
                    var key = $"{prefix}{r.Name}.webp";
                    await storage.PutAsync(StorageContainers.Media, key, new MemoryStream(r.Webp), "image/webp", ct, ImmutableCache);
                    renditions.Add(new MediaRendition(r.Name, key, "image/webp", r.Width, r.Height, r.Webp.Length));
                }
                (width, height) = (set.OriginalWidth, set.OriginalHeight);
            }
            else
            {
                var work = Directory.CreateTempSubdirectory("manoksha-media-");
                try
                {
                    var input = Path.Combine(work.FullName, "original" + Path.GetExtension(media.OriginalKey));
                    await using (var original = await storage.OpenReadAsync(StorageContainers.MediaOriginals, media.OriginalKey, ct))
                    await using (var file = File.Create(input))
                    {
                        await original.CopyToAsync(file, ct);
                    }
                    var v = await processor.ProcessVideoAsync(input, work.FullName, VideoMaxHeight, MaxVideoDuration, ct);
                    await using (var mp4 = File.OpenRead(v.Mp4Path))
                    {
                        var stored = await storage.PutAsync(StorageContainers.Media, $"{prefix}video.mp4", mp4, "video/mp4", ct, ImmutableCache);
                        renditions.Add(new MediaRendition("video", $"{prefix}video.mp4", "video/mp4", v.Width, v.Height, stored.Size));
                    }
                    await storage.PutAsync(StorageContainers.Media, $"{prefix}poster.webp", new MemoryStream(v.PosterWebp), "image/webp", ct, ImmutableCache);
                    renditions.Add(new MediaRendition("poster", $"{prefix}poster.webp", "image/webp", v.PosterWidth, v.PosterHeight, v.PosterWebp.Length));
                    (width, height, duration) = (v.Width, v.Height, v.DurationSeconds);
                }
                finally
                {
                    work.Delete(recursive: true);
                }
            }
        }
        catch (MediaProcessingException ex)
        {
            logger.LogWarning(ex, "Media {MediaId} could not be processed", mediaId);
            await FailAsync(media, ex.Message, ct);
            throw; // unreachable: FailAsync throws
        }

        var nextOrder = (await db.Set<ProductMedia>().Where(m => m.ProductId == productId && m.Status == MediaStatus.Ready)
            .MaxAsync(m => (int?)m.SortOrder, ct) ?? -1) + 1;
        media.MarkReady(info.Size, width, height, duration, renditions, nextOrder, clock.UtcNow);
        await audit.RecordAsync(new AuditRecord("catalog.product.media_added", "Product", productId.ToString(),
            After: new { mediaId, kind = media.Kind.ToString(), media.OriginalFileName, originalBytes = info.Size, width, height, duration,
                optimizedBytes = renditions.Sum(r => r.Bytes) }), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(media);
    }

    public async Task<IReadOnlyList<MediaDto>> ListAsync(Guid productId, CancellationToken ct)
    {
        await EnsureProductAsync(productId, ct);
        return (await db.Set<ProductMedia>().AsNoTracking().Where(m => m.ProductId == productId && m.Status != MediaStatus.Deleted)
            .OrderBy(m => m.Status).ThenBy(m => m.SortOrder).ThenBy(m => m.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<MediaDto> UpdateAsync(Guid productId, Guid mediaId, UpdateMediaRequest r, CancellationToken ct)
    {
        var media = await FindAsync(productId, mediaId, ct);
        if ((r.AltText?.Trim().Length ?? 0) > 200)
        {
            throw new BusinessRuleException("ALT_TEXT_TOO_LONG", "Keep the description under 200 characters.", 400);
        }
        media.SetAltText(r.AltText);
        await db.SaveChangesAsync(ct);
        return ToDto(media);
    }

    /// <summary>Sets the display order; the first image is the product's primary image.</summary>
    public async Task<IReadOnlyList<MediaDto>> ReorderAsync(Guid productId, ReorderMediaRequest r, CancellationToken ct)
    {
        var ready = await db.Set<ProductMedia>().Where(m => m.ProductId == productId && m.Status == MediaStatus.Ready).ToListAsync(ct);
        var ids = (r.MediaIds ?? []).ToList();
        if (ids.Count != ready.Count || ids.Distinct().Count() != ids.Count || ids.Exists(id => ready.TrueForAll(m => m.Id != id)))
        {
            throw new BusinessRuleException("MEDIA_ORDER_INVALID", "List every ready media item of this product exactly once.", 400);
        }
        for (var i = 0; i < ids.Count; i++)
        {
            ready.Single(m => m.Id == ids[i]).SetSortOrder(i);
        }
        await audit.RecordAsync(new AuditRecord("catalog.product.media_reordered", "Product", productId.ToString(), After: new { order = ids }), ct);
        await db.SaveChangesAsync(ct);
        return await ListAsync(productId, ct);
    }

    public async Task DeleteAsync(Guid productId, Guid mediaId, CancellationToken ct)
    {
        var media = await FindAsync(productId, mediaId, ct);
        media.MarkDeleted();
        await audit.RecordAsync(new AuditRecord("catalog.product.media_deleted", "Product", productId.ToString(),
            Before: new { mediaId, kind = media.Kind.ToString(), media.OriginalFileName }), ct);
        await db.SaveChangesAsync(ct);
        await DeleteObjectsAsync(media, ct);
    }

    // ---- ICatalogMedia (public) ----

    public async Task<IReadOnlyDictionary<Guid, ImageUrls>> PrimaryImagesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToList();
        var images = await db.Set<ProductMedia>().AsNoTracking()
            .Where(m => ids.Contains(m.ProductId) && m.Status == MediaStatus.Ready && m.Kind == MediaKind.Image)
            .OrderBy(m => m.SortOrder).ToListAsync(cancellationToken);
        return images.GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => ImageOf(g.First())!);
    }

    public async Task<IReadOnlyList<PublicMedia>> GalleryAsync(Guid productId, CancellationToken cancellationToken = default) =>
        (await db.Set<ProductMedia>().AsNoTracking().Where(m => m.ProductId == productId && m.Status == MediaStatus.Ready)
            .OrderBy(m => m.SortOrder).ToListAsync(cancellationToken))
        .Select(m => new PublicMedia(m.Id, m.Kind.ToString(), ImageOf(m), UrlOf(m, "video"), UrlOf(m, "poster"), m.DurationSeconds, m.AltText)).ToList();

    // ---- housekeeping ----

    /// <summary>Uploads that were started but never finished (e.g. the browser was closed) are removed after a day.</summary>
    public async Task<int> CleanupAbandonedUploadsAsync(CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddDays(-1);
        var stale = await db.Set<ProductMedia>().Where(m => (m.Status == MediaStatus.Uploading || m.Status == MediaStatus.Failed) && m.CreatedAt < cutoff)
            .Take(100).ToListAsync(ct);
        foreach (var m in stale)
        {
            m.MarkDeleted();
        }
        await db.SaveChangesAsync(ct);
        foreach (var m in stale)
        {
            await DeleteObjectsAsync(m, ct);
        }
        return stale.Count;
    }

    // ---- helpers ----

    private async Task FailAsync(ProductMedia media, string reason, CancellationToken ct)
    {
        media.MarkFailed(reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(StorageContainers.MediaOriginals, media.OriginalKey, ct);
        throw new BusinessRuleException("MEDIA_PROCESSING_FAILED", reason, 422);
    }

    private async Task DeleteObjectsAsync(ProductMedia media, CancellationToken ct)
    {
        try
        {
            await storage.DeleteAsync(StorageContainers.MediaOriginals, media.OriginalKey, ct);
            foreach (var r in media.Renditions)
            {
                await storage.DeleteAsync(StorageContainers.Media, r.ObjectKey, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage cleanup is best effort; the record is already deleted and nothing references the objects.
            logger.LogWarning(ex, "Deleting stored objects of media {MediaId} failed", media.Id);
        }
    }

    private async Task<ProductMedia> FindAsync(Guid productId, Guid mediaId, CancellationToken ct) =>
        await db.Set<ProductMedia>().SingleOrDefaultAsync(m => m.Id == mediaId && m.ProductId == productId && m.Status != MediaStatus.Deleted, ct)
        ?? throw new NotFoundException("MEDIA_NOT_FOUND", "Media not found.");

    private async Task EnsureProductAsync(Guid productId, CancellationToken ct)
    {
        if (!await db.Set<Product>().AnyAsync(p => p.Id == productId, ct))
        {
            throw new NotFoundException("PRODUCT_NOT_FOUND", "Product not found.");
        }
    }

    private string? UrlOf(ProductMedia m, string name) =>
        m.Renditions.FirstOrDefault(r => r.Name == name) is { } r ? storage.GetPublicUrl(StorageContainers.Media, r.ObjectKey) : null;

    private ImageUrls? ImageOf(ProductMedia m) =>
        m.Kind != MediaKind.Image || m.Status != MediaStatus.Ready
            ? null
            : new ImageUrls(UrlOf(m, "thumb")!, UrlOf(m, "medium")!, UrlOf(m, "large")!, m.AltText, m.Width ?? 0, m.Height ?? 0);

    private MediaDto ToDto(ProductMedia m) =>
        new(m.Id, m.Kind.ToString(), m.Status.ToString(), m.SortOrder, m.AltText, m.OriginalFileName, m.OriginalBytes, m.Width, m.Height, m.DurationSeconds,
            ImageOf(m), UrlOf(m, "video"), UrlOf(m, "poster"), m.Renditions.Sum(r => r.Bytes), m.FailureReason, m.CreatedAt);
}
