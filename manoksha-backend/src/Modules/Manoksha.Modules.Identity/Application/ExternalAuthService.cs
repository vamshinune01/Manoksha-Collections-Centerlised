using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Manoksha.Modules.Identity.Application;

/// <summary>
/// Mobile-OTP sign-in for customers and resellers. Each context is a separate identity: a customer OTP never
/// yields a reseller token and vice versa (ADR-001 §2).
/// </summary>
internal sealed class ExternalAuthService(
    ManokshaDbContext db,
    OtpService otp,
    TokenService tokens,
    SessionService sessions,
    IAuditWriter audit,
    IRequestContext requestContext,
    IEnumerable<IResellerLoginGate> resellerGates,
    IClock clock)
{
    public async Task<AuthResponse> VerifyOtpAsync(OtpVerifyRequest request, CancellationToken ct)
    {
        var accountType = OtpContexts.ToAccountType(request.Context);
        var mobile = await otp.VerifyAsync(request.Mobile, request.Context, request.Code, ct);
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.AccountType == accountType && u.MobileE164 == mobile, ct);

        if (accountType == AccountType.Customer)
        {
            if (user is null)
            {
                await LogAsync(null, accountType, mobile, "REGISTRATION_REQUIRED", ct);
                var registration = tokens.CreateChallengeToken(new ChallengeClaims(null, ChallengePurposes.CustomerRegistration, Audiences.Customer, null, mobile));
                return AuthResponse.Challenge(AuthStatus.RegistrationRequired, registration);
            }
            if (user.Status != UserStatus.Active)
            {
                await LogAsync(user.Id, accountType, mobile, "DENIED", ct);
                throw new ForbiddenException("ACCOUNT_NOT_ACTIVE", "This account is not active. Please contact support.");
            }
            await LogAsync(user.Id, accountType, mobile, "SUCCESS", ct);
            return AuthResponse.Authenticated(await sessions.IssueAsync(user, Audiences.Customer, ct));
        }

        // Reseller: only Owner-created accounts (SPEC §5.3). OTP verification is the activation step for PENDING resellers;
        // the Resellers module decides per business status who may sign in (ADR-001 §17).
        if (user is null || user.Status is not (UserStatus.Pending or UserStatus.Active))
        {
            await LogAsync(user?.Id, accountType, mobile, "DENIED", ct);
            throw new ForbiddenException("RESELLER_ACCOUNT_NOT_ACTIVE", "No active reseller account is registered for this number.");
        }
        foreach (var gate in resellerGates)
        {
            var decision = await gate.OnMobileVerifiedAsync(user.Id, ct);
            if (!decision.Allowed)
            {
                await LogAsync(user.Id, accountType, mobile, "DENIED", ct);
                throw new ForbiddenException(decision.DenyCode ?? "RESELLER_ACCOUNT_NOT_ACTIVE", decision.DenyMessage ?? "Sign-in is not available for this reseller account.");
            }
        }
        if (user.Status == UserStatus.Pending)
        {
            user.Activate(clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("identity.reseller.mobile_verified", "User", user.Id.ToString(), After: new { mobile = MobileNumber.Mask(mobile) }), ct);
        }
        await LogAsync(user.Id, accountType, mobile, "SUCCESS", ct);
        return AuthResponse.Authenticated(await sessions.IssueAsync(user, Audiences.Reseller, ct));
    }

    public async Task<AuthResponse> RegisterCustomerAsync(CustomerRegistrationRequest request, CancellationToken ct)
    {
        var claims = await tokens.ValidateChallengeTokenAsync(request.RegistrationToken ?? string.Empty, ChallengePurposes.CustomerRegistration);
        var mobile = claims.Mobile ?? throw new BusinessRuleException("CHALLENGE_INVALID", "Registration session is invalid.", 401);
        if (!EmailAddress.IsValid(request.Email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address.", 400);
        }

        var user = User.CreateCustomer(mobile, request.FullName, request.Email, clock.UtcNow);
        db.Add(user);
        await audit.RecordAsync(new AuditRecord("identity.customer.registered", "User", user.Id.ToString(),
            After: new { user.Id, mobile = MobileNumber.Mask(mobile) }), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("CUSTOMER_ALREADY_REGISTERED", "A customer account already exists for this mobile number. Please sign in.");
        }
        return AuthResponse.Authenticated(await sessions.IssueAsync(user, Audiences.Customer, ct));
    }

    private async Task LogAsync(Guid? userId, AccountType accountType, string mobile, string result, CancellationToken ct)
    {
        db.Add(new LoginEvent(userId, accountType.ToString(), MobileNumber.Mask(mobile), accountType.ToString().ToLowerInvariant(),
            result, null, requestContext.IpAddress, requestContext.UserAgent, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }
}
