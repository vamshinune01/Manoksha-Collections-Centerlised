using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Settings.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Settings.Endpoints;

internal static class SettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/settings").WithTags("Settings").RequireAudience(Audiences.Admin);

        admin.MapGet("/", async (SettingsService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission(Permissions.Settings.View)
            .WithName("ListSettings");

        admin.MapPut("/{key}", async (string key, UpdateSettingRequest request, SettingsService service, CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(key, request, ct)))
            .RequirePermission(Permissions.Settings.Manage)
            .WithName("UpdateSetting");
    }
}
