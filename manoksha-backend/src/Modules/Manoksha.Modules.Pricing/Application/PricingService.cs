using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Pricing.Domain;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Pricing.Application;

/// <summary>Owner-controlled retail prices and product reseller discounts (SPEC §15). Every change is versioned and audited.</summary>
internal sealed class PricingService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    ICatalogLookup catalog,
    IResellerDirectory resellers,
    PriceCalculator calculator,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<IReadOnlyList<SkuPriceDto>> ListAsync(IReadOnlyList<SkuInfo> skus, CancellationToken ct)
    {
        var ids = skus.Select(s => s.SkuId).ToList();
        var prices = await db.Set<RetailPrice>().AsNoTracking().Where(p => ids.Contains(p.SkuId) && p.EffectiveTo == null).ToDictionaryAsync(p => p.SkuId, ct);
        var discounts = await calculator.GetProductDiscountsAsync(skus.Select(s => s.ProductId).Distinct().ToList(), ct);
        return skus.Select(s => new SkuPriceDto(s.SkuId, s.SkuCode, s.ProductId, s.ProductName, s.VariantName, s.ProductStatus, s.AvailableForRetail, s.AvailableForReseller,
            prices.GetValueOrDefault(s.SkuId)?.Price, prices.GetValueOrDefault(s.SkuId)?.EffectiveFrom,
            discounts.TryGetValue(s.ProductId, out var d) ? d.Pct : null)).ToList();
    }

    public async Task<IReadOnlyList<RetailPriceHistoryDto>> HistoryAsync(Guid skuId, CancellationToken ct) =>
        await db.Set<RetailPrice>().AsNoTracking().Where(p => p.SkuId == skuId).OrderByDescending(p => p.EffectiveFrom)
            .Select(p => new RetailPriceHistoryDto(p.Id, p.Price, p.EffectiveFrom, p.EffectiveTo, p.SetBy, p.Reason)).ToListAsync(ct);

    public Task<IReadOnlyList<RetailPriceHistoryDto>> SetRetailPriceAsync(Guid skuId, SetRetailPriceRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var sku = await catalog.FindSkuAsync(skuId, innerCt) ?? throw new NotFoundException("SKU_NOT_FOUND", "SKU not found.");
            // Lock the current row so two concurrent changes cannot both become "current".
            var current = await db.Set<RetailPrice>()
                .FromSqlInterpolated($"SELECT * FROM pricing.retail_prices WHERE sku_id = {skuId} AND effective_to IS NULL FOR UPDATE")
                .SingleOrDefaultAsync(innerCt);
            if (current?.Price == r.Price)
            {
                throw new BusinessRuleException("PRICE_UNCHANGED", "The new price is the same as the current price.", 400);
            }
            var now = clock.UtcNow;
            current?.Supersede(now);
            await db.SaveChangesAsync(innerCt);
            db.Add(new RetailPrice(skuId, r.Price, currentUser.UserId, r.Reason.Trim(), now));
            await audit.RecordAsync(new AuditRecord("pricing.retail_price.changed", "Sku", skuId.ToString(),
                Before: current is null ? null : new { price = current.Price },
                After: new { price = r.Price, sku.SkuCode, sku.ProductName }, Reason: r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await HistoryAsync(skuId, innerCt);
        }, ct);
    }

    public async Task<ProductDiscountDto> GetProductDiscountAsync(Guid productId, CancellationToken ct)
    {
        var history = await db.Set<ProductResellerDiscount>().AsNoTracking().Where(d => d.ProductId == productId).OrderByDescending(d => d.EffectiveFrom)
            .Select(d => new ProductDiscountHistoryDto(d.Id, d.DiscountPct, d.EffectiveFrom, d.EffectiveTo, d.SetBy, d.Reason, d.EndReason)).ToListAsync(ct);
        return new ProductDiscountDto(productId, history.FirstOrDefault(h => h.EffectiveTo is null)?.DiscountPct, history);
    }

    /// <summary>Sets the product reseller discount that overrides every reseller's normal discount for this product.</summary>
    public Task<ProductDiscountDto> SetProductDiscountAsync(Guid productId, SetProductDiscountRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureProductAsync(productId, innerCt);
            var current = await LockCurrentDiscountAsync(productId, innerCt);
            var now = clock.UtcNow;
            current?.End(now, "Replaced");
            await db.SaveChangesAsync(innerCt);
            db.Add(new ProductResellerDiscount(productId, r.DiscountPct, currentUser.UserId, r.Reason.Trim(), now));
            await audit.RecordAsync(new AuditRecord("pricing.product_reseller_discount.changed", "Product", productId.ToString(),
                Before: current is null ? null : new { discountPct = current.DiscountPct }, After: new { discountPct = r.DiscountPct }, Reason: r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await GetProductDiscountAsync(productId, innerCt);
        }, ct);
    }

    /// <summary>Removes the override: resellers fall back to their own normal discount for this product.</summary>
    public Task<ProductDiscountDto> ClearProductDiscountAsync(Guid productId, PricingReasonRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var current = await LockCurrentDiscountAsync(productId, innerCt)
                ?? throw new BusinessRuleException("PRODUCT_DISCOUNT_NOT_SET", "This product has no reseller discount to remove.", 400);
            current.End(clock.UtcNow, r.Reason.Trim());
            await audit.RecordAsync(new AuditRecord("pricing.product_reseller_discount.removed", "Product", productId.ToString(),
                Before: new { discountPct = current.DiscountPct }, After: new { discountPct = (decimal?)null }, Reason: r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await GetProductDiscountAsync(productId, innerCt);
        }, ct);
    }

    /// <summary>Owner preview of the calculation for one reseller and SKU.</summary>
    public async Task<PricePreviewDto> PreviewAsync(Guid resellerId, Guid skuId, CancellationToken ct)
    {
        _ = await resellers.FindAsync(resellerId, ct) ?? throw new NotFoundException("RESELLER_NOT_FOUND", "Reseller not found.");
        var line = (await calculator.QuoteForResellerAsync(resellerId, [skuId], ct))[0];
        var terms = await resellers.GetCurrentTermsAsync(resellerId, ct);
        var sku = (await catalog.FindSkuAsync(skuId, ct))!;
        var discounts = await calculator.GetProductDiscountsAsync([sku.ProductId], ct);
        return new PricePreviewDto(resellerId, terms.Version, terms.DiscountPct, discounts.TryGetValue(sku.ProductId, out var d) ? d.Pct : null,
            line.RetailPrice, line.DiscountSource, line.DiscountPct, line.FinalUnitPrice);
    }

    private async Task<ProductResellerDiscount?> LockCurrentDiscountAsync(Guid productId, CancellationToken ct) =>
        await db.Set<ProductResellerDiscount>()
            .FromSqlInterpolated($"SELECT * FROM pricing.product_reseller_discounts WHERE product_id = {productId} AND effective_to IS NULL FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private async Task EnsureProductAsync(Guid productId, CancellationToken ct)
    {
        if (!await catalog.ProductExistsAsync(productId, ct))
        {
            throw new NotFoundException("PRODUCT_NOT_FOUND", "Product not found.");
        }
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for price changes.", 400);
        }
    }
}
