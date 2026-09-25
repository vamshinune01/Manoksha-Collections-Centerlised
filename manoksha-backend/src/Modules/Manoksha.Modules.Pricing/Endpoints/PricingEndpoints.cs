using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Pricing.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Pricing.Endpoints;

internal static class PricingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/pricing").WithTags("Pricing").RequireAudience(Audiences.Admin);
        admin.MapGet("/skus", async (string? q, Guid? productId, ICatalogLookup catalog, PricingService s, CancellationToken ct) =>
                await s.ListAsync(await catalog.SearchSkusAsync(q, productId, 50, ct), ct))
            .RequirePermission(Permissions.Pricing.View).WithName("ListSkuPrices");
        admin.MapGet("/skus/{skuId:guid}/history", (Guid skuId, PricingService s, CancellationToken ct) => s.HistoryAsync(skuId, ct))
            .RequirePermission(Permissions.Pricing.View).WithName("GetRetailPriceHistory");
        admin.MapPut("/skus/{skuId:guid}/retail-price", (Guid skuId, SetRetailPriceRequest r, PricingService s, CancellationToken ct) => s.SetRetailPriceAsync(skuId, r, ct))
            .RequirePermission(Permissions.Pricing.Manage).WithName("SetRetailPrice");
        admin.MapGet("/products/{productId:guid}/reseller-discount", (Guid productId, PricingService s, CancellationToken ct) => s.GetProductDiscountAsync(productId, ct))
            .RequirePermission(Permissions.Pricing.View).WithName("GetProductResellerDiscount");
        admin.MapPut("/products/{productId:guid}/reseller-discount", (Guid productId, SetProductDiscountRequest r, PricingService s, CancellationToken ct) => s.SetProductDiscountAsync(productId, r, ct))
            .RequirePermission(Permissions.Pricing.Manage).WithName("SetProductResellerDiscount");
        admin.MapPost("/products/{productId:guid}/reseller-discount/clear", (Guid productId, PricingReasonRequest r, PricingService s, CancellationToken ct) => s.ClearProductDiscountAsync(productId, r, ct))
            .RequirePermission(Permissions.Pricing.Manage).WithName("ClearProductResellerDiscount");
        admin.MapGet("/preview", (Guid resellerId, Guid skuId, PricingService s, CancellationToken ct) => s.PreviewAsync(resellerId, skuId, ct))
            .RequirePermission(Permissions.Resellers.View).WithName("PreviewResellerPrice");

        var reseller = endpoints.MapGroup("/api/v1/reseller").WithTags("Reseller portal").RequireAudience(Audiences.Reseller);
        reseller.MapGet("/catalog", (string? q, Guid? categoryId, int? page, int? pageSize, ResellerCatalogService s, CancellationToken ct) =>
            s.CatalogAsync(q, categoryId, page, pageSize, ct)).WithName("ResellerCatalog");
        reseller.MapPost("/price-quote", (QuoteRequest r, ResellerCatalogService s, CancellationToken ct) => s.QuoteAsync(r, ct)).WithName("ResellerPriceQuote");
    }
}
