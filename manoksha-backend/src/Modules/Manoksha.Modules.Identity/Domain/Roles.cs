using Manoksha.Application.Security;
using Manoksha.SharedKernel;
using PermissionCatalog = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Identity.Domain;

internal sealed class Permission
{
    private Permission()
    {
    }

    public Permission(string code, string module, bool ownerOnly)
    {
        Code = code;
        Module = module;
        OwnerOnly = ownerOnly;
    }

    public string Code { get; private set; } = default!;

    public string Module { get; private set; } = default!;

    public bool OwnerOnly { get; private set; }

    public void Update(string module, bool ownerOnly)
    {
        Module = module;
        OwnerOnly = ownerOnly;
    }
}

internal sealed class Role : Entity
{
    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public Role(string code, string name, string? description, RoleScope scope, bool isSystem, DateTimeOffset now)
    {
        Code = code;
        Name = name;
        Description = description;
        Scope = scope;
        IsSystem = isSystem;
        IsActive = true;
        CreatedAt = now;
    }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    public RoleScope Scope { get; private set; }

    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public bool IsOwnerRole => Code == SystemRoles.Owner;

    public void Rename(string name, string? description)
    {
        Name = name.Trim();
        Description = description;
    }

    public void SetActive(bool active)
    {
        if (!active && IsOwnerRole)
        {
            throw new BusinessRuleException("OWNER_ROLE_IMMUTABLE", "The Owner role cannot be deactivated.");
        }
        IsActive = active;
    }

    /// <summary>Replaces the permission set. Owner-only permissions can only ever belong to the OWNER role.</summary>
    public void SetPermissions(IEnumerable<string> codes, bool allowOwnerOnly)
    {
        var desired = codes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        foreach (var code in desired)
        {
            if (!PermissionCatalog.Exists(code))
            {
                throw new BusinessRuleException("PERMISSION_UNKNOWN", $"Unknown permission '{code}'.", 400);
            }
            if (!allowOwnerOnly && PermissionCatalog.IsOwnerOnly(code))
            {
                throw new BusinessRuleException("PERMISSION_OWNER_ONLY", $"Permission '{code}' is reserved for the Owner and cannot be granted to other roles.");
            }
        }
        _permissions.RemoveAll(p => !desired.Contains(p.PermissionCode));
        foreach (var code in desired.Where(c => _permissions.TrueForAll(p => p.PermissionCode != c)))
        {
            _permissions.Add(new RolePermission(Id, code));
        }
    }
}

internal sealed class RolePermission
{
    private RolePermission()
    {
    }

    public RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public Guid RoleId { get; private set; }

    public string PermissionCode { get; private set; } = default!;
}

/// <summary>A role granted to a user, globally (BranchId null) or for one branch. Revoked, never deleted.</summary>
internal sealed class UserRoleAssignment : Entity
{
    private UserRoleAssignment()
    {
    }

    public UserRoleAssignment(Guid userId, Guid roleId, Guid? branchId, Guid? assignedBy, string reason, DateTimeOffset now)
    {
        UserId = userId;
        RoleId = roleId;
        BranchId = branchId;
        AssignedBy = assignedBy;
        Reason = reason;
        AssignedAt = now;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public Guid? BranchId { get; private set; }

    public Guid? AssignedBy { get; private set; }

    public string Reason { get; private set; } = default!;

    public DateTimeOffset AssignedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public string? RevokeReason { get; private set; }

    public bool IsActive => RevokedAt is null;

    public void Revoke(Guid revokedBy, string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            throw new BusinessRuleException("ROLE_ASSIGNMENT_ALREADY_REVOKED", "This role assignment is already revoked.");
        }
        RevokedAt = now;
        RevokedBy = revokedBy;
        RevokeReason = reason;
    }
}
