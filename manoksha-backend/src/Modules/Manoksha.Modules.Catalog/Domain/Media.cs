using Manoksha.SharedKernel;

namespace Manoksha.Modules.Catalog.Domain;

internal enum MediaKind
{
    Image = 1,
    Video = 2,
}

internal enum MediaStatus
{
    /// <summary>Upload URL issued; the browser is uploading the original directly to storage.</summary>
    Uploading = 1,
    Ready = 2,
    Failed = 3,
    Deleted = 4,
}

/// <summary>One optimized file served to customers (WebP image size, MP4 video, poster).</summary>
internal sealed record MediaRendition(string Name, string ObjectKey, string ContentType, int Width, int Height, long Bytes);

/// <summary>
/// A product image or video (ADR-001 §29). PostgreSQL keeps only metadata and object paths; the HD original lives in the private
/// media bucket and the optimized renditions in the public one. Customers are only ever served renditions.
/// </summary>
internal sealed class ProductMedia : Entity
{
    private ProductMedia()
    {
    }

    public ProductMedia(Guid id, Guid productId, MediaKind kind, string originalKey, string originalFileName, string originalContentType, long declaredBytes,
        Guid createdBy, DateTimeOffset now)
        : base(id)
    {
        ProductId = productId;
        Kind = kind;
        Status = MediaStatus.Uploading;
        OriginalKey = originalKey;
        OriginalFileName = originalFileName;
        OriginalContentType = originalContentType;
        OriginalBytes = declaredBytes;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid ProductId { get; private set; }

    public MediaKind Kind { get; private set; }

    public MediaStatus Status { get; private set; }

    public int SortOrder { get; private set; }

    public string? AltText { get; private set; }

    public string OriginalKey { get; private set; } = default!;

    public string OriginalFileName { get; private set; } = default!;

    public string OriginalContentType { get; private set; } = default!;

    public long OriginalBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public double? DurationSeconds { get; private set; }

    public List<MediaRendition> Renditions { get; private set; } = [];

    public string? FailureReason { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void MarkReady(long originalBytes, int width, int height, double? duration, List<MediaRendition> renditions, int sortOrder, DateTimeOffset now)
    {
        Ensure(MediaStatus.Uploading);
        OriginalBytes = originalBytes;
        Width = width;
        Height = height;
        DurationSeconds = duration;
        Renditions = renditions;
        SortOrder = sortOrder;
        Status = MediaStatus.Ready;
        ProcessedAt = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Ensure(MediaStatus.Uploading);
        Status = MediaStatus.Failed;
        FailureReason = reason;
        ProcessedAt = now;
    }

    public void MarkDeleted()
    {
        if (Status == MediaStatus.Deleted)
        {
            throw new NotFoundException("MEDIA_NOT_FOUND", "Media not found.");
        }
        Status = MediaStatus.Deleted;
    }

    public void SetAltText(string? alt) => AltText = string.IsNullOrWhiteSpace(alt) ? null : alt.Trim();

    public void SetSortOrder(int order) => SortOrder = order;

    private void Ensure(MediaStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessRuleException("MEDIA_STATUS_INVALID", $"This media is {Status}.", 409);
        }
    }
}
