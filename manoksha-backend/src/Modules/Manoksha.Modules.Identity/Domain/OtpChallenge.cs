using Manoksha.SharedKernel;

namespace Manoksha.Modules.Identity.Domain;

internal enum OtpChallengeStatus
{
    Active = 1,
    Consumed = 2,
    Exhausted = 3,
    Superseded = 4,
    DeliveryFailed = 5,

    /// <summary>Not sent (no eligible account). Recorded so rate limits still apply; response is identical.</summary>
    Suppressed = 6,
}

/// <summary>A one-time password challenge bound to one mobile number AND one account context.</summary>
internal sealed class OtpChallenge : Entity
{
    private OtpChallenge()
    {
    }

    public OtpChallenge(Guid id, string mobileE164, string context, string codeHash, DateTimeOffset now, DateTimeOffset expiresAt, int maxAttempts, string? ipAddress, OtpChallengeStatus status)
        : base(id)
    {
        MobileE164 = mobileE164;
        Context = context;
        CodeHash = codeHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
        MaxAttempts = maxAttempts;
        IpAddress = ipAddress;
        Status = status;
    }

    public string MobileE164 { get; private set; } = default!;

    /// <summary>"customer" or "reseller".</summary>
    public string Context { get; private set; } = default!;

    public string CodeHash { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public OtpChallengeStatus Status { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public string? IpAddress { get; private set; }

    public void MarkDeliveryFailed() => Status = OtpChallengeStatus.DeliveryFailed;
}
