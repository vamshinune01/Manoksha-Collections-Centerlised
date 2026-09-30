using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

internal sealed class UserDirectory(ManokshaDbContext db) : IUserDirectory
{
    public async Task<UserContact?> GetContactAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var u = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (u is null)
        {
            return null;
        }
        var isOwner = u.AccountType == AccountType.Internal && (await PermissionService.LoadAsync(db, u.Id, cancellationToken)).IsOwner;
        return new UserContact(u.Id, u.DisplayName, u.Email, u.AccountType.ToString(), u.Status == UserStatus.Active, isOwner);
    }

    public async Task<IReadOnlyList<UserContact>> ListOwnersAsync(CancellationToken cancellationToken = default) =>
        await (from a in db.Set<UserRoleAssignment>()
               join r in db.Set<Role>() on a.RoleId equals r.Id
               join u in db.Set<User>() on a.UserId equals u.Id
               where a.RevokedAt == null && r.Code == SystemRoles.Owner && u.Status == UserStatus.Active
               select new UserContact(u.Id, u.DisplayName, u.Email, u.AccountType.ToString(), true, true))
            .Distinct().ToListAsync(cancellationToken);
}
