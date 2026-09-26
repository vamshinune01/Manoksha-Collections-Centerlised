using Manoksha.Application.Abstractions;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Payments.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Payments.Application;

public sealed record PaymentConfirmed(Guid AttemptId, string Purpose, Guid ReferenceId, string ReferenceNumber, decimal Amount, string Status) : IIntegrationEvent
{
    public static string EventType => "payments.payment_confirmed";
}

/// <summary>CRITICAL: money received that could not be applied (SPEC §14.2, §36). The Owner is alerted from the outbox.</summary>
public sealed record PaymentReconciliationRequired(Guid CaseId, string CaseNumber, string ReasonCode, decimal ExpectedAmount, decimal? PaidAmount, string ReferenceNumber)
    : IIntegrationEvent
{
    public static string EventType => "payments.reconciliation_required";
}

/// <summary>
/// Applies the provider's authoritative answer to one attempt, in one transaction with the attempt row locked (design §11, §13).
/// Idempotent: settled attempts ignore further events, so duplicate or concurrent callbacks never double-confirm, double-credit
/// or double-consume stock (SPEC §14.2, §32, §33).
/// </summary>
internal sealed class PaymentProcessor(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IEnumerable<IPaymentPurposeHandler> handlers,
    IAuditWriter audit,
    IOutbox outbox,
    IClock clock)
{
    public Task<string> ApplyAsync(Guid attemptId, ProviderPaymentStatus status, string source, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var attempt = await LockAsync(attemptId, innerCt);
            var result = await ApplyLockedAsync(attempt, status, source, innerCt);
            await db.SaveChangesAsync(innerCt);
            return result;
        }, ct);

    /// <summary>The provider session could not be created (or never was, before the window ended): nothing was paid.</summary>
    public Task FailBeforeSessionAsync(Guid attemptId, string reason, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var attempt = await LockAsync(attemptId, innerCt);
            if (attempt.Status != PaymentStatus.Initiated)
            {
                return;
            }
            Move(attempt, PaymentStatus.Failed, "session", reason);
            await Handler(attempt).OnPaymentFailedAsync(attempt.ReferenceId, reason, innerCt);
            await audit.RecordAsync(new AuditRecord("payments.attempt.initiation_failed", "PaymentAttempt", attempt.Id.ToString(),
                After: new { attempt.Purpose, attempt.ReferenceNumber, attempt.Amount, reason }), innerCt);
            await db.SaveChangesAsync(innerCt);
        }, ct);

    public Task SessionCreatedAsync(Guid attemptId, PaymentSession session, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var attempt = await LockAsync(attemptId, innerCt);
            if (attempt.Status != PaymentStatus.Initiated)
            {
                return;
            }
            var from = attempt.Status;
            attempt.SessionCreated(session.ProviderOrderRef, session.RedirectUrl, clock.UtcNow);
            db.Add(new PaymentStatusChange(attempt.Id, from, attempt.Status, "session", $"Provider order {session.ProviderOrderRef}", clock.UtcNow));
            await db.SaveChangesAsync(innerCt);
        }, ct);

    private async Task<string> ApplyLockedAsync(PaymentAttempt attempt, ProviderPaymentStatus status, string source, CancellationToken ct)
    {
        var now = clock.UtcNow;
        attempt.SchedulePoll(now);
        if (attempt.ProviderOrderRef is null || !string.Equals(attempt.ProviderOrderRef, status.ProviderOrderRef, StringComparison.Ordinal))
        {
            return "IGNORED_UNKNOWN_PROVIDER_ORDER";
        }

        switch (status.State)
        {
            case ProviderPaymentState.Pending:
                if (attempt.IsLive && now >= attempt.ExpiresAt)
                {
                    Move(attempt, PaymentStatus.Expired, source, "The payment window ended without a confirmed payment.");
                    await Handler(attempt).OnPaymentExpiredAsync(attempt.ReferenceId, ct);
                    return "EXPIRED";
                }
                return "PENDING";

            case ProviderPaymentState.Failed:
                if (!attempt.IsLive)
                {
                    return "IGNORED_NOT_LIVE";
                }
                var reason = string.IsNullOrWhiteSpace(status.FailureReason) ? "The payment was not completed." : status.FailureReason;
                Move(attempt, PaymentStatus.Failed, source, reason);
                await Handler(attempt).OnPaymentFailedAsync(attempt.ReferenceId, reason, ct);
                return "FAILED";

            case ProviderPaymentState.Success:
                return await ApplySuccessAsync(attempt, status, source, now, ct);

            default:
                return "IGNORED_UNKNOWN_STATE";
        }
    }

    private async Task<string> ApplySuccessAsync(PaymentAttempt attempt, ProviderPaymentStatus status, string source, DateTimeOffset now, CancellationToken ct)
    {
        if (attempt.IsSettled)
        {
            return "DUPLICATE_IGNORED";
        }
        attempt.RecordProviderPayment(status.ProviderPaymentRef);
        var handler = Handler(attempt);

        // Never trust the event alone: amount and currency must match what the backend asked for (design §13 point 4).
        if (status.PaidAmount != attempt.Amount || !string.Equals(status.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase))
        {
            var wasLive = attempt.IsLive;
            Move(attempt, PaymentStatus.ReconciliationRequired, source, "The paid amount does not match the amount due.");
            if (wasLive)
            {
                await handler.OnPaymentFailedAsync(attempt.ReferenceId, "Paid amount did not match the amount due.", ct);
            }
            await OpenReconciliationAsync(attempt, status.PaidAmount, "AMOUNT_MISMATCH",
                $"Expected ₹{attempt.Amount:N2} {attempt.Currency}, provider reported {status.PaidAmount?.ToString("N2") ?? "no amount"} {status.Currency}.", ct);
            return "RECONCILIATION_REQUIRED";
        }

        var withinWindow = attempt.IsLive && now < attempt.ExpiresAt;
        if (!withinWindow)
        {
            Move(attempt, PaymentStatus.LateSuccessRecheck, source, null);
        }
        var result = await handler.OnPaymentSucceededAsync(new PaymentSuccess(attempt.Id, attempt.ReferenceId, attempt.Amount, attempt.ProviderPaymentRef, withinWindow), ct);
        if (result.Outcome != PaymentOutcome.Confirmed && attempt.IsLive)
        {
            Move(attempt, PaymentStatus.LateSuccessRecheck, source, null);
        }

        switch (result.Outcome)
        {
            case PaymentOutcome.Confirmed:
                Move(attempt, PaymentStatus.Success, source, null);
                break;
            case PaymentOutcome.Recovered:
                Move(attempt, PaymentStatus.OrderRecovered, source, null);
                break;
            default:
                Move(attempt, PaymentStatus.ReconciliationRequired, source, result.Detail ?? "The payment could not be applied.");
                await OpenReconciliationAsync(attempt, status.PaidAmount, result.ReasonCode ?? "NOT_FULFILLABLE", result.Detail, ct);
                return "RECONCILIATION_REQUIRED";
        }

        await audit.RecordAsync(new AuditRecord("payments.attempt.succeeded", "PaymentAttempt", attempt.Id.ToString(),
            After: new { attempt.Purpose, attempt.ReferenceNumber, attempt.Amount, attempt.ProviderOrderRef, attempt.ProviderPaymentRef, status = attempt.Status.ToString(), source }), ct);
        outbox.Enqueue(new PaymentConfirmed(attempt.Id, attempt.Purpose, attempt.ReferenceId, attempt.ReferenceNumber, attempt.Amount, attempt.Status.ToString()));
        return attempt.Status == PaymentStatus.Success ? "SUCCESS" : "RECOVERED";
    }

    private async Task OpenReconciliationAsync(PaymentAttempt attempt, decimal? paidAmount, string reasonCode, string? detail, CancellationToken ct)
    {
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('payments.reconciliation_seq') AS \"Value\"").SingleAsync(ct);
        var rec = new PaymentReconciliation($"MC-REC-{seq:D6}", attempt, paidAmount, reasonCode, detail, clock.UtcNow);
        db.Add(rec);
        await audit.RecordAsync(new AuditRecord("payments.reconciliation.opened", "PaymentReconciliation", rec.Id.ToString(),
            After: new { rec.CaseNumber, reasonCode, detail, attempt.Purpose, attempt.ReferenceNumber, attempt.Amount, paidAmount, attempt.ProviderOrderRef, attempt.ProviderPaymentRef }), ct);
        outbox.Enqueue(new PaymentReconciliationRequired(rec.Id, rec.CaseNumber, reasonCode, attempt.Amount, paidAmount, attempt.ReferenceNumber));
    }

    private void Move(PaymentAttempt attempt, PaymentStatus to, string source, string? reason)
    {
        var from = attempt.MoveTo(to, reason, clock.UtcNow);
        db.Add(new PaymentStatusChange(attempt.Id, from, to, source, reason, clock.UtcNow));
    }

    private IPaymentPurposeHandler Handler(PaymentAttempt attempt) =>
        handlers.SingleOrDefault(h => h.Purpose == attempt.Purpose) ?? throw new InvalidOperationException($"No payment handler for purpose {attempt.Purpose}.");

    private async Task<PaymentAttempt> LockAsync(Guid attemptId, CancellationToken ct)
    {
        db.Forget<PaymentAttempt>(a => a.Id == attemptId);
        return await db.Set<PaymentAttempt>().FromSqlInterpolated($"SELECT *, xmin FROM payments.payment_attempts WHERE id = {attemptId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
    }
}
