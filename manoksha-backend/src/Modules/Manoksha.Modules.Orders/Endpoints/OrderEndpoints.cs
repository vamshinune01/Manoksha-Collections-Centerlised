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

        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Orders").RequireAudience(Audiences.Admin);
        admin.MapGet("/orders", (string? channel, string? status, Guid? branchId, Guid? resellerId, OrderQueryService s, CancellationToken ct) =>
            s.ListAsync(channel, status, branchId, resellerId, ct)).RequirePermission(Permissions.Orders.View).WithName("ListOrders");
        admin.MapGet("/orders/{id:guid}", (Guid id, OrderQueryService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(Permissions.Orders.View).WithName("GetOrder");
        admin.MapGet("/resellers/{resellerId:guid}/customers", (Guid resellerId, string? q, ResellerCustomerService s, CancellationToken ct) =>
            s.ListForResellerAsync(resellerId, q, ct)).RequirePermission(Permissions.Resellers.View).WithName("ListResellerCustomersForOwner");
        admin.MapGet("/fulfillment-inquiries", async (ManokshaDbContext db, CancellationToken ct) =>
                (await db.Set<FulfillmentInquiry>().AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(ct))
                .Select(i => new FulfillmentInquiryDto(i.Id, i.Reference, i.Channel.ToString(), i.ResellerId, i.ContactName, i.ContactMobile, i.CartJson, i.EvaluationsJson,
                    i.FailureReason, i.Status, i.CreatedAt)).ToList())
            .RequirePermission(Permissions.Exceptions.View).WithName("ListFulfillmentInquiries");
    }
}
