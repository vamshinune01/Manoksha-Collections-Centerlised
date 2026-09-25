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

/// <summary>Binary object storage (deposit proofs, product images, PDFs). Implementations: GCS, local disk.</summary>
public interface IFileStorage
{
    Task<StoredObject> PutAsync(string container, string objectKey, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string container, string objectKey, CancellationToken cancellationToken);

    /// <summary>Short-lived URL for private objects (never a permanent public link).</summary>
    Task<Uri> GetReadUrlAsync(string container, string objectKey, TimeSpan validFor, CancellationToken cancellationToken);
}

public sealed record StoredObject(string Container, string ObjectKey, long Size, string Sha256);
