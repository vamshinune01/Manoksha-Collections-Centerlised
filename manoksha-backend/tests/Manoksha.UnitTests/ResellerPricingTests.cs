using Manoksha.Modules.Pricing.Domain;

namespace Manoksha.UnitTests;

/// <summary>SPEC §15: product reseller discount overrides the reseller discount; discounts never stack; HALF-UP to paisa.</summary>
public class ResellerPricingTests
{
    [Fact]
    public void Reseller_discount_applies_when_no_product_discount()
    {
        var (source, pct, price) = ResellerPricing.Calculate(1000m, 10m, null);
        source.Should().Be("RESELLER");
        pct.Should().Be(10m);
        price.Should().Be(900m);
    }

    [Fact]
    public void Product_discount_overrides_and_does_not_stack()
    {
        var (source, pct, price) = ResellerPricing.Calculate(1000m, 10m, 15m);
        source.Should().Be("PRODUCT_RESELLER");
        pct.Should().Be(15m);
        price.Should().Be(850m, "15% only — never 10% then 15% (765)");
    }

    [Fact]
    public void Product_discount_lower_than_reseller_discount_still_wins()
    {
        var (_, pct, price) = ResellerPricing.Calculate(1000m, 20m, 5m);
        pct.Should().Be(5m);
        price.Should().Be(950m);
    }

    [Fact]
    public void Zero_product_discount_is_a_real_override()
    {
        var (source, _, price) = ResellerPricing.Calculate(1000m, 20m, 0m);
        source.Should().Be("PRODUCT_RESELLER");
        price.Should().Be(1000m);
    }

    [Theory]
    [InlineData("999.99", "12.5", "874.99")]   // 874.99125
    [InlineData("1234.57", "7.5", "1141.98")]   // 1141.97725 → .98 half-up
    [InlineData("0.01", "50", "0.01")]          // 0.005 → 0.01 half-up
    public void Final_price_is_rounded_half_up_to_paisa(string retail, string pct, string expected)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        ResellerPricing.Calculate(decimal.Parse(retail, inv), decimal.Parse(pct, inv), null).FinalUnitPrice.Should().Be(decimal.Parse(expected, inv));
    }
}
