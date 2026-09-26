using Manoksha.Modules.Payments.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Payments.Application;

/// <summary>
/// Missed-webhook safety net (design §13 point 5): retries unprocessed inbox events and asks the provider about live attempts
/// and recently failed/expired ones, so a late success is discovered even when no callback ever arrives.
/// </summary>
internal sealed class PaymentPoller(ManokshaDbContext db, PaymentService payments, PaymentWebhookService webhooks, IClock clock, ILogger<PaymentPoller> logger)
{
    /// <summary>How long after initiation a failed/expired attempt is still checked for a delayed success.</summary>
    public static readonly TimeSpan LateSuccessLookback = TimeSpan.FromHours(24);

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var handled = 0;

        var retryBefore = now.AddSeconds(-30);
        var events = await db.Set<ProviderEvent>().AsNoTracking()
            .Where(e => e.ProcessedAt == null && e.ReceivedAt < retryBefore && e.Attempts < 20)
            .OrderBy(e => e.ReceivedAt).Take(50).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in events)
        {
            await webhooks.ProcessAsync(id, ct);
            handled++;
        }

        var since = now - LateSuccessLookback;
        PaymentStatus[] pollable = [PaymentStatus.Initiated, PaymentStatus.Pending, PaymentStatus.Failed, PaymentStatus.Expired];
        var attempts = await db.Set<PaymentAttempt>().AsNoTracking()
            .Where(a => pollable.Contains(a.Status) && a.NextPollAt != null && a.NextPollAt <= now && a.InitiatedAt >= since)
            .OrderBy(a => a.NextPollAt).Take(100).Select(a => a.Id).ToListAsync(ct);
        foreach (var id in attempts)
        {
            try
            {
                await payments.SyncCoreAsync(id, "poll", ct);
                handled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Polling payment attempt {AttemptId} failed", id);
            }
        }
        return handled;
    }
}
