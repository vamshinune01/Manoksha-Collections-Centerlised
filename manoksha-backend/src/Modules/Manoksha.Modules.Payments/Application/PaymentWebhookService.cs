using Manoksha.Application.Abstractions;
using Manoksha.Modules.Payments.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Payments.Application;

public enum WebhookReceipt
{
    Accepted = 1,
    Duplicate = 2,
    InvalidSignature = 3,
    UnknownProvider = 4,
}

/// <summary>
/// Webhook inbox (design §13): verify the signature, store the event durably (unique per provider event id, so repeats have no
/// effect), then process it. Processing never trusts the payload — it re-queries the provider and applies that answer. An event
/// that fails to process stays in the inbox and is retried by the poller.
/// </summary>
internal sealed class PaymentWebhookService(
    ManokshaDbContext db,
    IPaymentGateway gateway,
    PaymentService payments,
    IClock clock,
    ILogger<PaymentWebhookService> logger)
{
    public const int MaxPayloadBytes = 64 * 1024;

    public async Task<WebhookReceipt> ReceiveAsync(string provider, IReadOnlyDictionary<string, string> headers, string body, CancellationToken ct)
    {
        if (!string.Equals(provider, gateway.Provider, StringComparison.Ordinal))
        {
            return WebhookReceipt.UnknownProvider;
        }
        var webhook = gateway.ParseWebhook(headers, body);
        if (webhook is null)
        {
            logger.LogWarning("Rejected {Provider} payment webhook with an invalid signature or body", provider);
            return WebhookReceipt.InvalidSignature;
        }

        var id = Uuid7.NewGuid();
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO payments.provider_events (id, provider, provider_event_id, provider_order_ref, event_type, payload, received_at, attempts)
            VALUES ({id}, {provider}, {webhook.EventId}, {webhook.ProviderOrderRef}, {webhook.EventType}, CAST({body} AS jsonb), {clock.UtcNow}, 0)
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            return WebhookReceipt.Duplicate;
        }
        await ProcessAsync(id, ct);
        return WebhookReceipt.Accepted;
    }

    /// <summary>Processes one stored event; failures are logged and left for the poller to retry.</summary>
    public async Task ProcessAsync(Guid eventId, CancellationToken ct)
    {
        var e = await db.Set<ProviderEvent>().AsNoTracking().SingleAsync(x => x.Id == eventId, ct);
        string result;
        try
        {
            var attemptId = await db.Set<PaymentAttempt>().AsNoTracking()
                .Where(a => a.Provider == e.Provider && a.ProviderOrderRef == e.ProviderOrderRef).Select(a => (Guid?)a.Id).SingleOrDefaultAsync(ct);
            if (attemptId is null)
            {
                result = "UNKNOWN_PAYMENT";
            }
            else
            {
                var attempt = await payments.SyncCoreAsync(attemptId.Value, "webhook", ct);
                result = PaymentService.ToCode(attempt.Status);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Processing payment event {EventId} failed; it will be retried", eventId);
            await db.Set<ProviderEvent>().Where(x => x.Id == eventId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
            return;
        }
        var now = clock.UtcNow;
        await db.Set<ProviderEvent>().Where(x => x.Id == eventId && x.ProcessedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProcessedAt, now).SetProperty(x => x.ProcessingResult, result).SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
    }
}
