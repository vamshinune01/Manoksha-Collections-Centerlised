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

        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Orders").RequireAudience(Audiences.Admin);
        admin.MapGet("/orders", (string? channel, string? status, Guid? branchId, Guid? resellerId, OrderQueryService s, CancellationToken ct) =>
            s.ListAsync(channel, status, branchId, resellerId, ct)).RequirePermission(Permissions.Orders.View).WithName("ListOrders");
        admin.MapGet("/orders/{id:guid}", (Guid id, OrderQueryService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(Permissions.Orders.View).WithName("GetOrder");
        admin.MapGet("/fulfillment-inquiries", async (ManokshaDbContext db, CancellationToken ct) =>
                (await db.Set<FulfillmentInquiry>().AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(200).ToListAsync(ct))
                .Select(i => new FulfillmentInquiryDto(i.Id, i.Reference, i.Channel.ToString(), i.ResellerId, i.ContactName, i.ContactMobile, i.CartJson, i.EvaluationsJson,
                    i.FailureReason, i.Status, i.CreatedAt)).ToList())
            .RequirePermission(Permissions.Exceptions.View).WithName("ListFulfillmentInquiries");
    }
}
