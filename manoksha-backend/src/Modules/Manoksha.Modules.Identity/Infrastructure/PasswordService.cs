using System.Security.Cryptography;
using Manoksha.Modules.Identity.Domain;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Manoksha.Modules.Identity.Infrastructure;

internal sealed class PasswordService(IOptions<IdentityOptions> options)
{
    private static readonly User DummyUser = User.CreateInternal("dummy@invalid.local", "dummy", null, DateTimeOffset.UnixEpoch, null);
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(DummyUser, "not-a-real-password-0");
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password)
    {
        EnsurePolicy(password);
        return _hasher.HashPassword(user, password);
    }

    public bool Verify(User? user, string password)
    {
        if (user?.PasswordHash is null)
        {
            // Equalise timing for unknown accounts.
            _hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            return false;
        }
        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
    }

    public void EnsurePolicy(string password)
    {
        var min = options.Value.Password.MinLength;
        if (string.IsNullOrEmpty(password) || password.Length < min || password.Length > 128
            || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new BusinessRuleException("PASSWORD_POLICY",
                $"Password must be {min}–128 characters and contain at least one letter and one digit.", 400);
        }
    }

    public static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        Span<char> chars = stackalloc char[14];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        // Guarantee policy: at least one letter and one digit.
        chars[0] = "ABCDEFGHJKLMNPQRSTUVWXYZ"[RandomNumberGenerator.GetInt32(24)];
        chars[^1] = "23456789"[RandomNumberGenerator.GetInt32(8)];
        return new string(chars);
    }
}
