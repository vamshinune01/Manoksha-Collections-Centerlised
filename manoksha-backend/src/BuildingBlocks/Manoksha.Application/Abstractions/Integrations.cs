namespace Manoksha.Application.Abstractions;

/// <summary>SMS delivery (OTP only in V1). Implementations: provider adapter, development fake.</summary>
public interface ISmsSender
{
    /// <exception cref="SmsDeliveryException">When the message could not be handed to the provider.</exception>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken);
}

public sealed record SmsMessage(string ToE164, string TemplateId, string Body);

public sealed class SmsDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Transactional email. Implementations: provider adapter, SMTP (local Mailpit), development fake.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);

/// <summary>
/// Binary object storage (deposit proofs, product media, PDFs). Implementations: Google Cloud Storage, local disk (development).
/// Containers are logical names mapped to buckets by configuration; <see cref="StorageContainers.Media"/> is the only public one.
/// </summary>
public interface IFileStorage
{
    Task<StoredObject> PutAsync(string container, string objectKey, Stream content, string contentType, CancellationToken cancellationToken,
        string? cacheControl = null);

    Task<Stream> OpenReadAsync(string container, string objectKey, CancellationToken cancellationToken);

    /// <summary>Short-lived URL for private objects (never a permanent public link).</summary>
    Task<Uri> GetReadUrlAsync(string container, string objectKey, TimeSpan validFor, CancellationToken cancellationToken);

    /// <summary>
    /// Short-lived URL the browser uploads one object to directly with HTTP PUT and the given Content-Type (large media never
    /// passes through the API). May be relative (development storage is reached through the web app's API proxy).
    /// </summary>
    Task<string> GetUploadUrlAsync(string container, string objectKey, string contentType, TimeSpan validFor, CancellationToken cancellationToken);

    /// <summary>Permanent URL of an object in the public container (optimized media only).</summary>
    string GetPublicUrl(string container, string objectKey);

    /// <returns>Null when the object does not exist.</returns>
    Task<StoredObjectInfo?> GetInfoAsync(string container, string objectKey, CancellationToken cancellationToken);

    Task DeleteAsync(string container, string objectKey, CancellationToken cancellationToken);
}

public static class StorageContainers
{
    /// <summary>Private: reseller deposit proofs.</summary>
    public const string DepositProofs = "deposit-proofs";

    /// <summary>Private: original (HD) product images and videos as uploaded.</summary>
    public const string MediaOriginals = "media-originals";

    /// <summary>Public, cacheable: optimized product media renditions served to web and mobile.</summary>
    public const string Media = "media";
}

public sealed record StoredObject(string Container, string ObjectKey, long Size, string Sha256);

public sealed record StoredObjectInfo(long Size, string? ContentType);

/// <summary>Web/mobile media optimization. Implementation: SkiaSharp (images, WebP) and ffmpeg (video, H.264 MP4).</summary>
public interface IMediaProcessor
{
    /// <summary>Decodes an image (EXIF orientation applied) and encodes WebP renditions no larger than each requested width.</summary>
    /// <exception cref="MediaProcessingException">Not a readable image.</exception>
    ImageRenditionSet ProcessImage(Stream original, IReadOnlyList<ImageSize> sizes);

    /// <summary>Transcodes a video to a web-friendly MP4 (≤ maxHeight, fast start) and extracts a poster frame (WebP).</summary>
    /// <exception cref="MediaProcessingException">Not a readable video, or longer than allowed.</exception>
    Task<VideoRendition> ProcessVideoAsync(string originalPath, string workDirectory, int maxHeight, TimeSpan maxDuration, CancellationToken cancellationToken);
}

public sealed record ImageSize(string Name, int MaxWidth);

public sealed record ImageRendition(string Name, int Width, int Height, byte[] Webp);

public sealed record ImageRenditionSet(int OriginalWidth, int OriginalHeight, IReadOnlyList<ImageRendition> Renditions);

public sealed record VideoRendition(string Mp4Path, int Width, int Height, double DurationSeconds, byte[] PosterWebp, int PosterWidth, int PosterHeight);

public sealed class MediaProcessingException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// UPI payment gateway (SPEC §14). Implementations: provider adapter (chosen later), development simulator. The backend never
/// trusts a webhook alone: a webhook only triggers a server-to-server <see cref="GetStatusAsync"/>, whose answer is validated.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Provider code used in webhook routes and stored on every attempt (e.g. "simulator").</summary>
    string Provider { get; }

    /// <summary>Creates (or returns the existing) payment session for our attempt id — idempotent per attempt.</summary>
    /// <exception cref="PaymentGatewayException">The provider could not create the session.</exception>
    Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken);

    /// <summary>Authoritative status of a provider order, fetched server-to-server.</summary>
    /// <exception cref="PaymentGatewayException">The provider could not be reached.</exception>
    Task<ProviderPaymentStatus> GetStatusAsync(string providerOrderRef, CancellationToken cancellationToken);

    /// <summary>Verifies the webhook signature and extracts the event identity; null when the signature is invalid.</summary>
    ProviderWebhook? ParseWebhook(IReadOnlyDictionary<string, string> headers, string body);
}

public sealed record PaymentSessionRequest(Guid AttemptId, decimal Amount, string Currency, string Description, DateTimeOffset ExpiresAt, string ReturnPath);

public sealed record PaymentSession(string ProviderOrderRef, string RedirectUrl);

public enum ProviderPaymentState
{
    Pending = 1,
    Success = 2,
    Failed = 3,
}

public sealed record ProviderPaymentStatus(
    string ProviderOrderRef,
    ProviderPaymentState State,
    decimal? PaidAmount,
    string Currency,
    string? ProviderPaymentRef,
    string? FailureReason);

public sealed record ProviderWebhook(string EventId, string ProviderOrderRef, string EventType);

public sealed class PaymentGatewayException(string message, Exception? inner = null) : Exception(message, inner);
