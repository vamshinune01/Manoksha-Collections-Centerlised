using Manoksha.Application.Abstractions;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Payments.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Payments.Application;

internal sealed class PaymentService(
    ManokshaDbContext db,
    IPaymentGateway gateway,
    PaymentProcessor processor,
    IClock clock,
    ILogger<PaymentService> logger) : IPayments
{
    public async Task<PaymentAttemptInfo> CreateAttemptAsync(NewPaymentAttempt request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0 || !Money.HasValidScale(request.Amount))
        {
            throw new BusinessRuleException("AMOUNT_INVALID", "The amount to pay must be greater than zero with at most 2 decimals.", 400);
        }
        var attempt = new PaymentAttempt(request.Purpose, request.ReferenceId, request.ReferenceNumber, request.PayerUserId, gateway.Provider, request.Amount,
            request.ExpiresAt, request.Description, request.ReturnPath, clock.UtcNow);
        db.Add(attempt);
        db.Add(new PaymentStatusChange(attempt.Id, null, attempt.Status, "checkout", null, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return ToInfo(attempt);
    }

    public async Task<PaymentAttemptInfo> StartSessionAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await FindAsync(attemptId, cancellationToken);
        if (attempt.Status != PaymentStatus.Initiated)
        {
            return ToInfo(attempt);
        }
        if (clock.UtcNow >= attempt.ExpiresAt)
        {
            await processor.FailBeforeSessionAsync(attemptId, "The payment could not be started before the window ended.", cancellationToken);
            return ToInfo(await FindAsync(attemptId, cancellationToken));
        }

        // The provider call is made outside any transaction or row lock (design §11).
        PaymentSession session;
        try
        {
            session = await gateway.CreateSessionAsync(
                new PaymentSessionRequest(attempt.Id, attempt.Amount, attempt.Currency, attempt.Description, attempt.ExpiresAt, attempt.ReturnPath), cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Payment session creation failed for attempt {AttemptId}", attemptId);
            await processor.FailBeforeSessionAsync(attemptId, "The payment could not be started. Nothing was charged.", cancellationToken);
            return ToInfo(await FindAsync(attemptId, cancellationToken));
        }
        await processor.SessionCreatedAsync(attemptId, session, cancellationToken);
        return ToInfo(await FindAsync(attemptId, cancellationToken));
    }

    public async Task<PaymentAttemptInfo> SyncAsync(Guid attemptId, CancellationToken cancellationToken = default) =>
        ToInfo(await SyncCoreAsync(attemptId, "status-check", cancellationToken));

    public async Task<PaymentAttemptInfo?> FindLatestAsync(string purpose, Guid referenceId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().Where(a => a.Purpose == purpose && a.ReferenceId == referenceId)
            .OrderByDescending(a => a.InitiatedAt).FirstOrDefaultAsync(cancellationToken);
        return attempt is null ? null : ToInfo(attempt);
    }

    /// <summary>Asks the provider (outside any lock), then applies the answer under the attempt lock.</summary>
    internal async Task<PaymentAttempt> SyncCoreAsync(Guid attemptId, string source, CancellationToken ct)
    {
        var attempt = await FindAsync(attemptId, ct);
        if (attempt.Status == PaymentStatus.Initiated)
        {
            // The session was never started (e.g. the process stopped after checkout): start it now — the provider call is
            // idempotent per attempt — or fail it once the window has ended.
            await StartSessionAsync(attemptId, ct);
            attempt = await FindAsync(attemptId, ct);
        }
        if (attempt.ProviderOrderRef is null || attempt.IsSettled)
        {
            return attempt;
        }

        ProviderPaymentStatus status;
        try
        {
            status = await gateway.GetStatusAsync(attempt.ProviderOrderRef, ct);
        }
        catch (PaymentGatewayException ex)
        {
            logger.LogWarning(ex, "Payment status query failed for attempt {AttemptId}", attemptId);
            var retryAt = clock.UtcNow.AddMinutes(1);
            await db.Set<PaymentAttempt>().Where(a => a.Id == attemptId).ExecuteUpdateAsync(s => s.SetProperty(a => a.NextPollAt, retryAt), ct);
            return attempt;
        }
        await processor.ApplyAsync(attemptId, status, source, ct);
        return await FindAsync(attemptId, ct);
    }

    private async Task<PaymentAttempt> FindAsync(Guid attemptId, CancellationToken ct) =>
        await db.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == attemptId, ct)
        ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");

    internal static PaymentAttemptInfo ToInfo(PaymentAttempt a) =>
        new(a.Id, a.Purpose, a.ReferenceId, a.ReferenceNumber, a.PayerUserId, a.Provider, a.Amount, ToCode(a.Status), a.ProviderOrderRef, a.ProviderPaymentRef,
            a.RedirectUrl, a.InitiatedAt, a.ExpiresAt, a.CompletedAt, a.FailureReason);

    /// <summary>SPEC §27.2 status names.</summary>
    internal static string ToCode(PaymentStatus s) => s switch
    {
        PaymentStatus.Initiated => "INITIATED",
        PaymentStatus.Pending => "PENDING",
        PaymentStatus.Success => "SUCCESS",
        PaymentStatus.Failed => "FAILED",
        PaymentStatus.Expired => "EXPIRED",
        PaymentStatus.LateSuccessRecheck => "LATE_SUCCESS_RECHECK",
        PaymentStatus.OrderRecovered => "ORDER_RECOVERED",
        PaymentStatus.ReconciliationRequired => "PAYMENT_RECONCILIATION_REQUIRED",
        _ => s.ToString(),
    };
}
