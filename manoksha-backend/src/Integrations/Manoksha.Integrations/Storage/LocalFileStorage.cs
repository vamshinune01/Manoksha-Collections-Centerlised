using System.Security.Cryptography;
using System.Text;
using Manoksha.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Manoksha.Integrations.Storage;

public sealed class LocalFileStorageOptions
{
    public string RootPath { get; set; } = ".local-storage";

    /// <summary>Where the API serves the public (media) container in development.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5080/local-files";

    /// <summary>Upload endpoint as reached from the admin web app (its API proxy forwards to /api/v1/dev-storage).</summary>
    public string UploadBaseUrl { get; set; } = "/api/backend/dev-storage";
}

/// <summary>
/// DEVELOPMENT/TEST ONLY: stores objects on local disk and imitates signed uploads with an HMAC token. Refused in Production;
/// deployments use Google Cloud Storage.
/// </summary>
public sealed class LocalFileStorage(IOptions<LocalFileStorageOptions> options) : IFileStorage
{
    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    public async Task<StoredObject> PutAsync(string container, string objectKey, Stream content, string contentType, CancellationToken cancellationToken,
        string? cacheControl = null)
    {
        var path = Resolve(container, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        await File.WriteAllTextAsync(path + ".content-type", contentType, cancellationToken);
        await using var read = File.OpenRead(path);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(read, cancellationToken));
        return new StoredObject(container, objectKey, new FileInfo(path).Length, sha);
    }

    public Task<Stream> OpenReadAsync(string container, string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.OpenRead(Resolve(container, objectKey)));

    public Task<Uri> GetReadUrlAsync(string container, string objectKey, TimeSpan validFor, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri(GetPublicUrl(container, objectKey)));

    public Task<string> GetUploadUrlAsync(string container, string objectKey, string contentType, TimeSpan validFor, CancellationToken cancellationToken)
    {
        var expires = DateTimeOffset.UtcNow.Add(validFor).ToUnixTimeSeconds();
        var sig = Sign(container, objectKey, contentType, expires);
        return Task.FromResult($"{options.Value.UploadBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(container)}/{Escape(objectKey)}?expires={expires}&sig={sig}");
    }

    public string GetPublicUrl(string container, string objectKey) =>
        $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(container)}/{Escape(objectKey)}";

    public Task<StoredObjectInfo?> GetInfoAsync(string container, string objectKey, CancellationToken cancellationToken)
    {
        var path = Resolve(container, objectKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<StoredObjectInfo?>(null);
        }
        var type = File.Exists(path + ".content-type") ? File.ReadAllText(path + ".content-type") : null;
        return Task.FromResult<StoredObjectInfo?>(new StoredObjectInfo(new FileInfo(path).Length, type));
    }

    public Task DeleteAsync(string container, string objectKey, CancellationToken cancellationToken)
    {
        var path = Resolve(container, objectKey);
        File.Delete(path);
        File.Delete(path + ".content-type");
        return Task.CompletedTask;
    }

    /// <summary>Validates an upload made to the development upload endpoint.</summary>
    public bool IsValidUpload(string container, string objectKey, string contentType, long expires, string sig) =>
        expires >= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(container, objectKey, contentType, expires)), Encoding.ASCII.GetBytes(sig));

    private string Sign(string container, string objectKey, string contentType, long expires) =>
        Convert.ToHexString(HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes($"{container}\n{objectKey}\n{contentType}\n{expires}"))).ToLowerInvariant();

    private static string Escape(string key) => string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private string Resolve(string container, string objectKey)
    {
        var root = Path.GetFullPath(options.Value.RootPath);
        var full = Path.GetFullPath(Path.Combine(root, container, objectKey));
        if (!full.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid object key.");
        }
        return full;
    }
}
