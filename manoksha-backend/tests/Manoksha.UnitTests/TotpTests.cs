using System.Text;
using Manoksha.Modules.Identity.Infrastructure;

namespace Manoksha.UnitTests;

public class TotpTests
{
    // RFC 6238 Appendix B (SHA-1 seed "12345678901234567890"); 6-digit values are the last 6 digits.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Matches_rfc6238_vectors(long unixSeconds, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        Totp.Compute(key, unixSeconds / Totp.StepSeconds).Should().Be(expected);
    }

    [Fact]
    public void Base32_round_trips()
    {
        var secret = Totp.GenerateSecret();
        Totp.Base32Encode(Totp.Base32Decode(secret)).Should().Be(secret);
    }

    [Fact]
    public void Verify_accepts_adjacent_step_and_rejects_others()
    {
        var secret = Totp.GenerateSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var key = Totp.Base32Decode(secret);
        var previous = Totp.Compute(key, Totp.TimeStep(now) - 1);
        var old = Totp.Compute(key, Totp.TimeStep(now) - 3);

        Totp.Verify(secret, previous, now).Should().Be(Totp.TimeStep(now) - 1);
        Totp.Verify(secret, old, now).Should().BeNull();
        Totp.Verify(secret, "abc123", now).Should().BeNull();
    }
}
