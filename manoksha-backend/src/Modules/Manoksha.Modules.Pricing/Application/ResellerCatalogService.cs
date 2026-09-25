using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Pricing.Application;

/// <summary>
/// The signed-in reseller's own catalog with their prices (SPEC §19.2, §31). The reseller is resolved from the token only,
/// so one reseller can never price or browse as another.
/// </summary>
internal sealed class ResellerCatalogService(ICatalogLookup catalog, IResellerDirectory resellers, PriceCalculator calculator, ICurrentUser currentUser)
{
    public async Task<ResellerCatalogPage> CatalogAsync(string? q, Guid? categoryId, int? page, int? pageSize, CancellationToken ct)
    {
        var reseller = await SelfAsync(ct);
        var skus = await catalog.ListSellableSkusAsync(SalesChannel.Reseller, q, categoryId, page ?? 1, pageSize ?? 24, ct);
        var ids = skus.Items.Select(s => s.Sku.SkuId).ToList();
        var priced = (await calculator.GetRetailPricesAsync(ids, ct)).Keys.ToHashSet();
        var lines = (await calculator.QuoteForResellerAsync(reseller.ResellerId, ids.Where(priced.Contains).ToList(), ct)).ToDictionary(l => l.SkuId);
        var items = skus.Items.Where(s => lines.ContainsKey(s.Sku.SkuId)).Select(s =>
        {
            var l = lines[s.Sku.SkuId];
            return new ResellerCatalogItemDto(s.Sku.SkuId, s.Sku.SkuCode, s.Sku.ProductId, s.Sku.ProductName, s.Sku.VariantName, s.CategoryName,
                l.RetailPrice, l.DiscountSource, l.DiscountPct, l.FinalUnitPrice);
        }).ToList();
        return new ResellerCatalogPage(items, skus.Total, skus.Page, skus.PageSize);
    }

    public async Task<IReadOnlyList<ResellerPriceLine>> QuoteAsync(QuoteRequest r, CancellationToken ct)
    {
        var reseller = await SelfAsync(ct);
        var ids = r.SkuIds ?? [];
        if (ids.Count is 0 or > 100)
        {
            throw new BusinessRuleException("QUOTE_INVALID", "Quote between 1 and 100 items.", 400);
        }
        return await calculator.QuoteForResellerAsync(reseller.ResellerId, ids, ct);
    }

    private async Task<ResellerInfo> SelfAsync(CancellationToken ct) =>
        await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");
}
