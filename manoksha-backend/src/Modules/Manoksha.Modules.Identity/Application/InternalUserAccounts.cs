using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

internal sealed class InternalUserAccounts(UserAdministrationService users, ManokshaDbContext db) : IInternalUserAccounts
{
    public async Task<NewInternalAccount> CreateAsync(string email, string displayName, string? mobile, string reason, CancellationToken cancellationToken = default)
    {
        var created = await users.CreateInternalUserCoreAsync(new CreateInternalUserRequest(email, displayName, mobile, reason), cancellationToken);
        return new NewInternalAccount(created.UserId, created.TemporaryPassword);
    }

    public async Task<InternalAccountInfo?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.Set<User>().AsNoTracking()
            .Where(u => u.Id == userId && u.AccountType == AccountType.Internal)
            .Select(u => new InternalAccountInfo(u.Id, u.DisplayName, u.Email, u.Status.ToString()))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<InternalAccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = EmailAddress.Normalize(email);
        return await db.Set<User>().AsNoTracking()
            .Where(u => u.AccountType == AccountType.Internal && u.EmailNormalized == normalized)
            .Select(u => new InternalAccountInfo(u.Id, u.DisplayName, u.Email, u.Status.ToString()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
