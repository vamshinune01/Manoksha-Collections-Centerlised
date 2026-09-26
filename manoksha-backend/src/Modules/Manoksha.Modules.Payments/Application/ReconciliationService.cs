using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Payments.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Payments.Application;

/// <param name="Action">NOTE, REFUND_INITIATED, REFUND_COMPLETED or RESOLVED.</param>
/// <param name="Note">Required: what was done / decided.</param>
/// <param name="ExternalRefundRef">Required for REFUND_COMPLETED: the refund reference from the bank/gateway.</param>
public sealed record ReconciliationActionRequest(string Action, string Note, string? ExternalRefundRef);

public sealed record ReconciliationHistoryDto(string Action, string FromStatus, string ToStatus, string Note, string? ExternalRefundRef, Guid ActorUserId, DateTimeOffset OccurredAt);

/// <summary>
/// Owner actions on a payment reconciliation case (SPEC §36): Owner notes/action, Refund Initiated / Refund Completed markers with
/// the external refund reference, resolution. No automated refund is made. Every change is audited and kept in history.
/// </summary>
internal sealed class ReconciliationService(ManokshaDbContext db, IUnitOfWork unitOfWork, IAuditWriter audit, ICurrentUser currentUser, IClock clock,
    PaymentQueryService queries)
{
    public async Task<PaymentReconciliationDto> ActAsync(Guid id, ReconciliationActionRequest request, CancellationToken ct)
    {
        var action = (request.Action ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "NOTE" => ReconciliationAction.Note,
            "REFUND_INITIATED" => ReconciliationAction.RefundInitiated,
            "REFUND_COMPLETED" => ReconciliationAction.RefundCompleted,
            "RESOLVED" => ReconciliationAction.Resolved,
            _ => throw new BusinessRuleException("RECONCILIATION_ACTION_INVALID", "Choose NOTE, REFUND_INITIATED, REFUND_COMPLETED or RESOLVED.", 400),
        };
        var note = request.Note?.Trim() ?? string.Empty;
        if (note.Length is < 3 or > 2000)
        {
            throw new BusinessRuleException("REASON_REQUIRED", "Describe the action taken (3–2000 characters).", 400);
        }
        var refundRef = string.IsNullOrWhiteSpace(request.ExternalRefundRef) ? null : request.ExternalRefundRef.Trim();
        if (refundRef is { Length: > 100 })
        {
            throw new BusinessRuleException("REFUND_REFERENCE_INVALID", "The refund reference is too long.", 400);
        }

        await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            db.Forget<PaymentReconciliation>(r => r.Id == id);
            var rec = await db.Set<PaymentReconciliation>()
                .FromSqlInterpolated($"SELECT *, xmin FROM payments.reconciliations WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(innerCt)
                ?? throw new NotFoundException("RECONCILIATION_NOT_FOUND", "Reconciliation case not found.");
            var now = clock.UtcNow;
            var from = rec.Apply(action, note, refundRef, now);
            db.Add(new ReconciliationHistory(rec.Id, action, from, rec.Status, note, refundRef, currentUser.UserId, now));
            await audit.RecordAsync(new AuditRecord("payments.reconciliation." + action.ToString().ToLowerInvariant(), "PaymentReconciliation", rec.Id.ToString(),
                Before: new { status = from.ToString() },
                After: new { rec.CaseNumber, status = rec.Status.ToString(), note, externalRefundRef = refundRef }, Reason: note), innerCt);
            await db.SaveChangesAsync(innerCt);
        }, ct);
        return await queries.GetReconciliationAsync(id, ct);
    }

    public async Task<IReadOnlyList<ReconciliationHistoryDto>> HistoryAsync(Guid id, CancellationToken ct) =>
        (await db.Set<ReconciliationHistory>().AsNoTracking().Where(h => h.ReconciliationId == id).OrderBy(h => h.OccurredAt).ToListAsync(ct))
        .Select(h => new ReconciliationHistoryDto(Code(h.Action), h.FromStatus.ToString(), h.ToStatus.ToString(), h.Note, h.ExternalRefundRef, h.ActorUserId, h.OccurredAt))
        .ToList();

    private static string Code(ReconciliationAction a) => a switch
    {
        ReconciliationAction.RefundInitiated => "REFUND_INITIATED",
        ReconciliationAction.RefundCompleted => "REFUND_COMPLETED",
        ReconciliationAction.Resolved => "RESOLVED",
        _ => "NOTE",
    };
}

/// <summary>
/// CRITICAL alert for a payment that needs reconciliation (SPEC §14.2 "alert Owner"). Until the notification channels arrive
/// (Phase 9) it is raised as a CRITICAL log entry (for log-based alerting) and shown to the Owner on every admin page.
/// </summary>
internal sealed class ReconciliationAlertHandler(ILogger<ReconciliationAlertHandler> logger) : IOutboxEventHandler
{
    public string EventType => PaymentReconciliationRequired.EventType;

    public Task HandleAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var e = JsonSerializer.Deserialize<PaymentReconciliationRequired>(payloadJson, JsonDefaults.Options);
        logger.LogCritical("PAYMENT RECONCILIATION REQUIRED {CaseNumber} for {Reference}: {Reason}, expected {Expected}, paid {Paid}",
            e?.CaseNumber, e?.ReferenceNumber, e?.ReasonCode, e?.ExpectedAmount, e?.PaidAmount);
        return Task.CompletedTask;
    }
}
