using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Modules.Pricing.Domain;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Pricing.Application;

internal sealed class PriceCalculator(ManokshaDbContext db, ICatalogLookup catalog, IResellerDirectory resellers) : IPriceCalculator
{
    public async Task<IReadOnlyDictionary<Guid, RetailPriceInfo>> GetRetailPricesAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default) =>
        await db.Set<RetailPrice>().AsNoTracking()
            .Where(p => skuIds.Contains(p.SkuId) && p.EffectiveTo == null)
            .ToDictionaryAsync(p => p.SkuId, p => new RetailPriceInfo(p.SkuId, p.Id, p.Price), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, (Guid Id, decimal Pct)>> GetProductDiscountsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        await db.Set<ProductResellerDiscount>().AsNoTracking()
            .Where(d => productIds.Contains(d.ProductId) && d.EffectiveTo == null)
            .ToDictionaryAsync(d => d.ProductId, d => (d.Id, d.DiscountPct), ct);

    public async Task<IReadOnlyDictionary<Guid, (Guid Id, decimal Pct)>> GetOnlineDiscountsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        await db.Set<ProductOnlineDiscount>().AsNoTracking()
            .Where(d => productIds.Contains(d.ProductId) && d.EffectiveTo == null)
            .ToDictionaryAsync(d => d.ProductId, d => (d.Id, d.DiscountPct), ct);

    private static void EnsureInStock(SkuInfo sku)
    {
        if (sku.OutOfStock)
        {
            var ex = new BusinessRuleException("SKU_OUT_OF_STOCK", $"{sku.ProductName} ({sku.VariantName}) is out of stock.", 422);
            ex.Details["skuId"] = sku.SkuId;
            throw ex;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, RetailPriceLine>> GetOnlinePricesAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default)
    {
        var retail = await GetRetailPricesAsync(skuIds, cancellationToken);
        if (retail.Count == 0)
        {
            return new Dictionary<Guid, RetailPriceLine>();
        }
        var skus = await catalog.FindSkusAsync(retail.Keys.ToList(), cancellationToken);
        var online = await GetOnlineDiscountsAsync(skus.Values.Select(s => s.ProductId).Distinct().ToList(), cancellationToken);
        return retail.Values.Where(p => skus.ContainsKey(p.SkuId)).ToDictionary(p => p.SkuId, p =>
        {
            var has = online.TryGetValue(skus[p.SkuId].ProductId, out var d);
            var pct = has ? d.Pct : 0m;
            return new RetailPriceLine(p.SkuId, p.RetailPriceId, p.Price, pct, Money.ApplyPercentDiscount(p.Price, pct), has ? d.Id : null);
        });
    }

    public async Task<IReadOnlyList<RetailPriceLine>> QuoteForRetailAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default)
    {
        var ids = skuIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var skus = await catalog.FindSkusAsync(ids, cancellationToken);
        var retail = await GetRetailPricesAsync(ids, cancellationToken);
        foreach (var id in ids)
        {
            if (!skus.TryGetValue(id, out var sku))
            {
                throw new NotFoundException("SKU_NOT_FOUND", "A requested item does not exist.");
            }
            if (!sku.AvailableForRetail || sku.ProductStatus != "Active" || !sku.VariantActive)
            {
                var ex = new BusinessRuleException("SKU_NOT_AVAILABLE_ONLINE", $"{sku.ProductName} ({sku.VariantName}) is not available online.", 422);
                ex.Details["skuId"] = id;
                throw ex;
            }
            if (!retail.ContainsKey(id))
            {
                var ex = new BusinessRuleException("SKU_NOT_PRICED", $"{sku.ProductName} ({sku.VariantName}) has no price yet and cannot be sold.", 422);
                ex.Details["skuId"] = id;
                throw ex;
            }
            EnsureInStock(sku);
        }
        var online = await GetOnlineDiscountsAsync(skus.Values.Select(s => s.ProductId).Distinct().ToList(), cancellationToken);
        return ids.Select(id =>
        {
            var price = retail[id];
            var has = online.TryGetValue(skus[id].ProductId, out var d);
            var pct = has ? d.Pct : 0m;
            return new RetailPriceLine(id, price.RetailPriceId, price.Price, pct, Money.ApplyPercentDiscount(price.Price, pct), has ? d.Id : null);
        }).ToList();
    }

    public Task<IReadOnlyList<ResellerPriceLine>> QuoteForResellerAsync(Guid resellerId, IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default) =>
        PriceForResellerAsync(resellerId, skuIds, requireInStock: true, cancellationToken);

    /// <param name="requireInStock">False only for browsing (catalog pages show out-of-stock items with their price).</param>
    internal async Task<IReadOnlyList<ResellerPriceLine>> PriceForResellerAsync(Guid resellerId, IReadOnlyCollection<Guid> skuIds, bool requireInStock, CancellationToken cancellationToken)
    {
        var ids = skuIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var skus = await catalog.FindSkusAsync(ids, cancellationToken);
        var retail = await GetRetailPricesAsync(ids, cancellationToken);
        foreach (var id in ids)
        {
            if (!skus.TryGetValue(id, out var sku))
            {
                throw new NotFoundException("SKU_NOT_FOUND", "A requested item does not exist.");
            }
            if (!sku.AvailableForReseller || sku.ProductStatus != "Active" || !sku.VariantActive)
            {
                var ex = new BusinessRuleException("SKU_NOT_AVAILABLE_FOR_RESELLER", $"{sku.ProductName} ({sku.VariantName}) is not available to resellers.", 422);
                ex.Details["skuId"] = id;
                throw ex;
            }
            if (!retail.ContainsKey(id))
            {
                var ex = new BusinessRuleException("SKU_NOT_PRICED", $"{sku.ProductName} ({sku.VariantName}) has no price yet and cannot be sold.", 422);
                ex.Details["skuId"] = id;
                throw ex;
            }
            if (requireInStock)
            {
                EnsureInStock(sku);
            }
        }

        var terms = await resellers.GetCurrentTermsAsync(resellerId, cancellationToken);
        var productDiscounts = await GetProductDiscountsAsync(skus.Values.Select(s => s.ProductId).Distinct().ToList(), cancellationToken);
        return ids.Select(id =>
        {
            var sku = skus[id];
            var price = retail[id];
            if (sku.VendorId is { } vendorId)
            {
                // Vendor products: exactly the reseller's own % for this vendor; none set = full retail (ADR-001 §45).
                var vendorPct = terms.VendorDiscounts?.GetValueOrDefault(vendorId) ?? 0m;
                return new ResellerPriceLine(id, price.RetailPriceId, price.Price, vendorPct > 0 ? DiscountSources.ResellerVendor : DiscountSources.None, vendorPct,
                    Money.ApplyPercentDiscount(price.Price, vendorPct), terms.TermId, terms.Version, null);
            }
            var hasProductDiscount = productDiscounts.TryGetValue(sku.ProductId, out var pd);
            var (source, pct, final) = ResellerPricing.Calculate(price.Price, terms.DiscountPct, hasProductDiscount ? pd.Pct : null);
            return new ResellerPriceLine(id, price.RetailPriceId, price.Price, source, pct, final, terms.TermId, terms.Version, hasProductDiscount ? pd.Id : null);
        }).ToList();
    }
}
