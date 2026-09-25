namespace Manoksha.Modules.Branches.Contracts;

public sealed record BranchInfo(Guid Id, string Code, string Name, bool IsActive);

/// <summary>Read access to the branch master for other modules.</summary>
public interface IBranchDirectory
{
    Task<BranchInfo?> FindAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BranchInfo>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed record FulfillmentPriorityEntry(int Priority, Guid BranchId, string BranchCode, string BranchName, bool IsActive);

/// <summary>
/// The Owner-configured fulfillment priority (SPEC §11). Checkout routing (Phase 6) evaluates branches in this order,
/// skipping inactive branches. Existing reservations/orders keep their assigned branch.
/// </summary>
public interface IFulfillmentPriorityProvider
{
    Task<(int Version, IReadOnlyList<FulfillmentPriorityEntry> Entries)> GetCurrentAsync(CancellationToken cancellationToken = default);
}
