using System.Text.RegularExpressions;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PermissionCatalog = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Identity.Application;

/// <summary>Owner-only role and permission administration (SPEC §26 "Employee permission change: Owner only").</summary>
internal sealed partial class RoleAdministrationService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IPermissionService permissions,
    IAuditWriter audit,
    IOutbox outbox,
    IClock clock)
{
    public static IReadOnlyList<PermissionDto> ListPermissions() =>
        PermissionCatalog.All.Select(p => new PermissionDto(p.Code, p.Module, p.OwnerOnly)).ToList();

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct) =>
        (await db.Set<Role>().AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<RoleDto> GetAsync(Guid roleId, CancellationToken ct) =>
        ToDto(await db.Set<Role>().AsNoTracking().Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, ct) ?? throw RoleNotFound());

    public Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!RoleCodePattern().IsMatch(code))
        {
            throw new BusinessRuleException("ROLE_CODE_INVALID", "Role code must be 3–50 characters: A–Z, 0–9 and underscore.", 400);
        }
        if (SystemRoles.Definitions.Any(d => d.Code == code))
        {
            throw new ConflictException("ROLE_CODE_RESERVED", "This role code is reserved for a system role.");
        }

        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureOwnerAsync(innerCt);
            var role = new Role(code, request.Name.Trim(), request.Description, request.Scope, isSystem: false, clock.UtcNow);
            role.SetPermissions(request.Permissions ?? [], allowOwnerOnly: false);
            db.Add(role);
            await audit.RecordAsync(new AuditRecord("identity.role.created", "Role", role.Id.ToString(),
                After: ToDto(role), Reason: request.Reason), innerCt);
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("ROLE_CODE_EXISTS", "A role with this code already exists.");
            }
            return ToDto(role);
        }, ct);
    }

    public Task<RoleDto> UpdateAsync(Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureOwnerAsync(innerCt);
            var role = await LoadAsync(roleId, innerCt);
            var before = ToDto(role);
            role.Rename(request.Name, request.Description);
            role.SetActive(request.IsActive);
            if (before.IsActive != request.IsActive)
            {
                await RotateStampsOfHoldersAsync(role.Id, innerCt);
            }
            await audit.RecordAsync(new AuditRecord("identity.role.updated", "Role", role.Id.ToString(), before, ToDto(role), request.Reason), innerCt);
            return ToDto(role);
        }, ct);
    }

    public Task<RoleDto> SetPermissionsAsync(Guid roleId, SetRolePermissionsRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureOwnerAsync(innerCt);
            var role = await LoadAsync(roleId, innerCt);
            if (role.IsOwnerRole)
            {
                throw new BusinessRuleException("OWNER_ROLE_IMMUTABLE", "The Owner role always has every permission and cannot be edited.");
            }
            var before = role.Permissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToList();
            role.SetPermissions(request.Permissions ?? [], allowOwnerOnly: false);
            var after = role.Permissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToList();
            await RotateStampsOfHoldersAsync(role.Id, innerCt);
            await audit.RecordAsync(new AuditRecord("identity.role.permissions_changed", "Role", role.Id.ToString(),
                Before: new { role.Code, permissions = before },
                After: new { role.Code, permissions = after, added = after.Except(before), removed = before.Except(after) },
                Reason: request.Reason), innerCt);
            return ToDto(role);
        }, ct);
    }

    /// <summary>Holders' cached permissions become invalid immediately.</summary>
    private async Task RotateStampsOfHoldersAsync(Guid roleId, CancellationToken ct)
    {
        var holders = await db.Set<UserRoleAssignment>().Where(a => a.RoleId == roleId && a.RevokedAt == null).Select(a => a.UserId).Distinct().ToListAsync(ct);
        var users = await db.Set<User>().Where(u => holders.Contains(u.Id)).ToListAsync(ct);
        foreach (var u in users)
        {
            u.RotateSecurityStamp();
            outbox.Enqueue(new UserAccessChanged(u.Id, "role_permissions_changed"));
        }
    }

    private async Task EnsureOwnerAsync(CancellationToken ct)
    {
        if (!(await permissions.GetEffectiveAccessAsync(ct)).IsOwner)
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only the Owner can change roles and permissions.");
        }
    }

    private async Task<Role> LoadAsync(Guid roleId, CancellationToken ct) =>
        await db.Set<Role>().Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, ct) ?? throw RoleNotFound();

    private static RoleDto ToDto(Role r) =>
        new(r.Id, r.Code, r.Name, r.Description, r.Scope.ToString(), r.IsSystem, r.IsActive,
            r.Permissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToList());

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    private static NotFoundException RoleNotFound() => new("ROLE_NOT_FOUND", "Role not found.");

    [GeneratedRegex("^[A-Z][A-Z0-9_]{2,49}$")]
    private static partial Regex RoleCodePattern();
}
