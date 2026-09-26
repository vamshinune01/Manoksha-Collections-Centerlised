using Manoksha.SharedKernel;

namespace Manoksha.Modules.Payments.Domain;

/// <summary>SPEC §27.2. SUCCESS, ORDER_RECOVERED and PAYMENT_RECONCILIATION_REQUIRED are sinks: later provider events are ignored.</summary>
internal enum PaymentStatus
{
    Initiated = 1,
    Pending = 2,
    Success = 3,
    Failed = 4,
    Expired = 5,
    LateSuccessRecheck = 6,
    OrderRecovered = 7,
    ReconciliationRequired = 8,
}

internal sealed class PaymentAttempt : Entity
{
    private static readonly Dictionary<PaymentStatus, PaymentStatus[]> Allowed = new()
    {
        [PaymentStatus.Initiated] = [PaymentStatus.Pending, PaymentStatus.Failed, PaymentStatus.Expired, PaymentStatus.Success, PaymentStatus.LateSuccessRecheck, PaymentStatus.ReconciliationRequired],
        [PaymentStatus.Pending] = [PaymentStatus.Success, PaymentStatus.Failed, PaymentStatus.Expired, PaymentStatus.LateSuccessRecheck, PaymentStatus.ReconciliationRequired],
        // A success reported after a failure/expiry is money received: it is rechecked, never ignored.
        [PaymentStatus.Failed] = [PaymentStatus.LateSuccessRecheck, PaymentStatus.ReconciliationRequired],
        [PaymentStatus.Expired] = [PaymentStatus.LateSuccessRecheck, PaymentStatus.ReconciliationRequired],
        [PaymentStatus.LateSuccessRecheck] = [PaymentStatus.Success, PaymentStatus.OrderRecovered, PaymentStatus.ReconciliationRequired],
        [PaymentStatus.Success] = [],
        [PaymentStatus.OrderRecovered] = [],
        [PaymentStatus.ReconciliationRequired] = [],
    };

    private PaymentAttempt()
    {
    }

    public PaymentAttempt(string purpose, Guid referenceId, string referenceNumber, Guid payerUserId, string provider, decimal amount, DateTimeOffset expiresAt,
        string description, string returnPath, DateTimeOffset now)
    {
        Purpose = purpose;
        ReferenceId = referenceId;
        ReferenceNumber = referenceNumber;
        PayerUserId = payerUserId;
        Provider = provider;
        Amount = amount;
        Currency = "INR";
        Status = PaymentStatus.Initiated;
        Description = description;
        ReturnPath = returnPath;
        InitiatedAt = now;
        ExpiresAt = expiresAt;
        NextPollAt = now.AddMinutes(1);
    }

    public string Purpose { get; private set; } = default!;

    public Guid ReferenceId { get; private set; }

    public string ReferenceNumber { get; private set; } = default!;

    public Guid PayerUserId { get; private set; }

    public string Provider { get; private set; } = default!;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = default!;

    public PaymentStatus Status { get; private set; }

    public string? ProviderOrderRef { get; private set; }

    public string? ProviderPaymentRef { get; private set; }

    public string? RedirectUrl { get; private set; }

    public string Description { get; private set; } = default!;

    public string ReturnPath { get; private set; } = default!;

    public DateTimeOffset InitiatedAt { get; private set; }

    /// <summary>Same deadline as the inventory reservation (design §14).</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>When the poller should next ask the provider (missed webhooks, late successes — design §13).</summary>
    public DateTimeOffset? NextPollAt { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsLive => Status is PaymentStatus.Initiated or PaymentStatus.Pending;

    public bool IsSettled => Status is PaymentStatus.Success or PaymentStatus.OrderRecovered or PaymentStatus.ReconciliationRequired;

    public void SessionCreated(string providerOrderRef, string redirectUrl, DateTimeOffset now)
    {
        ProviderOrderRef = providerOrderRef;
        RedirectUrl = redirectUrl;
        MoveTo(PaymentStatus.Pending, null, now);
    }

    public void RecordProviderPayment(string? providerPaymentRef) => ProviderPaymentRef ??= providerPaymentRef;

    public PaymentStatus MoveTo(PaymentStatus to, string? reason, DateTimeOffset now)
    {
        if (!Allowed[Status].Contains(to))
        {
            throw new BusinessRuleException("PAYMENT_STATUS_INVALID", $"A payment cannot move from {Status} to {to}.", 409);
        }
        var from = Status;
        Status = to;
        if (reason is not null)
        {
            FailureReason = reason;
        }
        if (to is not (PaymentStatus.Pending or PaymentStatus.LateSuccessRecheck))
        {
            CompletedAt ??= now;
        }
        SchedulePoll(now);
        return from;
    }

    /// <summary>Live attempts are polled every 30 s; failed/expired ones every 5 min for late successes; settled ones never.</summary>
    public void SchedulePoll(DateTimeOffset now, TimeSpan? retryAfter = null) =>
        NextPollAt = retryAfter is { } r ? now + r
            : IsLive ? now.AddSeconds(30)
            : Status is PaymentStatus.Failed or PaymentStatus.Expired ? now.AddMinutes(5)
            : null;
}

internal sealed class PaymentStatusChange : Entity
{
    private PaymentStatusChange()
    {
    }

    public PaymentStatusChange(Guid attemptId, PaymentStatus? from, PaymentStatus to, string source, string? note, DateTimeOffset now)
    {
        AttemptId = attemptId;
        FromStatus = from;
        ToStatus = to;
        Source = source;
        Note = note;
        OccurredAt = now;
    }

    public Guid AttemptId { get; private set; }

    public PaymentStatus? FromStatus { get; private set; }

    public PaymentStatus ToStatus { get; private set; }

    /// <summary>checkout / webhook / poll / status-check / session.</summary>
    public string Source { get; private set; } = default!;

    public string? Note { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Webhook inbox (design §13): unique per provider event, so a repeated callback has no second effect.</summary>
internal sealed class ProviderEvent : Entity
{
    private ProviderEvent()
    {
    }

    public string Provider { get; private set; } = default!;

    public string ProviderEventId { get; private set; } = default!;

    public string ProviderOrderRef { get; private set; } = default!;

    public string EventType { get; private set; } = default!;

    public string Payload { get; private set; } = default!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? ProcessingResult { get; private set; }

    public int Attempts { get; private set; }
}

internal enum ReconciliationStatus
{
    Open = 1,
    RefundInitiated = 2,
    RefundCompleted = 3,
    Resolved = 4,
}

/// <summary>
/// Minimum reconciliation safeguard (SPEC §36): money was received but the purpose could not be fulfilled. No automated refund —
/// the Owner records actions and external refund references (Owner actions arrive with the Exception Center, Phase 9).
/// </summary>
internal sealed class PaymentReconciliation : Entity
{
    private PaymentReconciliation()
    {
    }

    public PaymentReconciliation(string caseNumber, PaymentAttempt attempt, decimal? paidAmount, string reasonCode, string? detail, DateTimeOffset now)
    {
        CaseNumber = caseNumber;
        PaymentAttemptId = attempt.Id;
        Provider = attempt.Provider;
        ProviderOrderRef = attempt.ProviderOrderRef;
        ProviderPaymentRef = attempt.ProviderPaymentRef;
        ExpectedAmount = attempt.Amount;
        PaidAmount = paidAmount;
        PayerUserId = attempt.PayerUserId;
        Purpose = attempt.Purpose;
        ReferenceId = attempt.ReferenceId;
        ReferenceNumber = attempt.ReferenceNumber;
        ReasonCode = reasonCode;
        Detail = detail;
        Status = ReconciliationStatus.Open;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string CaseNumber { get; private set; } = default!;

    public Guid PaymentAttemptId { get; private set; }

    public string Provider { get; private set; } = default!;

    public string? ProviderOrderRef { get; private set; }

    public string? ProviderPaymentRef { get; private set; }

    public decimal ExpectedAmount { get; private set; }

    public decimal? PaidAmount { get; private set; }

    public Guid PayerUserId { get; private set; }

    public string Purpose { get; private set; } = default!;

    public Guid ReferenceId { get; private set; }

    public string ReferenceNumber { get; private set; } = default!;

    public string ReasonCode { get; private set; } = default!;

    public string? Detail { get; private set; }

    public ReconciliationStatus Status { get; private set; }

    public string? OwnerAction { get; private set; }

    public string? ExternalRefundRef { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Owner actions (SPEC §36). Refunds happen outside the application; the case records the markers and the external reference.
    /// Open → RefundInitiated → RefundCompleted → Resolved; Open/RefundInitiated may also be Resolved (e.g. goods supplied another
    /// way), always with a note. A note can be added at any time.
    /// </summary>
    public ReconciliationStatus Apply(ReconciliationAction action, string note, string? externalRefundRef, DateTimeOffset now)
    {
        var from = Status;
        switch (action)
        {
            case ReconciliationAction.Note:
                break;
            case ReconciliationAction.RefundInitiated:
                Ensure(ReconciliationStatus.Open);
                Status = ReconciliationStatus.RefundInitiated;
                break;
            case ReconciliationAction.RefundCompleted:
                Ensure(ReconciliationStatus.Open, ReconciliationStatus.RefundInitiated);
                if (string.IsNullOrWhiteSpace(externalRefundRef))
                {
                    throw new BusinessRuleException("REFUND_REFERENCE_REQUIRED", "Enter the external refund reference.", 400);
                }
                Status = ReconciliationStatus.RefundCompleted;
                break;
            case ReconciliationAction.Resolved:
                Ensure(ReconciliationStatus.Open, ReconciliationStatus.RefundInitiated, ReconciliationStatus.RefundCompleted);
                Status = ReconciliationStatus.Resolved;
                break;
        }
        if (!string.IsNullOrWhiteSpace(externalRefundRef))
        {
            ExternalRefundRef = externalRefundRef.Trim();
        }
        if (action == ReconciliationAction.Note)
        {
            Notes = note;
        }
        else
        {
            OwnerAction = note;
        }
        UpdatedAt = now;
        return from;
    }

    private void Ensure(params ReconciliationStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new BusinessRuleException("RECONCILIATION_STATUS_INVALID", $"This case is {Status}; that step is not possible.", 409);
        }
    }
}

internal enum ReconciliationAction
{
    Note = 1,
    RefundInitiated = 2,
    RefundCompleted = 3,
    Resolved = 4,
}

/// <summary>Append-only history of every reconciliation change (SPEC §36 "audit history of all reconciliation changes").</summary>
internal sealed class ReconciliationHistory : Entity
{
    private ReconciliationHistory()
    {
    }

    public ReconciliationHistory(Guid reconciliationId, ReconciliationAction action, ReconciliationStatus from, ReconciliationStatus to, string note, string? externalRefundRef,
        Guid actorUserId, DateTimeOffset now)
    {
        ReconciliationId = reconciliationId;
        Action = action;
        FromStatus = from;
        ToStatus = to;
        Note = note;
        ExternalRefundRef = externalRefundRef;
        ActorUserId = actorUserId;
        OccurredAt = now;
    }

    public Guid ReconciliationId { get; private set; }

    public ReconciliationAction Action { get; private set; }

    public ReconciliationStatus FromStatus { get; private set; }

    public ReconciliationStatus ToStatus { get; private set; }

    public string Note { get; private set; } = default!;

    public string? ExternalRefundRef { get; private set; }

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
