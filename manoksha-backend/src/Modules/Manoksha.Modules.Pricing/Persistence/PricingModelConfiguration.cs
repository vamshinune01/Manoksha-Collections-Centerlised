using Manoksha.Modules.Pricing.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Pricing.Persistence;

public sealed class PricingModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "pricing";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RetailPrice>(b =>
        {
            b.ToTable("retail_prices", SchemaName, t => t.HasCheckConstraint("ck_retail_prices_positive", "price > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.SkuId, x.EffectiveFrom });
            // Exactly one current price per SKU.
            b.HasIndex(x => x.SkuId).IsUnique().HasFilter("effective_to IS NULL").HasDatabaseName("ux_retail_prices_current");
        });

        modelBuilder.Entity<ProductResellerDiscount>(b =>
        {
            b.ToTable("product_reseller_discounts", SchemaName, t => t.HasCheckConstraint("ck_product_discount_range", "discount_pct >= 0 AND discount_pct <= 100"));
            b.HasKey(x => x.Id);
            b.Property(x => x.DiscountPct).HasPrecision(7, 4);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.Property(x => x.EndReason).HasMaxLength(2000);
            b.HasIndex(x => new { x.ProductId, x.EffectiveFrom });
            b.HasIndex(x => x.ProductId).IsUnique().HasFilter("effective_to IS NULL").HasDatabaseName("ux_product_reseller_discounts_current");
        });
    }
}
