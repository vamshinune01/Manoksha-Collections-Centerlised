using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using PermissionCatalog = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Identity.Application;

/// <summary>
/// Synchronises the permission catalog and system roles at startup. Non-Owner system role defaults are applied
/// only when the role is first created (the Owner may later change them); OWNER always holds every permission.
/// </summary>
internal sealed class IdentitySeeder(ManokshaDbContext db, IClock clock) : IStartupSeeder
{
    public int Order => 10;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Set<Permission>().ToDictionaryAsync(p => p.Code, cancellationToken);
        foreach (var def in PermissionCatalog.All)
        {
            if (existing.TryGetValue(def.Code, out var p))
            {
                p.Update(def.Module, def.OwnerOnly);
            }
            else
            {
                db.Add(new Permission(def.Code, def.Module, def.OwnerOnly));
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Set<Role>().Include(r => r.Permissions).ToDictionaryAsync(r => r.Code, cancellationToken);
        foreach (var def in SystemRoles.Definitions)
        {
            if (!roles.TryGetValue(def.Code, out var role))
            {
                role = new Role(def.Code, def.Name, null, def.Scope, isSystem: true, clock.UtcNow);
                role.SetPermissions(def.DefaultPermissions, allowOwnerOnly: def.Code == SystemRoles.Owner);
                db.Add(role);
            }
            else if (role.IsOwnerRole)
            {
                role.SetPermissions(def.DefaultPermissions, allowOwnerOnly: true);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
