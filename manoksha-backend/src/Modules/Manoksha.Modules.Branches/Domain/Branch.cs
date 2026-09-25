using Manoksha.SharedKernel;

namespace Manoksha.Modules.Branches.Domain;

/// <summary>A physical store/branch. Never hard-deleted; deactivated instead (history stays intact).</summary>
internal sealed class Branch : Entity
{
    private Branch()
    {
    }

    public Branch(Guid id, string code, string name, BranchAddress address, DateTimeOffset now)
        : base(id)
    {
        Code = NormalizeCode(code);
        Name = name.Trim();
        Address = address;
        IsActive = true;
        CreatedAt = now;
    }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public BranchAddress Address { get; private set; } = default!;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void Update(string name, BranchAddress address)
    {
        Name = name.Trim();
        Address = address;
    }

    public void SetActive(bool active) => IsActive = active;

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}

internal sealed record BranchAddress(string? Line1, string? City, string? State, string? Pin, string? Phone);

/// <summary>
/// One immutable version of the fulfillment priority list. A change creates a new version; old versions are kept, so
/// "old order, new order, actor, timestamp" is always available (SPEC §11).
/// </summary>
internal sealed class FulfillmentPriorityVersion : Entity
{
    private readonly List<FulfillmentPriorityVersionEntry> _entries = [];

    private FulfillmentPriorityVersion()
    {
    }

    public FulfillmentPriorityVersion(int versionNo, IReadOnlyList<Guid> orderedBranchIds, Guid? createdBy, string reason, DateTimeOffset now)
    {
        if (orderedBranchIds.Distinct().Count() != orderedBranchIds.Count)
        {
            throw new BusinessRuleException("PRIORITY_DUPLICATE_BRANCH", "Each branch may appear only once in the priority list.", 400);
        }
        VersionNo = versionNo;
        CreatedBy = createdBy;
        Reason = reason;
        CreatedAt = now;
        for (var i = 0; i < orderedBranchIds.Count; i++)
        {
            _entries.Add(new FulfillmentPriorityVersionEntry(Id, orderedBranchIds[i], i + 1));
        }
    }

    public int VersionNo { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public string Reason { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<FulfillmentPriorityVersionEntry> Entries => _entries.OrderBy(e => e.Priority).ToList();
}

internal sealed class FulfillmentPriorityVersionEntry
{
    private FulfillmentPriorityVersionEntry()
    {
    }

    public FulfillmentPriorityVersionEntry(Guid versionId, Guid branchId, int priority)
    {
        VersionId = versionId;
        BranchId = branchId;
        Priority = priority;
    }

    public Guid VersionId { get; private set; }

    public Guid BranchId { get; private set; }

    public int Priority { get; private set; }
}
