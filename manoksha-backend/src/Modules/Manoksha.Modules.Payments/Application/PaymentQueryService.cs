using Manoksha.Modules.Payments.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Payments.Application;

public sealed record PaymentStatusChangeDto(string? FromStatus, string ToStatus, string Source, string? Note, DateTimeOffset OccurredAt);

public sealed record PaymentAttemptDto(
    Guid Id,
    string Purpose,
    Guid ReferenceId,
    string ReferenceNumber,
    Guid PayerUserId,
    string Provider,
    decimal Amount,
    string Status,
    string? ProviderOrderRef,
    string? ProviderPaymentRef,
    DateTimeOffset InitiatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason,
    IReadOnlyList<PaymentStatusChangeDto> History);

public sealed record PaymentReconciliationDto(
    Guid Id,
    string CaseNumber,
    Guid PaymentAttemptId,
    string Provider,
    string? ProviderOrderRef,
    string? ProviderPaymentRef,
    decimal ExpectedAmount,
    decimal? PaidAmount,
    Guid PayerUserId,
    string Purpose,
    Guid ReferenceId,
    string ReferenceNumber,
    string ReasonCode,
    string? Detail,
    string Status,
    string? OwnerAction,
    string? ExternalRefundRef,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Owner/Exception-Center views of payments and reconciliation cases (SPEC §28 "Late Payment / Inventory Gone").</summary>
internal sealed class PaymentQueryService(ManokshaDbContext db)
{
    public async Task<IReadOnlyList<PaymentAttemptDto>> ListAttemptsAsync(string? status, string? purpose, Guid? referenceId, CancellationToken ct)
    {
        var q = db.Set<PaymentAttempt>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var match = Enum.GetValues<PaymentStatus>().FirstOrDefault(s => PaymentService.ToCode(s).Equals(status, StringComparison.OrdinalIgnoreCase));
            q = match == default ? q.Where(_ => false) : q.Where(a => a.Status == match);
        }
        if (!string.IsNullOrWhiteSpace(purpose))
        {
            var p = purpose.Trim().ToUpperInvariant();
            q = q.Where(a => a.Purpose == p);
        }
        if (referenceId is { } r)
        {
            q = q.Where(a => a.ReferenceId == r);
        }
        var attempts = await q.OrderByDescending(a => a.InitiatedAt).Take(200).ToListAsync(ct);
        var ids = attempts.Select(a => a.Id).ToList();
        var history = await db.Set<PaymentStatusChange>().AsNoTracking().Where(h => ids.Contains(h.AttemptId)).OrderBy(h => h.OccurredAt).ToListAsync(ct);
        return attempts.Select(a => new PaymentAttemptDto(a.Id, a.Purpose, a.ReferenceId, a.ReferenceNumber, a.PayerUserId, a.Provider, a.Amount, PaymentService.ToCode(a.Status),
            a.ProviderOrderRef, a.ProviderPaymentRef, a.InitiatedAt, a.ExpiresAt, a.CompletedAt, a.FailureReason,
            history.Where(h => h.AttemptId == a.Id).Select(h => new PaymentStatusChangeDto(h.FromStatus is { } f ? PaymentService.ToCode(f) : null, PaymentService.ToCode(h.ToStatus),
                h.Source, h.Note, h.OccurredAt)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<PaymentReconciliationDto>> ListReconciliationsAsync(string? status, CancellationToken ct)
    {
        var q = db.Set<PaymentReconciliation>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReconciliationStatus>(status, true, out var s))
        {
            q = q.Where(r => r.Status == s);
        }
        return (await q.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<PaymentReconciliationDto> GetReconciliationAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Set<PaymentReconciliation>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
              ?? throw new NotFoundException("RECONCILIATION_NOT_FOUND", "Reconciliation case not found."));

    private static PaymentReconciliationDto ToDto(PaymentReconciliation r) =>
        new(r.Id, r.CaseNumber, r.PaymentAttemptId, r.Provider, r.ProviderOrderRef, r.ProviderPaymentRef, r.ExpectedAmount, r.PaidAmount, r.PayerUserId, r.Purpose,
            r.ReferenceId, r.ReferenceNumber, r.ReasonCode, r.Detail, r.Status.ToString(), r.OwnerAction, r.ExternalRefundRef, r.Notes, r.CreatedAt, r.UpdatedAt);
}
