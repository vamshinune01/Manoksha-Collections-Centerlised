using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Inventory.Application;

/// <summary>Shared checks used by inventory workflows.</summary>
internal sealed class InventoryAccess(IPermissionService permissions, IBranchDirectory branches, ICatalogLookup catalog)
{
    public Task EnsureAsync(string permission, Guid branchId, CancellationToken ct) => permissions.EnsurePermissionForBranchAsync(permission, branchId, ct);

    public Task<bool> HasAsync(string permission, Guid branchId, CancellationToken ct) => permissions.HasPermissionForBranchAsync(permission, branchId, ct);

    public async Task<bool> IsOwnerAsync(CancellationToken ct) => (await permissions.GetEffectiveAccessAsync(ct)).IsOwner;

    public async Task<IReadOnlySet<Guid>?> VisibleBranchesAsync(string permission, CancellationToken ct) =>
        (await permissions.GetEffectiveAccessAsync(ct)).BranchesWith(permission);

    public async Task<BranchInfo> ActiveBranchAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await branches.FindAsync(branchId, ct) ?? throw new NotFoundException("BRANCH_NOT_FOUND", "Branch not found.");
        if (!branch.IsActive)
        {
            throw new BusinessRuleException("BRANCH_INACTIVE", $"Branch {branch.Name} is inactive.");
        }
        return branch;
    }

    public async Task<IReadOnlyDictionary<Guid, SkuInfo>> SkusAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var found = await catalog.FindSkusAsync(skuIds, ct);
        var missing = skuIds.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("SKU_NOT_FOUND", $"Unknown SKU(s): {string.Join(", ", missing)}.");
        }
        return found;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> BranchNamesAsync(CancellationToken ct) =>
        (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);

    public static bool IsSerialized(SkuInfo sku) => sku.TrackingMode == "Serialized";

    public static void RequireText(string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleException(code, message, 400);
        }
    }
}
