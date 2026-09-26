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
