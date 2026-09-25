using System.Security.Cryptography;
using System.Text;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manoksha.Modules.Identity.Application;

public static class OtpContexts
{
    public const string Customer = "customer";
    public const string Reseller = "reseller";

    public static AccountType ToAccountType(string context) => context switch
    {
        Customer => AccountType.Customer,
        Reseller => AccountType.Reseller,
        _ => throw new BusinessRuleException("OTP_CONTEXT_INVALID", "Context must be 'customer' or 'reseller'.", 400),
    };
}

public sealed record OtpRequestResult(int ExpiresInSeconds, int ResendAfterSeconds);

/// <summary>
/// Mobile OTP. Codes are hashed with a secret pepper, single-use, time-limited and attempt-limited, and bound to
/// one account context. If SMS delivery fails the request fails — there is no silent fallback (SPEC §5.2).
/// </summary>
internal sealed class OtpService(
    ManokshaDbContext db,
    ISmsSender smsSender,
    KeyMaterial keys,
    IOptions<IdentityOptions> options,
    IRequestContext requestContext,
    IClock clock,
    ILogger<OtpService> logger)
{
    private OtpOptions Options => options.Value.Otp;

    public async Task<OtpRequestResult> RequestAsync(string mobileRaw, string context, CancellationToken ct)
    {
        var mobile = MobileNumber.Normalize(mobileRaw);
        var accountType = OtpContexts.ToAccountType(context);
        var now = clock.UtcNow;

        var recent = await db.Set<OtpChallenge>().AsNoTracking()
            .Where(c => c.MobileE164 == mobile && c.Context == context && c.CreatedAt > now.AddHours(-1))
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);
        if (recent.Count > 0)
        {
            var retryAfter = recent.Max().AddSeconds(Options.ResendCooldownSeconds) - now;
            if (retryAfter > TimeSpan.Zero)
            {
                throw RateLimited("OTP_RESEND_TOO_SOON", "Please wait before requesting another code.", retryAfter);
            }
        }
        if (recent.Count >= Options.MaxPerHour)
        {
            throw RateLimited("OTP_RATE_LIMITED", "Too many codes requested for this number. Try again later.", recent.Min().AddHours(1) - now);
        }

        // Resellers cannot self-register (SPEC §5.3): only send when an Owner-created reseller account exists.
        // The response is identical either way to avoid revealing which numbers are resellers.
        var eligible = accountType != AccountType.Reseller || await db.Set<User>().AnyAsync(
            u => u.AccountType == AccountType.Reseller && u.MobileE164 == mobile
                 && (u.Status == UserStatus.Pending || u.Status == UserStatus.Active), ct);

        await db.Set<OtpChallenge>()
            .Where(c => c.MobileE164 == mobile && c.Context == context && c.Status == OtpChallengeStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, OtpChallengeStatus.Superseded), ct);

        var id = Uuid7.NewGuid();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        var challenge = new OtpChallenge(id, mobile, context, HashCode(id, code), now, now.AddSeconds(Options.TtlSeconds),
            Options.MaxAttempts, requestContext.IpAddress, eligible ? OtpChallengeStatus.Active : OtpChallengeStatus.Suppressed);
        db.Add(challenge);
        await db.SaveChangesAsync(ct);

        if (eligible)
        {
            try
            {
                await smsSender.SendAsync(new SmsMessage(mobile, Options.SmsTemplateId,
                    $"{code} is your Manoksha Collections verification code. It expires in {Options.TtlSeconds / 60} minutes. Do not share it."), ct);
            }
            catch (SmsDeliveryException ex)
            {
                logger.LogWarning(ex, "OTP SMS delivery failed for {Mobile}", MobileNumber.Mask(mobile));
                challenge.MarkDeliveryFailed();
                await db.SaveChangesAsync(CancellationToken.None);
                throw new BusinessRuleException("OTP_DELIVERY_FAILED",
                    "We could not send the verification code right now. Please try again shortly.", 503);
            }
        }

        return new OtpRequestResult(Options.TtlSeconds, Options.ResendCooldownSeconds);
    }

    /// <summary>Verifies and consumes the latest active code for this mobile + context. Returns the normalized mobile.</summary>
    public async Task<string> VerifyAsync(string mobileRaw, string context, string code, CancellationToken ct)
    {
        var mobile = MobileNumber.Normalize(mobileRaw);
        OtpContexts.ToAccountType(context);
        var now = clock.UtcNow;

        var challenge = await db.Set<OtpChallenge>().AsNoTracking()
            .Where(c => c.MobileE164 == mobile && c.Context == context && c.Status == OtpChallengeStatus.Active)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (challenge is null || challenge.ExpiresAt <= now)
        {
            throw new BusinessRuleException("OTP_INVALID_OR_EXPIRED", "The code is invalid or has expired. Request a new code.", 400);
        }

        // Atomically count the attempt; concurrent guesses cannot exceed the limit.
        var counted = await db.Set<OtpChallenge>()
            .Where(c => c.Id == challenge.Id && c.Status == OtpChallengeStatus.Active && c.Attempts < c.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (counted == 0)
        {
            throw new BusinessRuleException("OTP_ATTEMPTS_EXCEEDED", "Too many incorrect attempts. Request a new code.", 400);
        }

        var candidate = HashCode(challenge.Id, (code ?? string.Empty).Trim());
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(challenge.CodeHash)))
        {
            if (challenge.Attempts + 1 >= challenge.MaxAttempts)
            {
                await db.Set<OtpChallenge>().Where(c => c.Id == challenge.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, OtpChallengeStatus.Exhausted), ct);
                throw new BusinessRuleException("OTP_ATTEMPTS_EXCEEDED", "Too many incorrect attempts. Request a new code.", 400);
            }
            throw new BusinessRuleException("OTP_INVALID_OR_EXPIRED", "The code is invalid or has expired.", 400);
        }

        var consumed = await db.Set<OtpChallenge>()
            .Where(c => c.Id == challenge.Id && c.Status == OtpChallengeStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, OtpChallengeStatus.Consumed)
                .SetProperty(c => c.ConsumedAt, now), ct);
        if (consumed == 0)
        {
            throw new BusinessRuleException("OTP_INVALID_OR_EXPIRED", "The code is invalid or has expired.", 400);
        }
        return mobile;
    }

    private string HashCode(Guid challengeId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(keys.OtpPepper, Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));

    private static BusinessRuleException RateLimited(string code, string message, TimeSpan retryAfter)
    {
        var ex = new BusinessRuleException(code, message, 429);
        ex.Details["retryAfterSeconds"] = (int)Math.Ceiling(Math.Max(1, retryAfter.TotalSeconds));
        return ex;
    }
}
