using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Orders.Application;

/// <summary>
/// Anonymous storefront browsing (SPEC §19.1, ADR-001 §10): only products available for retail, active, and priced are shown, at
/// the current retail price. Everything here is display-only; checkout re-prices and re-validates.
/// </summary>
internal sealed class StorefrontService(ICatalogLookup catalog, IPriceCalculator prices, IStockAvailability stock)
{
    public async Task<StorefrontPage> ListAsync(string? q, Guid? categoryId, int? page, int? pageSize, CancellationToken ct)
    {
        var skus = await catalog.ListSellableSkusAsync(SalesChannel.Retail, q, categoryId, page ?? 1, pageSize ?? 24, ct);
        var ids = skus.Items.Select(s => s.Sku.SkuId).ToList();
        var retail = await prices.GetRetailPricesAsync(ids, ct);
        var available = await stock.AvailableQuantitiesAsync(ids, ct);
        var items = skus.Items.Where(s => retail.ContainsKey(s.Sku.SkuId)).Select(s =>
            new StorefrontItemDto(s.Sku.SkuId, s.Sku.SkuCode, s.Sku.ProductId, s.Sku.ProductName, s.Sku.VariantName, s.CategoryName, retail[s.Sku.SkuId].Price,
                available.GetValueOrDefault(s.Sku.SkuId) > 0)).ToList();
        return new StorefrontPage(items, skus.Total, skus.Page, skus.PageSize);
    }

    public async Task<StorefrontProductDto> ProductAsync(Guid productId, CancellationToken ct)
    {
        var skus = (await catalog.SearchSkusAsync(null, productId, 100, ct)).Where(IsSellable).ToList();
        var ids = skus.Select(s => s.SkuId).ToList();
        var retail = await prices.GetRetailPricesAsync(ids, ct);
        var available = await stock.AvailableQuantitiesAsync(ids, ct);
        var variants = skus.Where(s => retail.ContainsKey(s.SkuId)).OrderBy(s => s.VariantName)
            .Select(s => new StorefrontVariantDto(s.SkuId, s.SkuCode, s.VariantName, retail[s.SkuId].Price, available.GetValueOrDefault(s.SkuId) > 0)).ToList();
        if (variants.Count == 0)
        {
            throw new NotFoundException("PRODUCT_NOT_FOUND", "This product is not available online.");
        }
        return new StorefrontProductDto(productId, skus[0].ProductName, variants);
    }

    public async Task<IReadOnlyList<CartQuoteLineDto>> CartQuoteAsync(CartQuoteRequest request, CancellationToken ct)
    {
        var ids = (request.SkuIds ?? []).Distinct().ToList();
        if (ids.Count is 0 or > CheckoutRules.MaxLines)
        {
            throw new BusinessRuleException("CART_INVALID", $"Quote between 1 and {CheckoutRules.MaxLines} items.", 400);
        }
        var skus = await catalog.FindSkusAsync(ids, ct);
        var retail = await prices.GetRetailPricesAsync(ids, ct);
        var available = await stock.AvailableQuantitiesAsync(ids, ct);
        return ids.Select(id =>
        {
            if (!skus.TryGetValue(id, out var sku))
            {
                return new CartQuoteLineDto(id, false, null, null, null, false, "This item no longer exists.");
            }
            if (!IsSellable(sku) || !retail.TryGetValue(id, out var price))
            {
                return new CartQuoteLineDto(id, false, sku.ProductName, sku.VariantName, null, false, "This item is not available online.");
            }
            return new CartQuoteLineDto(id, true, sku.ProductName, sku.VariantName, price.Price, available.GetValueOrDefault(id) > 0, null);
        }).ToList();
    }

    private static bool IsSellable(SkuInfo s) => s.AvailableForRetail && s.ProductStatus == "Active" && s.VariantActive;
}
