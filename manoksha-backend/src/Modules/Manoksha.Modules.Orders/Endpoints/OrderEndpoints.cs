using Manoksha.Application.Abstractions;
using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Orders.Application;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Orders.Endpoints;

internal static class OrderEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var reseller = endpoints.MapGroup("/api/v1/reseller").WithTags("Reseller portal").RequireAudience(Audiences.Reseller);
        reseller.MapPost("/checkout", (ResellerCheckoutRequest r, HttpRequest http, ResellerCheckoutService s, CancellationToken ct) =>
            s.CheckoutAsync(r, http.GetRequiredIdempotencyKey(), ct)).WithName("ResellerCheckout");
        reseller.MapGet("/orders", (OrderQueryService s, CancellationToken ct) => s.ListMineAsync(ct)).WithName("ResellerOrders");
        reseller.MapGet("/orders/{id:guid}", (Guid id, OrderQueryService s, CancellationToken ct) => s.GetMineAsync(id, ct)).WithName("ResellerOrder");
        reseller.MapGet("/customers", (string? q, ResellerCustomerService s, CancellationToken ct) => s.ListAsync(q, ct)).WithName("ResellerCustomers");
        reseller.MapPost("/customers", (SaveResellerCustomerRequest r, ResellerCustomerService s, CancellationToken ct) => s.CreateAsync(r, ct)).WithName("CreateResellerCustomer");
        reseller.MapPut("/customers/{id:guid}", (Guid id, SaveResellerCustomerRequest r, ResellerCustomerService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .WithName("UpdateResellerCustomer");

        // Public storefront: anonymous browsing (ADR-001 §10). Prices shown here are display-only.
        var store = endpoints.MapGroup("/api/v1/catalog").WithTags("Storefront").AllowAnonymous();
        store.MapGet("/products", (string? q, Guid? categoryId, int? page, int? pageSize, StorefrontService s, CancellationToken ct) =>
            s.ListAsync(q, categoryId, page, pageSize, ct)).WithName("StorefrontProducts");
        store.MapGet("/products/{productId:guid}", (Guid productId, StorefrontService s, CancellationToken ct) => s.ProductAsync(productId, ct)).WithName("StorefrontProduct");
        store.MapGet("/order-charges", async (ISettingsReader settings, CancellationToken ct) =>
            new OrderChargesDto(await settings.GetAsync<decimal>(SettingKeys.ShippingFeePerOrder, ct))).WithName("StorefrontOrderCharges");
        store.MapPost("/cart-quote", (CartQuoteRequest r, StorefrontService s, CancellationToken ct) => s.CartQuoteAsync(r, ct)).WithName("StorefrontCartQuote");

        // Signed-in customers: checkout requires login (ADR-001 §10); no cancel endpoint exists (SPEC §21).
        var customer = endpoints.MapGroup("/api/v1/customer").WithTags("Customer").RequireAudience(Audiences.Customer);
        customer.MapPost("/checkout", (CustomerCheckoutRequest r, HttpRequest http, CustomerCheckoutService s, CancellationToken ct) =>
            s.CheckoutAsync(r, http.GetRequiredIdempotencyKey(), ct)).WithName("CustomerCheckout");
        customer.MapGet("/orders", (OrderQueryService s, CancellationToken ct) => s.ListForCustomerAsync(ct)).WithName("CustomerOrders");
        customer.MapGet("/orders/{id:guid}", (Guid id, OrderQueryService s, CancellationToken ct) => s.GetForCustomerAsync(id, ct)).WithName("CustomerOrder");
        customer.MapGet("/orders/{id:guid}/payment", (Guid id, OnlineOrderService s, CancellationToken ct) => s.PaymentStatusAsync(id, ct)).WithName("CustomerOrderPayment");
        customer.MapGet("/delivery-defaults", async (OrderQueryService s, CancellationToken ct) => Results.Ok(await s.LastDeliveryAsync(ct)))
            .Produces<DeliveryDto>().WithName("CustomerDeliveryDefaults");

        // POS store sales (Phase 8). Branch permissions are checked in the service for the requested branch.
        var pos = endpoints.MapGroup("/api/v1/pos").WithTags("POS").RequireAudience(Audiences.Pos);
        pos.MapGet("/context", (PosSaleService s, CancellationToken ct) => s.ContextAsync(ct)).WithName("PosContext");
        pos.MapGet("/items", (Guid branchId, string? q, PosSaleService s, CancellationToken ct) => s.SearchAsync(branchId, q, ct)).WithName("PosSearchItems");
        pos.MapPost("/sales/quote", (PosSaleRequest r, PosSaleService s, CancellationToken ct) => s.QuoteAsync(r, ct)).WithName("PosQuote");
        pos.MapGet("/approvers", (Guid branchId, string level, PosSaleService s, CancellationToken ct) => s.ApproversAsync(branchId, level, ct)).WithName("PosApprovers");
        pos.MapPost("/sales", (PosSaleRequest r, HttpRequest http, PosSaleService s, CancellationToken ct) => s.SellAsync(r, http.GetRequiredIdempotencyKey(), ct))
            .WithName("PosFinalizeSale");
        pos.MapGet("/sales/today", (Guid branchId, PosSaleService s, CancellationToken ct) => s.TodayAsync(branchId, ct)).WithName("PosSalesToday");
        pos.MapGet("/sales/{id:guid}/receipt", (Guid id, PosSaleService s, CancellationToken ct) => s.ReceiptAsync(id, ct)).WithName("PosReceipt");

        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Orders").RequireAudience(Audiences.Admin);
        admin.MapGet("/orders", (string? channel, string? status, Guid? branchId, Guid? resellerId, OrderQueryService s, CancellationToken ct) =>
            s.ListAsync(channel, status, branchId, resellerId, ct)).RequirePermission(Permissions.Orders.View).WithName("ListOrders");
        admin.MapGet("/orders/{id:guid}", (Guid id, OrderQueryService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(Permissions.Orders.View).WithName("GetOrder");
        // ---- Fulfillment (Phase 7). Permission checks are per order branch inside the service. ----
        admin.MapGet("/fulfillment/queue", (Guid? branchId, OrderQueryService s, CancellationToken ct) => s.QueueAsync(branchId, ct))
            .RequirePermission(Permissions.Orders.View).WithName("FulfillmentQueue");
        admin.MapPost("/orders/{id:guid}/processing", (Guid id, FulfillmentStepRequest r, FulfillmentService s, CancellationToken ct) => s.StartProcessingAsync(id, r, ct))
            .WithName("StartProcessingOrder");
        admin.MapPost("/orders/{id:guid}/packed", (Guid id, FulfillmentStepRequest r, FulfillmentService s, CancellationToken ct) => s.MarkPackedAsync(id, r, ct))
            .WithName("MarkOrderPacked");
        admin.MapPost("/orders/{id:guid}/shipped", (Guid id, ShipOrderRequest r, FulfillmentService s, CancellationToken ct) => s.MarkShippedAsync(id, r, ct))
            .WithName("MarkOrderShipped");
        admin.MapPost("/orders/{id:guid}/delivered", (Guid id, DeliverOrderRequest r, FulfillmentService s, CancellationToken ct) => s.MarkDeliveredAsync(id, r, ct))
            .WithName("MarkOrderDelivered");
        admin.MapPost("/orders/{id:guid}/fulfillment-exceptions", (Guid id, RaiseFulfillmentExceptionRequest r, FulfillmentService s, CancellationToken ct) =>
            s.RaiseExceptionAsync(id, r, ct)).WithName("RaiseFulfillmentException");
        admin.MapPost("/orders/{id:guid}/fulfillment-exceptions/resolve", (Guid id, ResolveExceptionRequest r, FulfillmentService s, CancellationToken ct) =>
            s.ResolveInPlaceAsync(id, r, ct)).WithName("ResolveFulfillmentException");
        admin.MapGet("/orders/{id:guid}/reroute-options", (Guid id, FulfillmentService s, CancellationToken ct) => s.RerouteOptionsAsync(id, ct)).WithName("RerouteOptions");
        admin.MapPost("/orders/{id:guid}/reroute", (Guid id, RerouteRequest r, FulfillmentService s, CancellationToken ct) => s.RerouteAsync(id, r, ct)).WithName("RerouteOrder");
        admin.MapPost("/orders/{id:guid}/cancel", (Guid id, CancelOrderRequest r, FulfillmentService s, CancellationToken ct) => s.CancelAsync(id, r, ct)).WithName("CancelOrder");
        admin.MapGet("/fulfillment-exceptions", (string? status, FulfillmentService s, CancellationToken ct) => s.ListExceptionsAsync(status, ct))
            .RequirePermission(Permissions.Exceptions.View).WithName("ListFulfillmentExceptions");

        admin.MapGet("/resellers/{resellerId:guid}/customers", (Guid resellerId, string? q, ResellerCustomerService s, CancellationToken ct) =>
            s.ListForResellerAsync(resellerId, q, ct)).RequirePermission(Permissions.Resellers.View).WithName("ListResellerCustomersForOwner");
        admin.MapGet("/fulfillment-inquiries", async (ManokshaDbContext db, CancellationToken ct) =>
                (await db.Set<FulfillmentInquiry>().AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(ct))
                .Select(i => new FulfillmentInquiryDto(i.Id, i.Reference, i.Channel.ToString(), i.ResellerId, i.ContactName, i.ContactMobile, i.CartJson, i.EvaluationsJson,
                    i.FailureReason, i.Status, i.CreatedAt, i.FollowUpNote, i.ClosedAt)).ToList())
            .RequirePermission(Permissions.Exceptions.View).WithName("ListFulfillmentInquiries");
        admin.MapPost("/fulfillment-inquiries/{id:guid}/close", async (Guid id, CloseInquiryRequest r, InquiryService s, CancellationToken ct) =>
                await s.CloseAsync(id, r, ct))
            .RequirePermission(Permissions.Exceptions.Manage).WithName("CloseFulfillmentInquiry");
    }
}
