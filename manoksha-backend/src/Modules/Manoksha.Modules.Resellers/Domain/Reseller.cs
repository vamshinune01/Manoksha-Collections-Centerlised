using Manoksha.SharedKernel;

namespace Manoksha.Modules.Resellers.Domain;

internal enum ResellerStatus
{
    Pending = 1,
    Active = 2,
    Frozen = 3,
    Suspended = 4,
    Closed = 5,
}

/// <summary>An Owner-created reseller (SPEC §6). Immutable internal id + human-readable number; mobile is fixed (ADR-001 §18).</summary>
internal sealed class Reseller : Entity
{
    private static readonly Dictionary<ResellerStatus, ResellerStatus[]> OwnerTransitions = new()
    {
        [ResellerStatus.Pending] = [ResellerStatus.Closed],
        [ResellerStatus.Active] = [ResellerStatus.Frozen, ResellerStatus.Suspended, ResellerStatus.Closed],
        [ResellerStatus.Frozen] = [ResellerStatus.Active, ResellerStatus.Closed],
        [ResellerStatus.Suspended] = [ResellerStatus.Active, ResellerStatus.Closed],
        [ResellerStatus.Closed] = [],
    };

    private Reseller()
    {
    }

    public Reseller(string number, Guid userId, ResellerProfile profile, string mobileE164, Guid createdBy, DateTimeOffset now)
    {
        ResellerNumber = number;
        UserId = userId;
        Profile = profile;
        MobileE164 = mobileE164;
        Status = ResellerStatus.Pending;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public string ResellerNumber { get; private set; } = default!;

    public Guid UserId { get; private set; }

    public ResellerProfile Profile { get; private set; } = default!;

    public string MobileE164 { get; private set; } = default!;

    public ResellerStatus Status { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void UpdateProfile(ResellerProfile profile) => Profile = profile;

    /// <summary>PENDING → ACTIVE, only after mobile OTP verification (SPEC §5.3).</summary>
    public void ActivateAfterVerification(DateTimeOffset now)
    {
        if (Status != ResellerStatus.Pending)
        {
            throw new BusinessRuleException("RESELLER_STATUS_INVALID", "Only a pending reseller can be activated by verification.");
        }
        Status = ResellerStatus.Active;
        ActivatedAt = now;
    }

    /// <summary>Owner status actions (SPEC §27.6). Returning a PENDING reseller to ACTIVE is only possible by OTP verification.</summary>
    public void ChangeStatusByOwner(ResellerStatus to)
    {
        if (!OwnerTransitions[Status].Contains(to))
        {
            throw new BusinessRuleException("RESELLER_STATUS_INVALID", $"A {Status} reseller cannot be changed to {to}.");
        }
        Status = to;
    }
}

internal sealed record ResellerProfile(string ContactName, string? BusinessName, string Email, string AddressLine, string City, string State, string Pin, string? Notes);

/// <summary>Append-only status history.</summary>
internal sealed class ResellerStatusChange : Entity
{
    private ResellerStatusChange()
    {
    }

    public ResellerStatusChange(Guid resellerId, ResellerStatus? from, ResellerStatus to, Guid? actorUserId, string reason, DateTimeOffset now)
    {
        ResellerId = resellerId;
        FromStatus = from;
        ToStatus = to;
        ActorUserId = actorUserId;
        Reason = reason;
        OccurredAt = now;
    }

    public Guid ResellerId { get; private set; }

    public ResellerStatus? FromStatus { get; private set; }

    public ResellerStatus ToStatus { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string Reason { get; private set; } = default!;

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// One immutable version of a reseller's commercial terms (SPEC §15: versioned and audited). Changing terms creates a new
/// version; historical orders keep the version they snapshotted.
/// </summary>
internal sealed class CommercialTerm : Entity
{
    private CommercialTerm()
    {
    }

    public CommercialTerm(Guid resellerId, int version, decimal discountPct, string? notes, string reason, Guid createdBy, DateTimeOffset now)
    {
        Percent.Validate(discountPct);
        ResellerId = resellerId;
        Version = version;
        DiscountPct = discountPct;
        Notes = notes;
        Reason = reason;
        CreatedBy = createdBy;
        EffectiveFrom = now;
    }

    public Guid ResellerId { get; private set; }

    public int Version { get; private set; }

    /// <summary>The reseller's normal discount % (applies unless a product reseller discount overrides it).</summary>
    public decimal DiscountPct { get; private set; }

    public string? Notes { get; private set; }

    public string Reason { get; private set; } = default!;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }
}

internal static class Percent
{
    public static void Validate(decimal pct)
    {
        if (pct is < 0 or > 100 || decimal.Round(pct, 2) != pct)
        {
            throw new BusinessRuleException("DISCOUNT_PERCENT_INVALID", "Discount must be between 0 and 100 with at most 2 decimals.", 400);
        }
    }
}
