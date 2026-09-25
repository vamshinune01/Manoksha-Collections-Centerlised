using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.SharedKernel;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Manoksha.Modules.Identity.Infrastructure;

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Purpose-bound short-lived tokens used between login steps (never accepted as access tokens).</summary>
internal static class ChallengePurposes
{
    public const string PasswordChange = "password_change";
    public const string Mfa = "mfa";
    public const string MfaEnrollment = "mfa_enrollment";
    public const string CustomerRegistration = "customer_registration";
}

internal sealed record ChallengeClaims(Guid? UserId, string Purpose, string? Client, string? Stamp, string? Mobile);

internal sealed class TokenService(KeyMaterial keys, IOptions<IdentityOptions> options, IClock clock)
{
    public const string ChallengeAudience = "manoksha-auth-challenge";
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IdentityOptions Options => options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(User user, string audience, Guid sessionId)
    {
        var now = clock.UtcNow;
        var expires = now.AddMinutes(Options.AccessTokenMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Options.Issuer,
            Audience = audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = keys.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                [ManokshaClaimTypes.Subject] = user.Id.ToString(),
                [ManokshaClaimTypes.AccountType] = user.AccountType.ToString(),
                [ManokshaClaimTypes.SessionId] = sessionId.ToString(),
                [ManokshaClaimTypes.SecurityStamp] = user.SecurityStamp,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        });
        return (token, expires);
    }

    public string CreateChallengeToken(ChallengeClaims claims)
    {
        var now = clock.UtcNow;
        var dict = new Dictionary<string, object>
        {
            ["purpose"] = claims.Purpose,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };
        if (claims.UserId is { } uid)
        {
            dict[ManokshaClaimTypes.Subject] = uid.ToString();
        }
        if (claims.Client is not null)
        {
            dict["client"] = claims.Client;
        }
        if (claims.Stamp is not null)
        {
            dict[ManokshaClaimTypes.SecurityStamp] = claims.Stamp;
        }
        if (claims.Mobile is not null)
        {
            dict["mobile"] = claims.Mobile;
        }
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Options.Issuer,
            Audience = ChallengeAudience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(Options.ChallengeTokenMinutes).UtcDateTime,
            SigningCredentials = keys.SigningCredentials,
            Claims = dict,
        });
    }

    public async Task<ChallengeClaims> ValidateChallengeTokenAsync(string token, string expectedPurpose)
    {
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = ChallengeAudience,
            IssuerSigningKey = keys.SigningKey,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) => expires is { } e && e > clock.UtcNow.UtcDateTime,
        });
        if (!result.IsValid)
        {
            throw Invalid();
        }
        var c = result.ClaimsIdentity;
        if (c.FindFirst("purpose")?.Value != expectedPurpose)
        {
            throw Invalid();
        }
        Guid? uid = Guid.TryParse(c.FindFirst(ManokshaClaimTypes.Subject)?.Value, out var g) ? g : null;
        return new ChallengeClaims(uid, expectedPurpose, c.FindFirst("client")?.Value, c.FindFirst(ManokshaClaimTypes.SecurityStamp)?.Value, c.FindFirst("mobile")?.Value);

        static BusinessRuleException Invalid() =>
            new("CHALLENGE_INVALID", "This sign-in step has expired or is invalid. Please sign in again.", 401);
    }

    public TokenValidationParameters AccessTokenValidationParameters() => new()
    {
        ValidIssuer = Options.Issuer,
        ValidAudiences = Audiences.All,
        IssuerSigningKey = keys.SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ManokshaClaimTypes.Subject,
        LifetimeValidator = (notBefore, expires, _, _) => expires is { } e && e > clock.UtcNow.UtcDateTime,
    };

    public static string NewRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

internal static class ClaimsPrincipalExtensions
{
    public static string? Get(this ClaimsPrincipal principal, string type) => principal.FindFirst(type)?.Value;
}
