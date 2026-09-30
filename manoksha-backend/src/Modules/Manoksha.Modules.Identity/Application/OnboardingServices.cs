using System.Security.Cryptography;
using System.Text;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Manoksha.Modules.Identity.Application;

public sealed record InvitationDto(string DisplayName, string EmailMasked, DateTimeOffset ExpiresAt);

public sealed record AcceptInvitationRequest(string Password);

public sealed record NewInvitationDto(Guid UserId, string Token, DateTimeOffset ExpiresAt);

public sealed record OwnerSetupStatus(bool OwnerExists, bool SetupEnabled);

public sealed record OwnerSetupRequest(string SetupCode, string Email, string FullName, string Password);

/// <summary>
/// Staff invitations (Owner-created accounts only; SPEC §5.1): a one-time link, valid 72 hours, lets the person set their own
/// password. Creating a new link revokes older unused ones. Only a hash of the token is stored.
/// </summary>
internal sealed class InvitationService(ManokshaDbContext db, IUnitOfWork unitOfWork, PasswordService passwords, IAuditWriter audit, ICurrentUser currentUser,
    IClock clock)
{
    public static readonly TimeSpan Validity = TimeSpan.FromHours(72);

    /// <summary>Inside the caller's transaction.</summary>
    internal async Task<NewInvitationDto> IssueAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var old in await db.Set<UserInvitation>().Where(i => i.UserId == userId && i.UsedAt == null && i.RevokedAt == null).ToListAsync(ct))
        {
            old.Revoke(now);
        }
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var invitation = new UserInvitation(userId, Hash(token), now.Add(Validity), currentUser.UserId, now);
        db.Add(invitation);
        await audit.RecordAsync(new AuditRecord("identity.invitation.issued", "User", userId.ToString(), After: new { expiresAt = invitation.ExpiresAt }), ct);
        await db.SaveChangesAsync(ct);
        return new NewInvitationDto(userId, token, invitation.ExpiresAt);
    }

    /// <summary>A new link for an existing staff member (e.g. the first one expired or they forgot their password).</summary>
    public Task<NewInvitationDto> ReissueAsync(Guid userId, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId && u.AccountType == AccountType.Internal, innerCt)
                       ?? throw new NotFoundException("USER_NOT_FOUND", "User not found.");
            if (user.Status != UserStatus.Active)
            {
                throw new BusinessRuleException("USER_NOT_ACTIVE", "Only active users can be invited. Reactivate the user first.", 409);
            }
            return await IssueAsync(user.Id, innerCt);
        }, ct);

    public async Task<InvitationDto> GetAsync(string token, CancellationToken ct)
    {
        var (invitation, user) = await FindAsync(token, ct);
        return new InvitationDto(user.DisplayName, MaskEmail(user.Email!), invitation.ExpiresAt);
    }

    public Task AcceptAsync(string token, AcceptInvitationRequest request, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var hash = Hash(token ?? string.Empty);
            var invitation = await db.Set<UserInvitation>().FromSqlInterpolated($"SELECT *, xmin FROM identity.user_invitations WHERE token_hash = {hash} FOR UPDATE")
                .SingleOrDefaultAsync(innerCt);
            if (invitation is null || !invitation.IsUsable(clock.UtcNow))
            {
                throw Invalid();
            }
            var user = await db.Set<User>().SingleAsync(u => u.Id == invitation.UserId, innerCt);
            if (user.Status != UserStatus.Active)
            {
                throw Invalid();
            }
            user.SetPassword(passwords.Hash(user, request.Password ?? string.Empty), mustChange: false);
            invitation.MarkUsed(clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("identity.invitation.accepted", "User", user.Id.ToString(), After: new { user.Email }), innerCt);
            await db.SaveChangesAsync(innerCt);
        }, ct);

    private async Task<(UserInvitation Invitation, User User)> FindAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token ?? string.Empty);
        var invitation = await db.Set<UserInvitation>().AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitation is null || !invitation.IsUsable(clock.UtcNow))
        {
            throw Invalid();
        }
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == invitation.UserId, ct);
        return user.Status == UserStatus.Active ? (invitation, user) : throw Invalid();
    }

    private static BusinessRuleException Invalid() =>
        new("INVITATION_INVALID", "This invitation link is invalid, already used or expired. Ask the Owner for a new link.", 410);

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}{new string('•', Math.Min(6, at - 1))}{email[at..]}";
    }
}

/// <summary>
/// First-run Owner setup page (web alternative to the bootstrap CLI command). Works only while no Owner exists AND a setup code is
/// configured (secret "Setup:OwnerSetupCode", at least 16 characters); after that it is permanently closed for this database.
/// </summary>
internal sealed class OwnerSetupService(ManokshaDbContext db, IUnitOfWork unitOfWork, PasswordService passwords, IAuditWriter audit, IConfiguration configuration,
    IClock clock)
{
    private string? SetupCode => configuration["Setup:OwnerSetupCode"] is { Length: >= 16 } code ? code : null;

    public async Task<OwnerSetupStatus> StatusAsync(CancellationToken ct) => new(await OwnerExistsAsync(ct), SetupCode is not null);

    public Task CreateOwnerAsync(OwnerSetupRequest request, CancellationToken ct)
    {
        var code = SetupCode ?? throw new BusinessRuleException("SETUP_DISABLED", "Owner setup is not enabled on this server.", 404);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.SetupCode ?? string.Empty), Encoding.UTF8.GetBytes(code)))
        {
            throw new ForbiddenException("SETUP_CODE_INVALID", "The setup code is not correct.");
        }
        var name = request.FullName?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 200)
        {
            throw new BusinessRuleException("NAME_INVALID", "Enter your full name.", 400);
        }
        if (!EmailAddress.IsValid(request.Email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Lock the Owner role row: two simultaneous setups cannot both create an Owner.
            var ownerRole = await db.Set<Role>().FromSqlInterpolated($"SELECT *, xmin FROM identity.roles WHERE code = {SystemRoles.Owner} FOR UPDATE").SingleAsync(innerCt);
            if (await db.Set<UserRoleAssignment>().AnyAsync(a => a.RoleId == ownerRole.Id && a.RevokedAt == null, innerCt))
            {
                throw new ConflictException("SETUP_COMPLETE", "The Owner account already exists. Sign in instead.");
            }
            var user = User.CreateInternal(request.Email, name, null, clock.UtcNow, null);
            user.SetPassword(passwords.Hash(user, request.Password ?? string.Empty), mustChange: false);
            db.Add(user);
            db.Add(new UserRoleAssignment(user.Id, ownerRole.Id, null, null, "Initial Owner setup (web)", clock.UtcNow));
            await audit.RecordAsync(new AuditRecord("identity.owner.bootstrapped", "User", user.Id.ToString(),
                After: new { user.Id, user.Email, role = SystemRoles.Owner }, Reason: "Initial Owner setup (web)"), innerCt);
            await db.SaveChangesAsync(innerCt);
        }, ct);
    }

    private async Task<bool> OwnerExistsAsync(CancellationToken ct) =>
        await (from a in db.Set<UserRoleAssignment>()
               join r in db.Set<Role>() on a.RoleId equals r.Id
               where r.Code == SystemRoles.Owner && a.RevokedAt == null
               select a.Id).AnyAsync(ct);
}
