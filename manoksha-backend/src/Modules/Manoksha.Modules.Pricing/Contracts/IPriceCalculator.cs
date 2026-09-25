namespace Manoksha.Modules.Pricing.Contracts;

public static class DiscountSources
{
    public const string None = "NONE";
    public const string Reseller = "RESELLER";
    public const string ProductReseller = "PRODUCT_RESELLER";
}

public sealed record RetailPriceInfo(Guid SkuId, Guid RetailPriceId, decimal Price);

/// <summary>
/// Authoritative reseller price for one SKU, with everything an order line must snapshot (SPEC §15):
/// retail/base price, applicable discount source and %, final unit price, and the versions they came from.
/// </summary>
public sealed record ResellerPriceLine(
    Guid SkuId,
    Guid RetailPriceId,
    decimal RetailPrice,
    string DiscountSource,
    decimal DiscountPct,
    decimal FinalUnitPrice,
    Guid CommercialTermId,
    int CommercialTermVersion,
    Guid? ProductDiscountId);

/// <summary>Backend price calculation — frontends never compute authoritative prices (SPEC §2, §15).</summary>
public interface IPriceCalculator
{
    Task<IReadOnlyDictionary<Guid, RetailPriceInfo>> GetRetailPricesAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prices SKUs for a reseller. Throws when a SKU is not sellable to resellers (inactive, not available for resellers, or
    /// unpriced) — the backend rejects such lines even if requested directly (ADR-001 §6).
    /// </summary>
    Task<IReadOnlyList<ResellerPriceLine>> QuoteForResellerAsync(Guid resellerId, IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default);
}
