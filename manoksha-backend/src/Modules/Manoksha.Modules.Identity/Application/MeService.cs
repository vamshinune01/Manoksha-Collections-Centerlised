using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

internal sealed class MeService(ManokshaDbContext db, ICurrentUser currentUser, IPermissionService permissions)
{
    public async Task<MeResponse> GetAsync(CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == currentUser.UserId, ct);
        var access = await permissions.GetEffectiveAccessAsync(ct);
        return new MeResponse(
            user.Id,
            user.AccountType.ToString(),
            currentUser.Audience!,
            user.DisplayName,
            user.Email,
            user.MobileE164,
            user.MfaEnabled,
            access.IsOwner,
            access.Roles.Select(r => new MeRole(r.RoleCode, r.RoleName, r.BranchId)).ToList(),
            access.GlobalPermissions.Order(StringComparer.Ordinal).ToList(),
            access.BranchPermissions.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Order(StringComparer.Ordinal).ToList()));
    }
}
