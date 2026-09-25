using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Manoksha.Modules.Identity.Infrastructure;

/// <summary>
/// Holds signing/encryption secrets. Outside Development/Testing every secret must be configured (Secret
/// Manager); ephemeral keys are generated only for local development and tests.
/// </summary>
internal sealed class KeyMaterial
{
    public KeyMaterial(IOptions<IdentityOptions> options, IHostEnvironment environment)
    {
        var o = options.Value;
        var allowEphemeral = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!string.IsNullOrWhiteSpace(o.SigningKeyPem))
        {
            ecdsa.ImportFromPem(o.SigningKeyPem);
        }
        else if (!allowEphemeral)
        {
            throw new InvalidOperationException("Auth:SigningKeyPem must be configured outside Development/Testing.");
        }
        SigningKey = new ECDsaSecurityKey(ecdsa) { KeyId = Convert.ToHexString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()))[..16] };
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256);

        DataEncryptionKey = Resolve(o.DataEncryptionKey, "Auth:DataEncryptionKey", allowEphemeral, 32);
        OtpPepper = Resolve(o.Otp.Pepper, "Auth:Otp:Pepper", allowEphemeral, 32);
    }

    public ECDsaSecurityKey SigningKey { get; }

    public SigningCredentials SigningCredentials { get; }

    public byte[] DataEncryptionKey { get; }

    public byte[] OtpPepper { get; }

    private static byte[] Resolve(string? base64, string name, bool allowEphemeral, int length)
    {
        if (!string.IsNullOrWhiteSpace(base64))
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length < length)
            {
                throw new InvalidOperationException($"{name} must be at least {length} bytes (base64).");
            }
            return bytes[..length];
        }
        if (!allowEphemeral)
        {
            throw new InvalidOperationException($"{name} must be configured outside Development/Testing.");
        }
        return RandomNumberGenerator.GetBytes(length);
    }
}
