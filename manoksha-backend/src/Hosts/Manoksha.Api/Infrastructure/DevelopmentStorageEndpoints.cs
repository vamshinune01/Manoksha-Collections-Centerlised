using Manoksha.Application.Abstractions;
using Manoksha.Integrations.Storage;
using Microsoft.AspNetCore.Http.Features;

namespace Manoksha.Api.Infrastructure;

/// <summary>
/// Stand-ins for Google Cloud Storage when the development disk storage is used (never in Production, which refuses local storage):
/// signed direct uploads (PUT with an HMAC token, like a GCS signed URL) and public reads of the optimized media container.
/// </summary>
internal static class DevelopmentStorageEndpoints
{
    public static void MapDevelopmentStorage(this WebApplication app)
    {
        if (app.Services.GetService<LocalFileStorage>() is null)
        {
            return;
        }

        app.MapPut("/api/v1/dev-storage/{container}/{**key}", async (string container, string key, long expires, string sig, HttpContext http, LocalFileStorage storage) =>
            {
                var contentType = http.Request.ContentType ?? string.Empty;
                if (!storage.IsValidUpload(container, key, contentType, expires, sig))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                {
                    limit.MaxRequestBodySize = 250L * 1024 * 1024;
                }
                await storage.PutAsync(container, key, http.Request.Body, contentType, http.RequestAborted);
                return Results.Ok();
            })
            .AllowAnonymous().ExcludeFromDescription();

        app.MapGet("/local-files/{container}/{**key}", async (string container, string key, LocalFileStorage storage, CancellationToken ct) =>
            {
                if (container != StorageContainers.Media)
                {
                    return Results.NotFound();
                }
                var info = await storage.GetInfoAsync(container, key, ct);
                return info is null
                    ? Results.NotFound()
                    : Results.Stream(await storage.OpenReadAsync(container, key, ct), info.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
            })
            .AllowAnonymous().ExcludeFromDescription();
    }
}
