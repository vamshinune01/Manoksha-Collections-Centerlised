using Manoksha.SharedKernel;

namespace Manoksha.Modules.Wallet.Domain;

/// <summary>
/// A reseller's prepaid wallet. <see cref="Balance"/> is a cache of the immutable ledger, changed only under a row lock
/// together with a ledger entry; the database enforces balance ≥ 0 (SPEC §16).
/// </summary>
internal sealed class ResellerWallet : Entity
{
    private ResellerWallet()
    {
    }

    public ResellerWallet(Guid resellerId, DateTimeOffset now)
    {
        ResellerId = resellerId;
        Balance = 0m;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid ResellerId { get; private set; }

    public decimal Balance { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public (decimal Before, decimal After) Apply(LedgerDirection direction, decimal amount, DateTimeOffset now)
    {
        if (amount <= 0 || !Money.HasValidScale(amount))
        {
            throw new BusinessRuleException("AMOUNT_INVALID", "Amount must be greater than zero with at most 2 decimals.", 400);
        }
        var before = Balance;
        var after = direction == LedgerDirection.Credit ? before + amount : before - amount;
        if (after < 0)
        {
            var ex = new BusinessRuleException("INSUFFICIENT_WALLET_BALANCE",
                $"Wallet balance ₹{before:N2} is not enough for ₹{amount:N2}. Please add money to your wallet.", 422);
            ex.Details["balance"] = before;
            ex.Details["required"] = amount;
            throw ex;
        }
        Balance = after;
        UpdatedAt = now;
        return (before, after);
    }
}

internal enum LedgerEntryType
{
    Deposit = 1,
    Debit = 2,
    Reversal = 3,
    Adjustment = 4,
}

internal enum LedgerDirection
{
    Credit = 1,
    Debit = 2,
}

/// <summary>Append-only wallet ledger (SPEC §16 required fields). Corrections are new entries, never edits or deletes.</summary>
internal sealed class WalletLedgerEntry : Entity
{
    private WalletLedgerEntry()
    {
    }

    public WalletLedgerEntry(Guid walletId, Guid resellerId, LedgerEntryType type, LedgerDirection direction, decimal amount, decimal balanceBefore, decimal balanceAfter,
        Guid? orderId, string? orderNumber, Guid? depositRequestId, Guid? reversesEntryId, string? reason, Guid? createdBy, DateTimeOffset now, Guid? onlineDepositId = null)
    {
        OnlineDepositId = onlineDepositId;
        WalletId = walletId;
        ResellerId = resellerId;
        Type = type;
        Direction = direction;
        Amount = amount;
        BalanceBefore = balanceBefore;
        BalanceAfter = balanceAfter;
        OrderId = orderId;
        OrderNumber = orderNumber;
        DepositRequestId = depositRequestId;
        ReversesEntryId = reversesEntryId;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public long Seq { get; private set; }

    public Guid WalletId { get; private set; }

    public Guid ResellerId { get; private set; }

    public LedgerEntryType Type { get; private set; }

    public LedgerDirection Direction { get; private set; }

    public decimal Amount { get; private set; }

    public decimal BalanceBefore { get; private set; }

    public decimal BalanceAfter { get; private set; }

    public Guid? OrderId { get; private set; }

    public string? OrderNumber { get; private set; }

    public Guid? DepositRequestId { get; private set; }

    public Guid? ReversesEntryId { get; private set; }

    /// <summary>Provider-confirmed online deposit this credit came from (SPEC §17.1) — at most one credit each.</summary>
    public Guid? OnlineDepositId { get; private set; }

    public string? Reason { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

internal enum DepositStatus
{
    Pending = 1,
    Approved = 2,
    Credited = 3,
    Rejected = 4,
}

/// <summary>Direct/PhonePe deposit with proof, credited only after Owner approval (SPEC §17.2, ADR-001 §20).</summary>
internal sealed class DepositRequest : Entity
{
    private DepositRequest()
    {
    }

    public DepositRequest(string number, Guid resellerId, decimal amount, string method, string reference, Guid proofFileId, string? note, Guid submittedBy, DateTimeOffset now)
    {
        Number = number;
        ResellerId = resellerId;
        Amount = amount;
        Method = method;
        Reference = reference;
        ProofFileId = proofFileId;
        ResellerNote = note;
        SubmittedBy = submittedBy;
        SubmittedAt = now;
        Status = DepositStatus.Pending;
    }

    public string Number { get; private set; } = default!;

    public Guid ResellerId { get; private set; }

    public decimal Amount { get; private set; }

    public string Method { get; private set; } = default!;

    /// <summary>UTR / transaction reference, normalised (upper-case, no spaces).</summary>
    public string Reference { get; private set; } = default!;

    public Guid ProofFileId { get; private set; }

    public string? ResellerNote { get; private set; }

    public DepositStatus Status { get; private set; }

    public Guid SubmittedBy { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewNote { get; private set; }

    public Guid? LedgerEntryId { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>PENDING → APPROVED → CREDITED in one step (SPEC §27.4).</summary>
    public void ApproveAndCredit(Guid by, Guid ledgerEntryId, string? note, DateTimeOffset now)
    {
        EnsurePending();
        Status = DepositStatus.Credited;
        ReviewedBy = by;
        ReviewedAt = now;
        ReviewNote = note;
        LedgerEntryId = ledgerEntryId;
    }

    public void Reject(Guid by, string reason, DateTimeOffset now)
    {
        EnsurePending();
        Status = DepositStatus.Rejected;
        ReviewedBy = by;
        ReviewedAt = now;
        ReviewNote = reason;
    }

    private void EnsurePending()
    {
        if (Status != DepositStatus.Pending)
        {
            throw new ConflictException("DEPOSIT_ALREADY_DECIDED", $"This deposit request was already {(Status == DepositStatus.Rejected ? "rejected" : "approved and credited")}.");
        }
    }
}

/// <summary>Private files attached to wallet workflows (deposit proofs).</summary>
internal sealed class WalletFile : Entity
{
    private WalletFile()
    {
    }

    public WalletFile(Guid id, string objectKey, string contentType, long size, string sha256, Guid uploadedBy, DateTimeOffset now)
        : base(id)
    {
        ObjectKey = objectKey;
        ContentType = contentType;
        Size = size;
        Sha256 = sha256;
        UploadedBy = uploadedBy;
        UploadedAt = now;
    }

    public string ObjectKey { get; private set; } = default!;

    public string ContentType { get; private set; } = default!;

    public long Size { get; private set; }

    public string Sha256 { get; private set; } = default!;

    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }
}

internal enum OnlineDepositStatus
{
    Pending = 1,
    Credited = 2,
    Failed = 3,
    Expired = 4,
}

/// <summary>
/// Provider-confirmed online wallet deposit (SPEC §17.1): the wallet is credited only after the payment provider's authoritative
/// success, once. A success that arrives after a failure/expiry is still money received, so it is credited then.
/// </summary>
internal sealed class OnlineDeposit : Entity
{
    private OnlineDeposit()
    {
    }

    public OnlineDeposit(string number, Guid resellerId, decimal amount, Guid requestedBy, DateTimeOffset now)
    {
        Number = number;
        ResellerId = resellerId;
        Amount = amount;
        RequestedBy = requestedBy;
        CreatedAt = now;
        Status = OnlineDepositStatus.Pending;
    }

    public string Number { get; private set; } = default!;

    public Guid ResellerId { get; private set; }

    public decimal Amount { get; private set; }

    public OnlineDepositStatus Status { get; private set; }

    public Guid? PaymentAttemptId { get; private set; }

    public Guid? LedgerEntryId { get; private set; }

    public string? ProviderPaymentRef { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void AttachPayment(Guid attemptId) => PaymentAttemptId = attemptId;

    public void Credit(Guid ledgerEntryId, string? providerPaymentRef, DateTimeOffset now)
    {
        if (Status == OnlineDepositStatus.Credited)
        {
            throw new BusinessRuleException("DEPOSIT_ALREADY_CREDITED", "This deposit was already credited.", 409);
        }
        Status = OnlineDepositStatus.Credited;
        LedgerEntryId = ledgerEntryId;
        ProviderPaymentRef = providerPaymentRef;
        CompletedAt = now;
    }

    public void Close(OnlineDepositStatus to, DateTimeOffset now)
    {
        if (Status == OnlineDepositStatus.Pending)
        {
            Status = to;
            CompletedAt = now;
        }
    }
}
