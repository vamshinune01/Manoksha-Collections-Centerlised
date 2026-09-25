using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Resellers.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Resellers.Endpoints;

internal static class ResellerEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/resellers").WithTags("Resellers").RequireAudience(Audiences.Admin);
        admin.MapGet("/", (string? q, string? status, ResellerService s, CancellationToken ct) => s.ListAsync(q, status, ct))
            .RequirePermission(Permissions.Resellers.View).WithName("ListResellers");
        admin.MapGet("/{id:guid}", (Guid id, ResellerService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(Permissions.Resellers.View).WithName("GetReseller");
        admin.MapPost("/", (CreateResellerRequest r, ResellerService s, CancellationToken ct) => s.CreateAsync(r, ct))
            .RequirePermission(Permissions.Resellers.Manage).WithName("CreateReseller");
        admin.MapPut("/{id:guid}", (Guid id, UpdateResellerRequest r, ResellerService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .RequirePermission(Permissions.Resellers.Manage).WithName("UpdateReseller");
        admin.MapPost("/{id:guid}/status", (Guid id, ChangeResellerStatusRequest r, ResellerService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct))
            .RequirePermission(Permissions.Resellers.Manage).WithName("ChangeResellerStatus");
        admin.MapPost("/{id:guid}/commercial-terms", (Guid id, ChangeTermsRequest r, ResellerService s, CancellationToken ct) => s.ChangeTermsAsync(id, r, ct))
            .RequirePermission(Permissions.Resellers.Manage).WithName("ChangeResellerTerms");

        // Reseller self-service: identity comes only from the reseller token.
        var self = endpoints.MapGroup("/api/v1/reseller").WithTags("Reseller portal").RequireAudience(Audiences.Reseller);
        self.MapGet("/me", (ResellerService s, CancellationToken ct) => s.GetSelfAsync(ct)).WithName("ResellerMe");
        self.MapGet("/commercial-terms", (ResellerService s, CancellationToken ct) => s.GetSelfTermsAsync(ct)).WithName("ResellerCommercialTerms");
    }
}
