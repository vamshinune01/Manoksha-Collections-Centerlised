namespace Manoksha.Modules.Identity.Infrastructure;

/// <summary>Configuration section "Auth". Secrets (signing key, pepper, encryption key) come from Secret Manager.</summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "manoksha-api";

    /// <summary>PEM-encoded EC P-256 private key for ES256 token signing.</summary>
    public string? SigningKeyPem { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;

    public int SessionAbsoluteDays { get; set; } = 30;

    public int ChallengeTokenMinutes { get; set; } = 10;

    /// <summary>Base64 256-bit key used to encrypt MFA secrets at rest.</summary>
    public string? DataEncryptionKey { get; set; }

    public PasswordOptions Password { get; set; } = new();

    public OtpOptions Otp { get; set; } = new();

    /// <summary>Roles that must use MFA (production: OWNER).</summary>
    public string[] MfaRequiredRoles { get; set; } = [];
}

public sealed class PasswordOptions
{
    public int MinLength { get; set; } = 10;

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;
}

public sealed class OtpOptions
{
    /// <summary>Secret pepper for OTP hashing.</summary>
    public string? Pepper { get; set; }

    public int TtlSeconds { get; set; } = 300;

    public int MaxAttempts { get; set; } = 5;

    public int ResendCooldownSeconds { get; set; } = 60;

    public int MaxPerHour { get; set; } = 5;

    /// <summary>DLT-registered SMS template id (provider specific).</summary>
    public string SmsTemplateId { get; set; } = "OTP_LOGIN";
}
