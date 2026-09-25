namespace Manoksha.Application.Security;

public interface IPermissionService
{
    /// <summary>Effective access of the current internal user (cached per security stamp).</summary>
    Task<EffectiveAccess> GetEffectiveAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the user holds the permission globally or in at least one branch.</summary>
    Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default);

    /// <summary>True when the user holds the permission globally or for this specific branch.</summary>
    Task<bool> HasPermissionForBranchAsync(string permission, Guid branchId, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="SharedKernel.ForbiddenException"/> unless permitted for the branch.</summary>
    Task EnsurePermissionForBranchAsync(string permission, Guid branchId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Effective permissions. <see cref="GlobalPermissions"/> apply to every branch; <see cref="BranchPermissions"/>
/// apply only to the listed branch.
/// </summary>
public sealed record EffectiveAccess(
    Guid UserId,
    bool IsOwner,
    IReadOnlySet<string> GlobalPermissions,
    IReadOnlyDictionary<Guid, IReadOnlySet<string>> BranchPermissions,
    IReadOnlyList<RoleGrant> Roles)
{
    public static EffectiveAccess None(Guid userId) =>
        new(userId, false, new HashSet<string>(), new Dictionary<Guid, IReadOnlySet<string>>(), Array.Empty<RoleGrant>());

    public bool Has(string permission) =>
        GlobalPermissions.Contains(permission) || BranchPermissions.Values.Any(p => p.Contains(permission));

    public bool HasForBranch(string permission, Guid branchId) =>
        GlobalPermissions.Contains(permission) || (BranchPermissions.TryGetValue(branchId, out var p) && p.Contains(permission));

    /// <summary>Branches where the permission applies; null means all branches (global grant).</summary>
    public IReadOnlySet<Guid>? BranchesWith(string permission) =>
        GlobalPermissions.Contains(permission)
            ? null
            : BranchPermissions.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key).ToHashSet();
}

public sealed record RoleGrant(Guid AssignmentId, Guid RoleId, string RoleCode, string RoleName, Guid? BranchId);
