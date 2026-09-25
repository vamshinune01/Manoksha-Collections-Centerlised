using Manoksha.SharedKernel;

namespace Manoksha.Modules.Pricing.Domain;

/// <summary>Retail selling price history per SKU (Owner-controlled, SPEC §15). The open row (no EffectiveTo) is current.</summary>
internal sealed class RetailPrice : Entity
{
    private RetailPrice()
    {
    }

    public RetailPrice(Guid skuId, decimal price, Guid setBy, string reason, DateTimeOffset now)
    {
        if (price <= 0 || !Money.HasValidScale(price))
        {
            throw new BusinessRuleException("PRICE_INVALID", "Retail price must be greater than zero with at most 2 decimals.", 400);
        }
        SkuId = skuId;
        Price = price;
        SetBy = setBy;
        Reason = reason;
        EffectiveFrom = now;
    }

    public Guid SkuId { get; private set; }

    public decimal Price { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public Guid SetBy { get; private set; }

    public string Reason { get; private set; } = default!;

    public void Supersede(DateTimeOffset at) => EffectiveTo ??= at;
}

/// <summary>Product-level reseller discount history. When present it overrides every reseller's normal discount for that product.</summary>
internal sealed class ProductResellerDiscount : Entity
{
    private ProductResellerDiscount()
    {
    }

    public ProductResellerDiscount(Guid productId, decimal discountPct, Guid setBy, string reason, DateTimeOffset now)
    {
        if (discountPct is < 0 or > 100 || decimal.Round(discountPct, 2) != discountPct)
        {
            throw new BusinessRuleException("DISCOUNT_PERCENT_INVALID", "Discount must be between 0 and 100 with at most 2 decimals.", 400);
        }
        ProductId = productId;
        DiscountPct = discountPct;
        SetBy = setBy;
        Reason = reason;
        EffectiveFrom = now;
    }

    public Guid ProductId { get; private set; }

    public decimal DiscountPct { get; private set; }

    public DateTimeOffset EffectiveFrom { get; private set; }

    public DateTimeOffset? EffectiveTo { get; private set; }

    public Guid SetBy { get; private set; }

    public string Reason { get; private set; } = default!;

    public string? EndReason { get; private set; }

    public void End(DateTimeOffset at, string reason)
    {
        EffectiveTo ??= at;
        EndReason ??= reason;
    }
}

/// <summary>The pricing rule of SPEC §15, isolated for unit testing.</summary>
internal static class ResellerPricing
{
    /// <summary>
    /// Applicable discount = product reseller discount when configured, otherwise the reseller's discount — never both.
    /// Final unit price = retail × (1 − applicable/100), HALF-UP to paisa (ADR-001 §11).
    /// </summary>
    public static (string Source, decimal Pct, decimal FinalUnitPrice) Calculate(decimal retailPrice, decimal resellerDiscountPct, decimal? productDiscountPct)
    {
        var (source, pct) = productDiscountPct is { } p ? ("PRODUCT_RESELLER", p) : ("RESELLER", resellerDiscountPct);
        return (source, pct, Money.ApplyPercentDiscount(retailPrice, pct));
    }
}
