using Manoksha.Application.Abstractions;

namespace Manoksha.IntegrationTests.Infrastructure;

/// <summary>Failure injection: delegates to the real storage, but writes throw while <c>fail()</c> is true (storage outage).</summary>
public sealed class FaultyFileStorage(IFileStorage inner, Func<bool> fail) : IFileStorage
{
    public Task<StoredObject> PutAsync(string container, string objectKey, Stream content, string contentType, CancellationToken cancellationToken, string? cacheControl = null) =>
        fail() ? throw new IOException("Simulated storage outage") : inner.PutAsync(container, objectKey, content, contentType, cancellationToken, cacheControl);

    public Task<Stream> OpenReadAsync(string container, string objectKey, CancellationToken cancellationToken) => inner.OpenReadAsync(container, objectKey, cancellationToken);

    public Task<Uri> GetReadUrlAsync(string container, string objectKey, TimeSpan validFor, CancellationToken cancellationToken) =>
        inner.GetReadUrlAsync(container, objectKey, validFor, cancellationToken);

    public Task<string> GetUploadUrlAsync(string container, string objectKey, string contentType, TimeSpan validFor, CancellationToken cancellationToken) =>
        inner.GetUploadUrlAsync(container, objectKey, contentType, validFor, cancellationToken);

    public string GetPublicUrl(string container, string objectKey) => inner.GetPublicUrl(container, objectKey);

    public Task<StoredObjectInfo?> GetInfoAsync(string container, string objectKey, CancellationToken cancellationToken) => inner.GetInfoAsync(container, objectKey, cancellationToken);

    public Task DeleteAsync(string container, string objectKey, CancellationToken cancellationToken) =>
        fail() ? throw new IOException("Simulated storage outage") : inner.DeleteAsync(container, objectKey, cancellationToken);
}
