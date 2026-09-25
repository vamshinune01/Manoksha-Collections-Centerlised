using Manoksha.SharedKernel;

namespace Manoksha.Modules.Identity.Domain;

/// <summary>
/// A login session (refresh-token family). Access tokens carry the session id and are rejected as soon as the
/// session is revoked.
/// </summary>
internal sealed class AuthSession : Entity
{
    private AuthSession()
    {
    }

    public AuthSession(Guid userId, string audience, DateTimeOffset now, DateTimeOffset expiresAt, string? ipAddress, string? userAgent)
    {
        UserId = userId;
        Audience = audience;
        CreatedAt = now;
        LastRefreshedAt = now;
        ExpiresAt = expiresAt;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent;
    }

    public Guid UserId { get; private set; }

    public string Audience { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastRefreshedAt { get; private set; }

    /// <summary>Absolute session lifetime; refresh cannot extend beyond this.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokeReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Touch(DateTimeOffset now) => LastRefreshedAt = now;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is null)
        {
            RevokedAt = now;
            RevokeReason = reason;
        }
    }
}

internal sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid sessionId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        SessionId = sessionId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set when rotated. A second use of the same token is treated as theft and revokes the session.</summary>
    public DateTimeOffset? UsedAt { get; private set; }
}

internal sealed class LoginEvent : Entity
{
    private LoginEvent()
    {
    }

    public LoginEvent(Guid? userId, string accountType, string identifierMasked, string? audience, string result, string? reason, string? ipAddress, string? userAgent, DateTimeOffset occurredAt)
    {
        UserId = userId;
        AccountType = accountType;
        IdentifierMasked = identifierMasked;
        Audience = audience;
        Result = result;
        Reason = reason;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent;
        OccurredAt = occurredAt;
    }

    public Guid? UserId { get; private set; }

    public string AccountType { get; private set; } = default!;

    public string IdentifierMasked { get; private set; } = default!;

    public string? Audience { get; private set; }

    public string Result { get; private set; } = default!;

    public string? Reason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
