using System.Security.Cryptography;
using Manoksha.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Manoksha.Integrations.Storage;

public sealed class LocalFileStorageOptions
{
    public string RootPath { get; set; } = ".local-storage";

    public string PublicBaseUrl { get; set; } = "http://localhost:5080/local-files";
}

/// <summary>DEVELOPMENT/TEST ONLY: stores objects on local disk. Production uses Google Cloud Storage.</summary>
public sealed class LocalFileStorage(IOptions<LocalFileStorageOptions> options) : IFileStorage
{
    public async Task<StoredObject> PutAsync(string container, string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = Resolve(container, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var file = File.Create(path))
        {
            await content.CopyToAsync(file, cancellationToken);
        }
        await using var read = File.OpenRead(path);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(read, cancellationToken));
        return new StoredObject(container, objectKey, new FileInfo(path).Length, sha);
    }

    public Task<Stream> OpenReadAsync(string container, string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(File.OpenRead(Resolve(container, objectKey)));

    public Task<Uri> GetReadUrlAsync(string container, string objectKey, TimeSpan validFor, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"{options.Value.PublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(container)}/{Uri.EscapeDataString(objectKey)}"));

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
