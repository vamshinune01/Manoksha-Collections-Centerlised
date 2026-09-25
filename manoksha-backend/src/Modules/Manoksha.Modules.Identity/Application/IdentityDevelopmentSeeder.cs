using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

/// <summary>LOCAL/DEVELOPMENT ONLY: an Owner and one user per branch role at the Karimnagar dev branch.</summary>
internal sealed class IdentityDevelopmentSeeder(ManokshaDbContext db, PasswordService hasher, IClock clock) : IDevelopmentSeeder
{
    public int Order => 10;

    public async Task SeedAsync(DevelopmentSeedContext context, CancellationToken cancellationToken)
    {
        var roles = await db.Set<Role>().ToDictionaryAsync(r => r.Code, cancellationToken);
        (string Email, string Name, string Role, Guid? Branch)[] seeds =
        [
            (DevelopmentSeedData.OwnerEmail, "Dev Owner", SystemRoles.Owner, null),
            (DevelopmentSeedData.ManagerEmail, "Karimnagar Manager", SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar),
            (DevelopmentSeedData.SalesEmail, "Karimnagar Sales", SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar),
            (DevelopmentSeedData.InventoryEmail, "Karimnagar Inventory", SystemRoles.InventoryEmployee, DevelopmentSeedData.BranchKarimnagar),
        ];

        foreach (var s in seeds)
        {
            var normalized = EmailAddress.Normalize(s.Email);
            if (await db.Set<User>().AnyAsync(u => u.AccountType == AccountType.Internal && u.EmailNormalized == normalized, cancellationToken))
            {
                continue;
            }
            var user = User.CreateInternal(s.Email, s.Name, null, clock.UtcNow, null);
            user.SetPassword(hasher.Hash(user, context.Password), mustChange: false);
            db.Add(user);
            db.Add(new UserRoleAssignment(user.Id, roles[s.Role].Id, s.Branch, null, "Development seed", clock.UtcNow));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
