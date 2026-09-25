using Manoksha.SharedKernel;

namespace Manoksha.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData("1234.567", "1234.57")] // ADR-001 §11 example
    [InlineData("1234.565", "1234.57")] // half-up, not banker's rounding
    [InlineData("1234.564", "1234.56")]
    [InlineData("0.005", "0.01")]
    [InlineData("100", "100.00")]
    public void Round_uses_half_up_to_paisa(string input, string expected) =>
        Money.Round(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("1000.00", "10", "900.00")]
    [InlineData("999.99", "12.5", "874.99")]  // 874.99125 → 874.99
    [InlineData("1299.00", "7.75", "1198.33")] // 1198.3275 → 1198.33
    [InlineData("500.00", "0", "500.00")]
    [InlineData("500.00", "100", "0.00")]
    public void ApplyPercentDiscount_rounds_final_price_to_paisa(string retail, string pct, string expected)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Money.ApplyPercentDiscount(decimal.Parse(retail, inv), decimal.Parse(pct, inv)).Should().Be(decimal.Parse(expected, inv));
    }

    [Fact]
    public void ApplyPercentDiscount_rejects_out_of_range_percent()
    {
        var act = () => Money.ApplyPercentDiscount(100m, 101m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
