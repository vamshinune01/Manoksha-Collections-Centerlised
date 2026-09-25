using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Manoksha.Modules.Identity.Application;

/// <summary>
/// Email + password sign-in for Owner, managers and employees (individual accounts only, SPEC §5.1), with
/// first-login password change and MFA (TOTP) steps.
/// </summary>
internal sealed class InternalAuthService(
    ManokshaDbContext db,
    PasswordService passwords,
    TokenService tokens,
    SessionService sessions,
    SecretProtector protector,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IOptions<IdentityOptions> options,
    IClock clock)
{
    private const string MfaIssuer = "Manoksha Collections";

    public async Task<AuthResponse> LoginAsync(InternalLoginRequest request, CancellationToken ct)
    {
        var client = NormalizeClient(request.Client);
        var email = EmailAddress.Normalize(request.Email ?? string.Empty);
        var now = clock.UtcNow;
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.AccountType == AccountType.Internal && u.EmailNormalized == email, ct);

        if (user is not null && user.IsLockedOut(now))
        {
            await LogAsync(user.Id, email, client, "LOCKED_OUT", null, ct);
            throw new BusinessRuleException("ACCOUNT_LOCKED", "Too many failed attempts. Try again later.", 423);
        }

        if (!passwords.Verify(user, request.Password ?? string.Empty))
        {
            if (user is not null)
            {
                user.RecordFailedLogin(now, options.Value.Password.MaxFailedAttempts, TimeSpan.FromMinutes(options.Value.Password.LockoutMinutes));
            }
            await LogAsync(user?.Id, email, client, "FAILED", "INVALID_CREDENTIALS", ct);
            throw InvalidCredentials();
        }

        if (user!.Status != UserStatus.Active)
        {
            await LogAsync(user.Id, email, client, "DENIED", "ACCOUNT_" + user.Status.ToString().ToUpperInvariant(), ct);
            throw new ForbiddenException("ACCOUNT_NOT_ACTIVE", "This account is not active. Contact the Owner.");
        }

        user.RecordSuccessfulLogin(now);
        await LogAsync(user.Id, email, client, "PASSWORD_OK", null, ct);
        return await ContinueAsync(user, client, ct);
    }

    public async Task<AuthResponse> CompleteFirstPasswordChangeAsync(FirstPasswordChangeRequest request, CancellationToken ct)
    {
        var (user, client) = await ResolveChallengeAsync(request.ChallengeToken, ChallengePurposes.PasswordChange, ct);
        user.SetPassword(passwords.Hash(user, request.NewPassword), mustChange: false);
        await RecordAuditAsUserAsync(user, "identity.password.changed", "First sign-in password change", ct);
        await db.SaveChangesAsync(ct);
        return await ContinueAsync(user, client, ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == currentUser.UserId, ct);
        if (!passwords.Verify(user, request.CurrentPassword ?? string.Empty))
        {
            throw InvalidCredentials();
        }
        user.SetPassword(passwords.Hash(user, request.NewPassword), mustChange: false);
        await audit.RecordAsync(new AuditRecord("identity.password.changed", "User", user.Id.ToString()), ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AuthResponse> VerifyMfaAsync(MfaVerifyRequest request, CancellationToken ct)
    {
        var (user, client) = await ResolveChallengeAsync(request.ChallengeToken, ChallengePurposes.Mfa, ct);
        var now = clock.UtcNow;
        if (user.IsLockedOut(now))
        {
            throw new BusinessRuleException("ACCOUNT_LOCKED", "Too many failed attempts. Try again later.", 423);
        }
        var step = Totp.Verify(protector.Unprotect(user.MfaSecretProtected!), request.Code ?? string.Empty, now);
        if (step is null || !user.TryConsumeMfaTimeStep(step.Value))
        {
            user.RecordFailedLogin(now, options.Value.Password.MaxFailedAttempts, TimeSpan.FromMinutes(options.Value.Password.LockoutMinutes));
            await LogAsync(user.Id, user.EmailNormalized!, client, "FAILED", "INVALID_MFA_CODE", ct);
            throw new BusinessRuleException("MFA_CODE_INVALID", "The authenticator code is incorrect.", 401);
        }
        await LogAsync(user.Id, user.EmailNormalized!, client, "SUCCESS", "MFA", ct);
        return AuthResponse.Authenticated(await sessions.IssueAsync(user, client, ct));
    }

    public async Task<MfaEnrollmentStartResponse> StartMfaEnrollmentAsync(MfaEnrollmentStartRequest request, CancellationToken ct)
    {
        var user = await ResolveEnrollingUserAsync(request.ChallengeToken, ct);
        var secret = Totp.GenerateSecret();
        user.BeginMfaEnrollment(protector.Protect(secret));
        await db.SaveChangesAsync(ct);
        return new MfaEnrollmentStartResponse(secret, Totp.BuildOtpAuthUri(MfaIssuer, user.Email!, secret));
    }

    public async Task<AuthResponse?> ConfirmMfaEnrollmentAsync(MfaEnrollmentConfirmRequest request, CancellationToken ct)
    {
        string? client = null;
        User user;
        if (!string.IsNullOrWhiteSpace(request.ChallengeToken))
        {
            (user, client) = await ResolveChallengeAsync(request.ChallengeToken, ChallengePurposes.MfaEnrollment, ct);
        }
        else
        {
            user = await ResolveEnrollingUserAsync(null, ct);
        }

        if (user.MfaPendingSecretProtected is null)
        {
            throw new BusinessRuleException("MFA_ENROLLMENT_NOT_STARTED", "Start MFA enrollment first.", 400);
        }
        var step = Totp.Verify(protector.Unprotect(user.MfaPendingSecretProtected), request.Code ?? string.Empty, clock.UtcNow)
            ?? throw new BusinessRuleException("MFA_CODE_INVALID", "The authenticator code is incorrect.", 400);
        user.CompleteMfaEnrollment(step);
        await RecordAuditAsUserAsync(user, "identity.mfa.enabled", null, ct);
        await db.SaveChangesAsync(ct);

        return client is null ? null : AuthResponse.Authenticated(await sessions.IssueAsync(user, client, ct));
    }

    private async Task<AuthResponse> ContinueAsync(User user, string client, CancellationToken ct)
    {
        if (user.MustChangePassword)
        {
            await db.SaveChangesAsync(ct);
            return AuthResponse.Challenge(AuthStatus.PasswordChangeRequired, Challenge(user, ChallengePurposes.PasswordChange, client));
        }
        if (user.MfaEnabled)
        {
            await db.SaveChangesAsync(ct);
            return AuthResponse.Challenge(AuthStatus.MfaRequired, Challenge(user, ChallengePurposes.Mfa, client));
        }
        if (await IsMfaRequiredAsync(user.Id, ct))
        {
            await db.SaveChangesAsync(ct);
            return AuthResponse.Challenge(AuthStatus.MfaEnrollmentRequired, Challenge(user, ChallengePurposes.MfaEnrollment, client));
        }
        await LogAsync(user.Id, user.EmailNormalized!, client, "SUCCESS", null, ct);
        return AuthResponse.Authenticated(await sessions.IssueAsync(user, client, ct));
    }

    private async Task<bool> IsMfaRequiredAsync(Guid userId, CancellationToken ct)
    {
        var required = options.Value.MfaRequiredRoles;
        if (required.Length == 0)
        {
            return false;
        }
        return await (
            from a in db.Set<UserRoleAssignment>()
            join r in db.Set<Role>() on a.RoleId equals r.Id
            where a.UserId == userId && a.RevokedAt == null && required.Contains(r.Code)
            select a.Id).AnyAsync(ct);
    }

    private string Challenge(User user, string purpose, string client) =>
        tokens.CreateChallengeToken(new ChallengeClaims(user.Id, purpose, client, user.SecurityStamp, null));

    private async Task<(User User, string Client)> ResolveChallengeAsync(string token, string purpose, CancellationToken ct)
    {
        var claims = await tokens.ValidateChallengeTokenAsync(token ?? string.Empty, purpose);
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == claims.UserId && u.AccountType == AccountType.Internal, ct);
        if (user is null || user.Status != UserStatus.Active || user.SecurityStamp != claims.Stamp)
        {
            throw new BusinessRuleException("CHALLENGE_INVALID", "This sign-in step has expired or is invalid. Please sign in again.", 401);
        }
        return (user, NormalizeClient(claims.Client));
    }

    private async Task<User> ResolveEnrollingUserAsync(string? challengeToken, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(challengeToken))
        {
            return (await ResolveChallengeAsync(challengeToken, ChallengePurposes.MfaEnrollment, ct)).User;
        }
        if (!currentUser.IsAuthenticated || currentUser.AccountType != AccountType.Internal)
        {
            throw new BusinessRuleException("CHALLENGE_INVALID", "Sign in to manage MFA.", 401);
        }
        return await db.Set<User>().SingleAsync(u => u.Id == currentUser.UserId, ct);
    }

    /// <summary>Audit for steps where the actor is identified by a challenge token rather than a session.</summary>
    private Task RecordAuditAsUserAsync(User user, string action, string? reason, CancellationToken ct) =>
        audit.RecordAsync(new AuditRecord(action, "User", user.Id.ToString(), After: new { userId = user.Id }, Reason: reason), ct);

    private async Task LogAsync(Guid? userId, string email, string client, string result, string? reason, CancellationToken ct)
    {
        db.Add(new LoginEvent(userId, nameof(AccountType.Internal), MaskEmail(email), client, result, reason,
            requestContext.IpAddress, requestContext.UserAgent, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeClient(string? client) => client switch
    {
        null or "" or Audiences.Admin => Audiences.Admin,
        Audiences.Pos => Audiences.Pos,
        _ => throw new BusinessRuleException("CLIENT_INVALID", "Client must be 'admin' or 'pos'.", 400),
    };

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" + (at >= 0 ? email[at..] : string.Empty) : email[0] + "***" + email[at..];
    }

    private static BusinessRuleException InvalidCredentials() =>
        new("INVALID_CREDENTIALS", "Email or password is incorrect.", 401);
}
