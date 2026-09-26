using Manoksha.Application.Security;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Identity.Domain;

internal enum UserStatus
{
    Pending = 1,
    Active = 2,
    Locked = 3,
    Disabled = 4,
}

/// <summary>
/// A login identity in exactly one account context (INTERNAL, CUSTOMER or RESELLER). Mobile and email are
/// attributes with uniqueness per account type — never keys (SPEC §2, ADR-001 §2).
/// </summary>
internal sealed class User : Entity
{
    private User()
    {
    }

    private User(AccountType accountType, UserStatus status, string displayName, DateTimeOffset now, Guid? createdBy)
    {
        AccountType = accountType;
        Status = status;
        DisplayName = displayName.Trim();
        CreatedAt = now;
        CreatedBy = createdBy;
        SecurityStamp = NewStamp();
    }

    public AccountType AccountType { get; private set; }

    public UserStatus Status { get; private set; }

    public string DisplayName { get; private set; } = default!;

    public string? Email { get; private set; }

    public string? EmailNormalized { get; private set; }

    public string? MobileE164 { get; private set; }

    public DateTimeOffset? MobileVerifiedAt { get; private set; }

    public string? PasswordHash { get; private set; }

    public bool MustChangePassword { get; private set; }

    public bool MfaEnabled { get; private set; }

    public string? MfaSecretProtected { get; private set; }

    public string? MfaPendingSecretProtected { get; private set; }

    public long? MfaLastUsedTimeStep { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTimeOffset? LockoutUntil { get; private set; }

    /// <summary>Rotated whenever credentials, roles or status change; invalidates outstanding access tokens.</summary>
    public string SecurityStamp { get; private set; } = default!;

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public uint RowVersion { get; private set; }

    public static User CreateInternal(string email, string displayName, string? mobileE164, DateTimeOffset now, Guid? createdBy)
    {
        var user = new User(AccountType.Internal, UserStatus.Active, displayName, now, createdBy)
        {
            Email = email.Trim(),
            EmailNormalized = EmailAddress.Normalize(email),
            MobileE164 = mobileE164,
        };
        return user;
    }

    public static User CreateCustomer(string mobileE164, string displayName, string email, DateTimeOffset now) =>
        new(AccountType.Customer, UserStatus.Active, displayName, now, null)
        {
            MobileE164 = mobileE164,
            MobileVerifiedAt = now,
            Email = email.Trim(),
            EmailNormalized = EmailAddress.Normalize(email),
        };

    /// <summary>Owner-created reseller identity: starts PENDING until mobile OTP verification (SPEC §5.3).</summary>
    public static User CreatePendingReseller(string mobileE164, string displayName, string? email, DateTimeOffset now, Guid createdBy) =>
        new(AccountType.Reseller, UserStatus.Pending, displayName, now, createdBy)
        {
            MobileE164 = mobileE164,
            Email = email?.Trim(),
            EmailNormalized = email is null ? null : EmailAddress.Normalize(email),
        };

    public bool IsLockedOut(DateTimeOffset now) => LockoutUntil is { } until && until > now;

    public void SetPassword(string passwordHash, bool mustChange)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChange;
        RotateSecurityStamp();
    }

    public void RecordFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginCount++;
        if (FailedLoginCount >= maxAttempts)
        {
            LockoutUntil = now + lockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutUntil = null;
        LastLoginAt = now;
    }

    public void BeginMfaEnrollment(string protectedSecret) => MfaPendingSecretProtected = protectedSecret;

    public void CompleteMfaEnrollment(long usedTimeStep)
    {
        MfaSecretProtected = MfaPendingSecretProtected ?? throw new InvalidOperationException("No pending MFA enrollment.");
        MfaPendingSecretProtected = null;
        MfaEnabled = true;
        MfaLastUsedTimeStep = usedTimeStep;
        RotateSecurityStamp();
    }

    /// <summary>Accepts a TOTP time step once (replay protection).</summary>
    public bool TryConsumeMfaTimeStep(long timeStep)
    {
        if (MfaLastUsedTimeStep is { } last && timeStep <= last)
        {
            return false;
        }
        MfaLastUsedTimeStep = timeStep;
        return true;
    }

    public void ChangeStatus(UserStatus status)
    {
        Status = status;
        RotateSecurityStamp();
    }

    public void Activate(DateTimeOffset now)
    {
        Status = UserStatus.Active;
        MobileVerifiedAt ??= now;
        RotateSecurityStamp();
    }

    /// <summary>A customer edits their own name and email (the mobile is the sign-in identity and is not editable here).</summary>
    public void UpdateCustomerProfile(string displayName, string email)
    {
        if (AccountType != AccountType.Customer)
        {
            throw new BusinessRuleException("PROFILE_NOT_EDITABLE", "Only customer profiles can be edited here.", 409);
        }
        DisplayName = displayName.Trim();
        Email = email.Trim();
        EmailNormalized = EmailAddress.Normalize(email);
    }

    public void RotateSecurityStamp() => SecurityStamp = NewStamp();

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
