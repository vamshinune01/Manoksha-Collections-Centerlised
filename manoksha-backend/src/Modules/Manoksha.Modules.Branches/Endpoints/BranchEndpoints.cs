using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Branches.Endpoints;

internal static class BranchEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Branches").RequireAudience(Audiences.Admin);

        admin.MapGet("/branches", (BranchService s, CancellationToken ct) => s.ListDtosAsync(ct))
            .RequirePermission(Permissions.Branches.View).WithName("ListBranches");
        admin.MapGet("/branches/{id:guid}", (Guid id, BranchService s, CancellationToken ct) => s.GetDtoAsync(id, ct))
            .RequirePermission(Permissions.Branches.View).WithName("GetBranch");
        admin.MapPost("/branches", async (CreateBranchRequest r, BranchService s, CancellationToken ct) =>
            {
                var b = await s.CreateAsync(r, ct);
                return Results.Created($"/api/v1/admin/branches/{b.Id}", b);
            })
            .RequirePermission(Permissions.Branches.Manage).WithName("CreateBranch");
        admin.MapPut("/branches/{id:guid}", (Guid id, UpdateBranchRequest r, BranchService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .RequirePermission(Permissions.Branches.Manage).WithName("UpdateBranch");
        admin.MapPost("/branches/{id:guid}/status", (Guid id, ChangeBranchStatusRequest r, BranchService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct))
            .RequirePermission(Permissions.Branches.Manage).WithName("ChangeBranchStatus");

        admin.MapGet("/fulfillment-priority", (BranchService s, CancellationToken ct) => s.GetPriorityDtoAsync(ct))
            .RequirePermission(Permissions.Branches.View).WithName("GetFulfillmentPriority");
        admin.MapGet("/fulfillment-priority/history", (BranchService s, CancellationToken ct) => s.GetPriorityHistoryAsync(ct))
            .RequirePermission(Permissions.Branches.View).WithName("GetFulfillmentPriorityHistory");
        admin.MapPut("/fulfillment-priority", (SetPriorityRequest r, BranchService s, CancellationToken ct) => s.SetPriorityAsync(r, ct))
            .RequirePermission(Permissions.Branches.FulfillmentPriorityManage).WithName("SetFulfillmentPriority");
    }
}
