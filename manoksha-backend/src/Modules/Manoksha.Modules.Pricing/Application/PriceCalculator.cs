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
        }
        return ids.Select(id => new RetailPriceLine(id, retail[id].RetailPriceId, retail[id].Price)).ToList();
    }

    public async Task<IReadOnlyList<ResellerPriceLine>> QuoteForResellerAsync(Guid resellerId, IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default)
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
        }

        var terms = await resellers.GetCurrentTermsAsync(resellerId, cancellationToken);
        var productDiscounts = await GetProductDiscountsAsync(skus.Values.Select(s => s.ProductId).Distinct().ToList(), cancellationToken);
        return ids.Select(id =>
        {
            var sku = skus[id];
            var price = retail[id];
            var hasProductDiscount = productDiscounts.TryGetValue(sku.ProductId, out var pd);
            var (source, pct, final) = ResellerPricing.Calculate(price.Price, terms.DiscountPct, hasProductDiscount ? pd.Pct : null);
            return new ResellerPriceLine(id, price.RetailPriceId, price.Price, source, pct, final, terms.TermId, terms.Version, hasProductDiscount ? pd.Id : null);
        }).ToList();
    }
}
