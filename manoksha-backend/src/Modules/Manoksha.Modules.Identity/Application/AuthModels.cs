using Manoksha.Modules.Identity.Infrastructure;

namespace Manoksha.Modules.Identity.Application;

public static class AuthStatus
{
    public const string Authenticated = "AUTHENTICATED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string MfaRequired = "MFA_REQUIRED";
    public const string MfaEnrollmentRequired = "MFA_ENROLLMENT_REQUIRED";
    public const string RegistrationRequired = "REGISTRATION_REQUIRED";
}

public sealed record AuthResponse(string Status, TokenPair? Tokens = null, string? ChallengeToken = null)
{
    public static AuthResponse Authenticated(TokenPair tokens) => new(AuthStatus.Authenticated, tokens);

    public static AuthResponse Challenge(string status, string token) => new(status, null, token);
}

public sealed record InternalLoginRequest(string Email, string Password, string? Client);

public sealed record FirstPasswordChangeRequest(string ChallengeToken, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MfaVerifyRequest(string ChallengeToken, string Code);

public sealed record MfaEnrollmentStartRequest(string? ChallengeToken);

public sealed record MfaEnrollmentStartResponse(string Secret, string OtpAuthUri);

public sealed record MfaEnrollmentConfirmRequest(string? ChallengeToken, string Code);

public sealed record OtpRequestRequest(string Mobile, string Context);

public sealed record OtpVerifyRequest(string Mobile, string Context, string Code);

public sealed record CustomerRegistrationRequest(string RegistrationToken, string FullName, string Email);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record MeResponse(
    Guid UserId,
    string AccountType,
    string Audience,
    string DisplayName,
    string? Email,
    string? Mobile,
    bool MfaEnabled,
    bool IsOwner,
    IReadOnlyList<MeRole> Roles,
    IReadOnlyList<string> GlobalPermissions,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> BranchPermissions);

public sealed record MeRole(string Code, string Name, Guid? BranchId);
