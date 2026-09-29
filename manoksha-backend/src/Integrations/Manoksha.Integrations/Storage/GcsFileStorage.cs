using System.Net;
using System.Security.Cryptography;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Manoksha.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Manoksha.Integrations.Storage;

public sealed class GcsStorageOptions
{
    /// <summary>Private bucket: deposit proofs and original (HD) media, stored under "{container}/".</summary>
    public string PrivateBucket { get; set; } = default!;

    /// <summary>Public-read bucket: optimized media renditions only.</summary>
    public string PublicBucket { get; set; } = default!;

    /// <summary>Optional base URL for public media (e.g. a CDN later); defaults to https://storage.googleapis.com/{PublicBucket}.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Local development only: act as this service account using the developer's own Google login (Application Default
    /// Credentials) — needed to sign upload URLs without key files. Leave empty on Cloud Run (the runtime identity signs).
    /// </summary>
    public string? ImpersonateServiceAccount { get; set; }

    /// <summary>Project billed for API quota when using a developer login (e.g. the project that owns the buckets).</summary>
    public string? QuotaProject { get; set; }
}

/// <summary>Google Cloud Storage. On Cloud Run, signed URLs are signed through IAM (no key files).</summary>
public sealed class GcsFileStorage : IFileStorage
{
    private readonly GcsStorageOptions _options;
    private readonly StorageClient _client;
    private readonly UrlSigner _signer;

    public GcsFileStorage(IOptions<GcsStorageOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.PrivateBucket) || string.IsNullOrWhiteSpace(_options.PublicBucket))
        {
            throw new InvalidOperationException("Integrations:Storage:Gcs:PrivateBucket and PublicBucket must be configured.");
        }
        var credential = GoogleCredential.GetApplicationDefault();
        if (!string.IsNullOrWhiteSpace(_options.QuotaProject))
        {
            credential = credential.CreateWithQuotaProject(_options.QuotaProject);
        }
        if (!string.IsNullOrWhiteSpace(_options.ImpersonateServiceAccount))
        {
            credential = credential.Impersonate(new ImpersonatedCredential.Initializer(_options.ImpersonateServiceAccount)
            {
                Scopes = ["https://www.googleapis.com/auth/cloud-platform"],
            });
        }
        _client = StorageClient.Create(credential);
        _signer = UrlSigner.FromCredential(credential);
    }

    public async Task<StoredObject> PutAsync(string container, string objectKey, Stream content, string contentType, CancellationToken cancellationToken,
        string? cacheControl = null)
    {
        var (bucket, name) = Locate(container, objectKey);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var hashing = new HashingStream(content, hash);
        var obj = await _client.UploadObjectAsync(new Google.Apis.Storage.v1.Data.Object
        {
            Bucket = bucket,
            Name = name,
            ContentType = contentType,
            CacheControl = cacheControl,
        }, hashing, cancellationToken: cancellationToken);
        return new StoredObject(container, objectKey, (long)(obj.Size ?? 0), Convert.ToHexString(hash.GetHashAndReset()));
    }

    public async Task<Stream> OpenReadAsync(string container, string objectKey, CancellationToken cancellationToken)
    {
        var (bucket, name) = Locate(container, objectKey);
        var buffer = new MemoryStream();
        await _client.DownloadObjectAsync(bucket, name, buffer, cancellationToken: cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    public async Task<Uri> GetReadUrlAsync(string container, string objectKey, TimeSpan validFor, CancellationToken cancellationToken)
    {
        var (bucket, name) = Locate(container, objectKey);
        return new Uri(await _signer.SignAsync(bucket, name, validFor, HttpMethod.Get, cancellationToken: cancellationToken));
    }

    public async Task<string> GetUploadUrlAsync(string container, string objectKey, string contentType, TimeSpan validFor, CancellationToken cancellationToken)
    {
        var (bucket, name) = Locate(container, objectKey);
        var template = UrlSigner.RequestTemplate.FromBucket(bucket).WithObjectName(name).WithHttpMethod(HttpMethod.Put)
            .WithContentHeaders(new Dictionary<string, IEnumerable<string>> { ["Content-Type"] = [contentType] });
        return await _signer.SignAsync(template, UrlSigner.Options.FromDuration(validFor), cancellationToken);
    }

    public string GetPublicUrl(string container, string objectKey)
    {
        var (bucket, name) = Locate(container, objectKey);
        if (bucket != _options.PublicBucket)
        {
            throw new InvalidOperationException($"Container '{container}' is private.");
        }
        var baseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl) ? $"https://storage.googleapis.com/{bucket}" : _options.PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{string.Join('/', name.Split('/').Select(Uri.EscapeDataString))}";
    }

    public async Task<StoredObjectInfo?> GetInfoAsync(string container, string objectKey, CancellationToken cancellationToken)
    {
        var (bucket, name) = Locate(container, objectKey);
        try
        {
            var obj = await _client.GetObjectAsync(bucket, name, cancellationToken: cancellationToken);
            return new StoredObjectInfo((long)(obj.Size ?? 0), obj.ContentType);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string container, string objectKey, CancellationToken cancellationToken)
    {
        var (bucket, name) = Locate(container, objectKey);
        try
        {
            await _client.DeleteObjectAsync(bucket, name, cancellationToken: cancellationToken);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private (string Bucket, string Name) Locate(string container, string objectKey)
    {
        if (objectKey.Contains("..", StringComparison.Ordinal) || objectKey.StartsWith('/'))
        {
            throw new InvalidOperationException("Invalid object key.");
        }
        return container == StorageContainers.Media ? (_options.PublicBucket, objectKey) : (_options.PrivateBucket, $"{container}/{objectKey}");
    }

    /// <summary>Hashes the bytes as they are uploaded (one pass).</summary>
    private sealed class HashingStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            hash.AppendData(buffer, offset, n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            hash.AppendData(buffer.Span[..n]);
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
