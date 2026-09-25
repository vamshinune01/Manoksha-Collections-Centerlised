using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Audit.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Audit.Endpoints;

internal static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/audit").WithTags("Audit").RequireAudience(Audiences.Admin);

        admin.MapGet("/", async (
                string? entityType, string? entityId, Guid? actorUserId, string? action,
                DateTimeOffset? from, DateTimeOffset? to, long? beforeSeq, int? limit,
                AuditQueryService service, CancellationToken ct) =>
            Results.Ok(await service.SearchAsync(new AuditSearchQuery(entityType, entityId, actorUserId, action, from, to, beforeSeq, limit), ct)))
            .RequirePermission(Permissions.Audit.View)
            .WithName("SearchAudit");
    }
}
