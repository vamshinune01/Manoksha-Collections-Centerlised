using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.Modules.Identity;

/// <summary>
/// Operational commands run from the CLI (never exposed over HTTP). The first Owner is created here because no
/// one can yet hold the permission to create users.
/// </summary>
public static class IdentityCommands
{
    public static async Task<Guid> BootstrapOwnerAsync(IServiceProvider services, string email, string displayName, string password, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ManokshaDbContext>();
        var clock = sp.GetRequiredService<IClock>();

        var ownerRole = await db.Set<Role>().SingleAsync(r => r.Code == SystemRoles.Owner, ct);
        if (await db.Set<UserRoleAssignment>().AnyAsync(a => a.RoleId == ownerRole.Id && a.RevokedAt == null, ct))
        {
            throw new InvalidOperationException("An Owner already exists. Use the admin application to manage users.");
        }
        if (!EmailAddress.IsValid(email))
        {
            throw new InvalidOperationException("Invalid email address.");
        }

        return await sp.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async innerCt =>
        {
            var user = User.CreateInternal(email, displayName, null, clock.UtcNow, null);
            user.SetPassword(sp.GetRequiredService<PasswordService>().Hash(user, password), mustChange: false);
            db.Add(user);
            db.Add(new UserRoleAssignment(user.Id, ownerRole.Id, null, null, "Initial Owner bootstrap", clock.UtcNow));
            await sp.GetRequiredService<IAuditWriter>().RecordAsync(new AuditRecord("identity.owner.bootstrapped", "User", user.Id.ToString(),
                After: new { user.Id, user.Email, role = SystemRoles.Owner }, Reason: "Initial Owner bootstrap (CLI)"), innerCt);
            return user.Id;
        }, ct);
    }
}
