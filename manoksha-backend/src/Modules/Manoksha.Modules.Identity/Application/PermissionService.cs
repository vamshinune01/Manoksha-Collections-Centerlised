using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Manoksha.Modules.Identity.Application;

/// <summary>
/// Resolves effective permissions from active role assignments. Cached per (user, security stamp): any role,
/// permission or status change rotates the stamp, so a stale grant is never served.
/// </summary>
internal sealed class PermissionService(
    ManokshaDbContext db,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    IMemoryCache cache) : IPermissionService
{
    private EffectiveAccess? _memo;

    public async Task<EffectiveAccess> GetEffectiveAccessAsync(CancellationToken cancellationToken = default)
    {
        if (_memo is not null)
        {
            return _memo;
        }
        if (!currentUser.IsAuthenticated || currentUser.AccountType != AccountType.Internal)
        {
            return _memo = EffectiveAccess.None(currentUser.UserIdOrNull ?? Guid.Empty);
        }

        var userId = currentUser.UserId;
        var stamp = httpContextAccessor.HttpContext?.User.FindFirst(ManokshaClaimTypes.SecurityStamp)?.Value ?? "none";
        var cacheKey = $"access:{userId:N}:{stamp}";
        if (cache.TryGetValue(cacheKey, out EffectiveAccess? cached) && cached is not null)
        {
            return _memo = cached;
        }

        var access = await LoadAsync(db, userId, cancellationToken);
        cache.Set(cacheKey, access, TimeSpan.FromMinutes(5));
        return _memo = access;
    }

    public async Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken = default) =>
        (await GetEffectiveAccessAsync(cancellationToken)).Has(permission);

    public async Task<bool> HasPermissionForBranchAsync(string permission, Guid branchId, CancellationToken cancellationToken = default) =>
        (await GetEffectiveAccessAsync(cancellationToken)).HasForBranch(permission, branchId);

    public async Task EnsurePermissionForBranchAsync(string permission, Guid branchId, CancellationToken cancellationToken = default)
    {
        if (!await HasPermissionForBranchAsync(permission, branchId, cancellationToken))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You do not have permission to perform this action for this branch.");
        }
    }

    internal static async Task<EffectiveAccess> LoadAsync(ManokshaDbContext db, Guid userId, CancellationToken ct)
    {
        var grants = await (
            from a in db.Set<UserRoleAssignment>()
            join r in db.Set<Role>() on a.RoleId equals r.Id
            where a.UserId == userId && a.RevokedAt == null && r.IsActive
            select new { a.Id, RoleId = r.Id, r.Code, r.Name, a.BranchId, Permissions = r.Permissions.Select(p => p.PermissionCode).ToList() })
            .ToListAsync(ct);

        var global = new HashSet<string>(StringComparer.Ordinal);
        var perBranch = new Dictionary<Guid, HashSet<string>>();
        foreach (var g in grants)
        {
            if (g.BranchId is { } branch)
            {
                if (!perBranch.TryGetValue(branch, out var set))
                {
                    perBranch[branch] = set = new HashSet<string>(StringComparer.Ordinal);
                }
                set.UnionWith(g.Permissions);
            }
            else
            {
                global.UnionWith(g.Permissions);
            }
        }

        var isOwner = grants.Exists(g => g.Code == SystemRoles.Owner && g.BranchId is null);
        if (isOwner)
        {
            global.UnionWith(Manoksha.Application.Security.Permissions.All.Select(p => p.Code));
        }

        return new EffectiveAccess(
            userId,
            isOwner,
            global,
            perBranch.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value),
            grants.Select(g => new RoleGrant(g.Id, g.RoleId, g.Code, g.Name, g.BranchId)).ToList());
    }
}
