using System.Security.Cryptography;
using System.Text;

namespace Manoksha.Modules.Identity.Infrastructure;

/// <summary>AES-256-GCM encryption for small secrets at rest (MFA seeds).</summary>
internal sealed class SecretProtector(KeyMaterial keys)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(keys.DataEncryptionKey, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);
        return "v1." + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedValue)
    {
        if (!protectedValue.StartsWith("v1.", StringComparison.Ordinal))
        {
            throw new CryptographicException("Unsupported protected value format.");
        }
        var bytes = Convert.FromBase64String(protectedValue[3..]);
        var nonce = bytes[..NonceSize];
        var tag = bytes[NonceSize..(NonceSize + TagSize)];
        var cipher = bytes[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(keys.DataEncryptionKey, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
