using System.Security.Cryptography;
using System.Text;

namespace Manoksha.Modules.Identity.Infrastructure;

/// <summary>RFC 6238 TOTP (SHA-1, 6 digits, 30 s) compatible with standard authenticator apps.</summary>
internal static class Totp
{
    public const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string BuildOtpAuthUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period={StepSeconds}";

    public static long TimeStep(DateTimeOffset at) => at.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] key, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(timeStep & 0xFF);
            timeStep >>= 8;
        }
#pragma warning disable CA5350 // RFC 6238 authenticator compatibility requires HMAC-SHA1.
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Verifies within ±1 step; returns the matched time step.</summary>
    public static long? Verify(string base32Secret, string code, DateTimeOffset now)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            return null;
        }
        var key = Base32Decode(base32Secret);
        var current = TimeStep(now);
        for (var delta = -1; delta <= 1; delta++)
        {
            var candidate = Compute(key, current + delta);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(code)))
            {
                return current + delta;
            }
        }
        return null;
    }

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }
        return sb.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        var clean = input.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 character.");
            }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. output];
    }
}
