using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Purchasing.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Purchasing.Endpoints;

internal static class PurchasingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/purchasing").WithTags("Purchasing").RequireAudience(Audiences.Admin);
        var view = Permissions.Purchasing.View;
        var manage = Permissions.Purchasing.Manage;

        admin.MapGet("/suppliers", (SupplierService s, CancellationToken ct) => s.ListAsync(ct)).RequirePermission(view).WithName("ListSuppliers");
        admin.MapGet("/suppliers/{id:guid}", (Guid id, SupplierService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(view).WithName("GetSupplier");
        admin.MapPost("/suppliers", (SaveSupplierRequest r, SupplierService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequirePermission(manage).WithName("CreateSupplier");
        admin.MapPut("/suppliers/{id:guid}", (Guid id, SaveSupplierRequest r, SupplierService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(manage).WithName("UpdateSupplier");

        // Listing is also open to receiving staff (service limits them to receivable POs of their branch, without costs).
        admin.MapGet("/purchase-orders", (string? status, Guid? branchId, PurchaseOrderService s, CancellationToken ct) => s.ListAsync(status, branchId, ct)).WithName("ListPurchaseOrders");
        admin.MapGet("/purchase-orders/{id:guid}", (Guid id, PurchaseOrderService s, CancellationToken ct) => s.GetAsync(id, ct)).WithName("GetPurchaseOrder");
        admin.MapPost("/purchase-orders", (CreatePurchaseOrderRequest r, PurchaseOrderService s, CancellationToken ct) => s.CreateAsync(r, ct)).RequirePermission(manage).WithName("CreatePurchaseOrder");
        admin.MapPut("/purchase-orders/{id:guid}", (Guid id, UpdatePurchaseOrderRequest r, PurchaseOrderService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(manage).WithName("UpdatePurchaseOrder");
        admin.MapPost("/purchase-orders/{id:guid}/lines", (Guid id, AmendLineRequest r, PurchaseOrderService s, CancellationToken ct) => s.AmendLineAsync(id, r, ct)).RequirePermission(manage).WithName("AmendPurchaseOrderLine");
        admin.MapPost("/purchase-orders/{id:guid}/issue", (Guid id, PoReasonRequest r, PurchaseOrderService s, CancellationToken ct) => s.IssueAsync(id, r, ct)).RequirePermission(manage).WithName("IssuePurchaseOrder");
        admin.MapPost("/purchase-orders/{id:guid}/close", (Guid id, PoReasonRequest r, PurchaseOrderService s, CancellationToken ct) => s.CloseAsync(id, r, ct)).RequirePermission(manage).WithName("ClosePurchaseOrder");
        admin.MapPost("/purchase-orders/{id:guid}/cancel", (Guid id, PoReasonRequest r, PurchaseOrderService s, CancellationToken ct) => s.CancelAsync(id, r, ct)).RequirePermission(manage).WithName("CancelPurchaseOrder");

        admin.MapPost("/purchase-orders/{id:guid}/receipts", (Guid id, CreateGoodsReceiptRequest r, HttpRequest http, GoodsReceiptService s, CancellationToken ct) =>
                s.ReceiveAsync(id, r, http.GetRequiredIdempotencyKey(), ct))
            .RequirePermission(Permissions.Purchasing.GoodsReceiptRecord).WithName("RecordGoodsReceipt");
        admin.MapGet("/goods-receipts", (Guid? purchaseOrderId, Guid? branchId, GoodsReceiptService s, CancellationToken ct) => s.ListAsync(purchaseOrderId, branchId, ct)).WithName("ListGoodsReceipts");
    }
}
