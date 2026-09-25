using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Inventory.Endpoints;

internal static class InventoryEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        foreach (var (prefix, audience) in new[] { ("/api/v1/admin/inventory", Audiences.Admin), ("/api/v1/pos/inventory", Audiences.Pos) })
        {
            var g = endpoints.MapGroup(prefix).WithTags("Inventory").RequireAudience(audience);
            var suffix = audience == Audiences.Admin ? string.Empty : "_Pos";

            g.MapGet("/stock", (Guid? branchId, Guid? skuId, InventoryQueryService s, CancellationToken ct) => s.StockAsync(branchId, skuId, ct))
                .RequirePermission(Permissions.Inventory.View).WithName("GetStock" + suffix);
            g.MapGet("/items", (Guid skuId, Guid? branchId, string? status, InventoryQueryService s, CancellationToken ct) => s.ItemsAsync(skuId, branchId, status, ct))
                .RequirePermission(Permissions.Inventory.View).WithName("GetItems" + suffix);
            g.MapGet("/movements", (Guid? skuId, Guid? itemId, Guid? branchId, string? reference, long? beforeSeq, InventoryQueryService s, CancellationToken ct) =>
                s.MovementsAsync(skuId, itemId, branchId, reference, beforeSeq, ct)).RequirePermission(Permissions.Inventory.View).WithName("GetMovements" + suffix);

            g.MapGet("/transfers", (string? status, Guid? branchId, TransferService s, CancellationToken ct) => s.ListAsync(status, branchId, ct))
                .RequirePermission(Permissions.Inventory.View).WithName("ListTransfers" + suffix);
            g.MapGet("/transfers/{id:guid}", (Guid id, TransferService s, CancellationToken ct) => s.GetAsync(id, ct))
                .RequirePermission(Permissions.Inventory.View).WithName("GetTransfer" + suffix);
            g.MapPost("/transfers", (CreateTransferRequest r, TransferService s, CancellationToken ct) => s.CreateAsync(r, ct))
                .RequirePermission(Permissions.Transfers.Create).WithName("CreateTransfer" + suffix);
            g.MapPost("/transfers/{id:guid}/approve", (Guid id, DecisionRequest r, TransferService s, CancellationToken ct) => s.ApproveAsync(id, r, ct))
                .RequirePermission(Permissions.Transfers.Approve).WithName("ApproveTransfer" + suffix);
            g.MapPost("/transfers/{id:guid}/reject", (Guid id, InventoryReasonRequest r, TransferService s, CancellationToken ct) => s.RejectAsync(id, r, ct))
                .RequirePermission(Permissions.Transfers.Approve).WithName("RejectTransfer" + suffix);
            g.MapPost("/transfers/{id:guid}/prepare", (Guid id, LinesRequest r, TransferService s, CancellationToken ct) => s.PrepareAsync(id, r, ct))
                .RequirePermission(Permissions.Transfers.Dispatch).WithName("PrepareTransfer" + suffix);
            g.MapPost("/transfers/{id:guid}/dispatch", (Guid id, TransferService s, CancellationToken ct) => s.DispatchAsync(id, ct))
                .RequirePermission(Permissions.Transfers.Dispatch).WithName("DispatchTransfer" + suffix);
            g.MapPost("/transfers/{id:guid}/receive", (Guid id, LinesRequest r, TransferService s, CancellationToken ct) => s.ReceiveAsync(id, r, ct))
                .RequirePermission(Permissions.Transfers.Receive).WithName("ReceiveTransfer" + suffix);
            g.MapPost("/transfers/{id:guid}/cancel", (Guid id, InventoryReasonRequest r, TransferService s, CancellationToken ct) => s.CancelAsync(id, r, ct))
                .WithName("CancelTransfer" + suffix);

            g.MapGet("/counts", (Guid? branchId, CountService s, CancellationToken ct) => s.ListAsync(branchId, ct))
                .RequirePermission(Permissions.Inventory.Count).WithName("ListCounts" + suffix);
            g.MapGet("/counts/{id:guid}", (Guid id, CountService s, CancellationToken ct) => s.GetAsync(id, ct))
                .RequirePermission(Permissions.Inventory.Count).WithName("GetCount" + suffix);
            g.MapPost("/counts", (CreateCountRequest r, CountService s, CancellationToken ct) => s.CreateAsync(r, ct))
                .RequirePermission(Permissions.Inventory.Count).WithName("CreateCount" + suffix);
            g.MapPut("/counts/{id:guid}/lines", (Guid id, RecordCountsRequest r, CountService s, CancellationToken ct) => s.RecordAsync(id, r, ct))
                .RequirePermission(Permissions.Inventory.Count).WithName("RecordCounts" + suffix);
            g.MapPost("/counts/{id:guid}/submit", (Guid id, CountService s, CancellationToken ct) => s.SubmitAsync(id, ct))
                .RequirePermission(Permissions.Inventory.Count).WithName("SubmitCount" + suffix);
        }

        var admin = endpoints.MapGroup("/api/v1/admin/inventory").WithTags("Inventory").RequireAudience(Audiences.Admin);
        admin.MapGet("/adjustments", (string? status, Guid? branchId, AdjustmentService s, CancellationToken ct) => s.ListAsync(status, branchId, ct))
            .WithName("ListAdjustments");
        admin.MapPost("/adjustments", (CreateAdjustmentRequest r, AdjustmentService s, CancellationToken ct) => s.RequestAsync(r, ct))
            .RequirePermission(Permissions.Inventory.AdjustRequest).WithName("RequestAdjustment");
        admin.MapPost("/adjustments/{id:guid}/approve", (Guid id, ApproveAdjustmentRequest r, AdjustmentService s, CancellationToken ct) => s.ApproveAsync(id, r, ct))
            .WithName("ApproveAdjustment");
        admin.MapPost("/adjustments/{id:guid}/reject", (Guid id, InventoryReasonRequest r, AdjustmentService s, CancellationToken ct) => s.RejectAsync(id, r, ct))
            .WithName("RejectAdjustment");
        admin.MapGet("/discrepancies", (string? status, Guid? branchId, DiscrepancyService s, CancellationToken ct) => s.ListAsync(status, branchId, ct))
            .RequirePermission(Permissions.Inventory.View).WithName("ListDiscrepancies");
        admin.MapGet("/discrepancies/{id:guid}", (Guid id, DiscrepancyService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(Permissions.Inventory.View).WithName("GetDiscrepancy");
        admin.MapPost("/discrepancies/{id:guid}/resolve", (Guid id, ResolveDiscrepancyRequest r, DiscrepancyService s, CancellationToken ct) => s.ResolveAsync(id, r, ct))
            .RequirePermission(Permissions.Inventory.DiscrepancyResolve).WithName("ResolveDiscrepancy");

        // POS / mobile scanning: product + piece location/status + branch availability.
        endpoints.MapGet("/api/v1/pos/scan/{code}", (string code, InventoryQueryService s, CancellationToken ct) => s.ScanAsync(code, ct))
            .WithTags("POS").RequireAudience(Audiences.Pos).RequirePermission(Permissions.Catalog.View).WithName("PosScan");
    }
}
