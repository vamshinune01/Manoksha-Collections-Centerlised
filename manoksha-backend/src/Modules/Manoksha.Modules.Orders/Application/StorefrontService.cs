using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Orders.Application;

/// <summary>
/// Anonymous storefront browsing (SPEC §19.1, ADR-001 §10): only products available for retail, active, and priced are shown, at
/// the current retail price. Everything here is display-only; checkout re-prices and re-validates.
/// </summary>
internal sealed class StorefrontService(ICatalogLookup catalog, ICatalogMedia media, IPriceCalculator prices, IStockAvailability stock, IResellerDirectory resellers,
    ISettingsReader settings, ICurrentUser currentUser)
{
    public async Task<StorefrontPage> ListAsync(string? q, Guid? categoryId, Guid? vendorId, int? page, int? pageSize, CancellationToken ct)
    {
        var skus = await catalog.ListSellableSkusAsync(SalesChannel.Retail, q, categoryId, page ?? 1, pageSize ?? 24, vendorId, ct);
        var ids = skus.Items.Select(s => s.Sku.SkuId).ToList();
        var online = await prices.GetOnlinePricesAsync(ids, ct);
        var available = await stock.AvailableQuantitiesAsync(ids, ct);
        var images = await media.PrimaryImagesAsync(skus.Items.Select(s => s.Sku.ProductId).Distinct().ToList(), ct);
        var items = skus.Items.Where(s => online.ContainsKey(s.Sku.SkuId)).Select(s =>
        {
            var p = online[s.Sku.SkuId];
            return new StorefrontItemDto(s.Sku.SkuId, s.Sku.SkuCode, s.Sku.ProductId, s.Sku.ProductName, s.Sku.VariantName, s.CategoryName, p.FinalPrice,
                InStock(s.Sku, available), images.GetValueOrDefault(s.Sku.ProductId), p.RetailPrice, p.DiscountPct, s.Sku.VendorId, s.Sku.VendorName, s.Sku.ProductCode);
        }).ToList();
        return new StorefrontPage(items, skus.Total, skus.Page, skus.PageSize);
    }

    /// <summary>Vendor products sell until marked out of stock; branch products need branch stock (hint only).</summary>
    private static bool InStock(SkuInfo sku, IReadOnlyDictionary<Guid, int> available) =>
        sku.VendorId is not null ? !sku.OutOfStock : available.GetValueOrDefault(sku.SkuId) > 0;

    /// <summary>Cart totals for display: price per line for this buyer, grouped by vendor with each vendor's shipping (ADR-001 §46).</summary>
    public async Task<CartSummaryDto> CartSummaryAsync(CartSummaryRequest request, bool asReseller, CancellationToken ct)
    {
        var lines = (request.Lines ?? []).Where(l => l.Quantity > 0).GroupBy(l => l.SkuId).Select(g => new CheckoutLineRequest(g.Key, Math.Min(1000, g.Sum(x => x.Quantity)))).ToList();
        if (lines.Count is 0 or > CheckoutRules.MaxLines)
        {
            throw new BusinessRuleException("CART_INVALID", $"Quote between 1 and {CheckoutRules.MaxLines} items.", 400);
        }
        var ids = lines.Select(l => l.SkuId).ToList();
        var skus = await catalog.FindSkusAsync(ids, ct);
        var online = await prices.GetOnlinePricesAsync(ids, ct);
        var available = await stock.AvailableQuantitiesAsync(ids, ct);
        bool Sellable(SkuInfo s) => (asReseller ? s.AvailableForReseller : s.AvailableForRetail) && s.ProductStatus == "Active" && s.VariantActive;
        var buyable = ids.Where(id => skus.TryGetValue(id, out var s) && Sellable(s) && online.ContainsKey(id) && InStock(s, available)).ToList();
        IReadOnlyDictionary<Guid, (decimal Retail, decimal Pct, decimal Unit)> unit;
        if (asReseller)
        {
            var reseller = await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");
            unit = buyable.Count == 0 ? new Dictionary<Guid, (decimal, decimal, decimal)>()
                : (await prices.QuoteForResellerAsync(reseller.ResellerId, buyable, ct)).ToDictionary(q => q.SkuId, q => (q.RetailPrice, q.DiscountPct, q.FinalUnitPrice));
        }
        else
        {
            unit = online.ToDictionary(kv => kv.Key, kv => (kv.Value.RetailPrice, kv.Value.DiscountPct, kv.Value.FinalPrice));
        }
        var result = lines.Select(l =>
        {
            if (!skus.TryGetValue(l.SkuId, out var s))
            {
                return new CartSummaryLineDto(l.SkuId, l.Quantity, false, false, null, null, null, null, null, null, 0, null, 0, "This item no longer exists.");
            }
            var sellable = Sellable(s) && unit.ContainsKey(l.SkuId);
            var inStock = InStock(s, available);
            var u = sellable ? unit[l.SkuId] : default;
            var countable = sellable && inStock && buyable.Contains(l.SkuId);
            return new CartSummaryLineDto(l.SkuId, l.Quantity, sellable, inStock, s.ProductName, s.VariantName, s.ProductCode, s.VendorId, s.VendorName,
                sellable ? u.Retail : null, sellable ? u.Pct : 0, sellable ? u.Unit : null, countable ? u.Unit * l.Quantity : 0,
                !sellable ? "This item is not available." : !inStock ? "Out of stock." : null);
        }).ToList();
        var counted = result.Where(r => r.LineTotal > 0).ToList();
        var vendors = await catalog.GetVendorsAsync(counted.Where(r => r.VendorId is not null).Select(r => r.VendorId!.Value).Distinct().ToList(), ct);
        var groups = counted.GroupBy(r => r.VendorId).Select(g => g.Key is { } v
                ? new CartVendorGroupDto(v, vendors[v].Name, g.Sum(x => x.LineTotal), vendors[v].ShippingFee)
                : new CartVendorGroupDto(null, "Manoksha Collections", g.Sum(x => x.LineTotal), 0m))
            .OrderBy(g => g.VendorName).ToList();
        if (groups.FirstOrDefault(g => g.VendorId is null) is { } branchGroup)
        {
            var fee = await settings.GetAsync<decimal>(SettingKeys.ShippingFeePerOrder, ct);
            groups[groups.IndexOf(branchGroup)] = branchGroup with { ShippingFee = fee };
        }
        var merchandise = counted.Sum(r => r.LineTotal);
        var shipping = groups.Sum(g => g.ShippingFee);
        return new CartSummaryDto(result, groups, merchandise, shipping, merchandise + shipping);
    }

    public async Task<StorefrontProductDto> ProductAsync(Guid productId, CancellationToken ct)
    {
        var skus = (await catalog.SearchSkusAsync(null, productId, 100, ct)).Where(IsSellable).ToList();
        var ids = skus.Select(s => s.SkuId).ToList();
        var online = await prices.GetOnlinePricesAsync(ids, ct);
        var available = await stock.AvailableQuantitiesAsync(ids, ct);
        var variants = skus.Where(s => online.ContainsKey(s.SkuId)).OrderBy(s => s.VariantName)
            .Select(s => new StorefrontVariantDto(s.SkuId, s.SkuCode, s.VariantName, online[s.SkuId].FinalPrice, InStock(s, available), online[s.SkuId].RetailPrice,
                online[s.SkuId].DiscountPct)).ToList();
        if (variants.Count == 0)
        {
            throw new NotFoundException("PRODUCT_NOT_FOUND", "This product is not available online.");
        }
        return new StorefrontProductDto(productId, skus[0].ProductName, variants, await media.GalleryAsync(productId, ct), skus[0].VendorName, skus[0].ProductCode);
    }

    public async Task<IReadOnlyList<CartQuoteLineDto>> CartQuoteAsync(CartQuoteRequest request, CancellationToken ct)
    {
        var ids = (request.SkuIds ?? []).Distinct().ToList();
        if (ids.Count is 0 or > CheckoutRules.MaxLines)
        {
            throw new BusinessRuleException("CART_INVALID", $"Quote between 1 and {CheckoutRules.MaxLines} items.", 400);
        }
        var skus = await catalog.FindSkusAsync(ids, ct);
        var retail = await prices.GetOnlinePricesAsync(ids, ct);
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
            return new CartQuoteLineDto(id, true, sku.ProductName, sku.VariantName, price.FinalPrice, InStock(sku, available), null);
        }).ToList();
    }

    private static bool IsSellable(SkuInfo s) => s.AvailableForRetail && s.ProductStatus == "Active" && s.VariantActive;
}
