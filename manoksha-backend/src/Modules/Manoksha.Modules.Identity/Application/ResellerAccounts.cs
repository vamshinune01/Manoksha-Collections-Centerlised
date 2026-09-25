using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

internal sealed class ResellerAccounts(ManokshaDbContext db, SessionService sessions, ICurrentUser currentUser, IClock clock) : IResellerAccounts
{
    public async Task<Guid> CreatePendingAsync(string mobileE164, string displayName, string? email, CancellationToken cancellationToken = default)
    {
        if (await db.Set<User>().AnyAsync(u => u.AccountType == AccountType.Reseller && u.MobileE164 == mobileE164, cancellationToken))
        {
            throw new ConflictException("RESELLER_MOBILE_EXISTS", "A reseller is already registered with this mobile number.");
        }
        var user = User.CreatePendingReseller(mobileE164, displayName, email, clock.UtcNow, currentUser.UserId);
        db.Add(user);
        return user.Id;
    }

    public async Task RevokeSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == userId && u.AccountType == AccountType.Reseller, cancellationToken);
        await sessions.RevokeAllAsync(userId, reason, cancellationToken);
        user.RotateSecurityStamp();
    }
}
