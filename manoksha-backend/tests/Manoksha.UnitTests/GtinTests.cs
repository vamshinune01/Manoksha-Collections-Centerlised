using Manoksha.Modules.Catalog.Domain;

namespace Manoksha.UnitTests;

public class GtinTests
{
    [Theory]
    [InlineData("4006381333931")] // well-known valid EAN-13
    [InlineData("036000291452")]  // valid UPC-A
    [InlineData("96385074")]      // valid EAN-8
    public void Recognises_valid_gtins(string code) => Gtin.IsValidGtin(code).Should().BeTrue();

    [Theory]
    [InlineData("4006381333932")]
    [InlineData("40063813339")]
    [InlineData("ABC")]
    public void Rejects_invalid_gtins(string code) => Gtin.IsValidGtin(code).Should().BeFalse();

    [Fact]
    public void Internal_codes_are_valid_ean13_in_the_in_store_range()
    {
        var code = Gtin.CreateInternalEan13(123);
        code.Should().Be("2900000001237").And.HaveLength(13);
        Gtin.IsValidGtin(code).Should().BeTrue();
    }

    [Fact]
    public void Slugs_are_url_safe_and_unique_per_id()
    {
        var id = Guid.NewGuid();
        var slug = Slugs.Create("Kanchi Silk Saree — Red/Gold!", id);
        slug.Should().MatchRegex("^[a-z0-9-]+$").And.StartWith("kanchi-silk-saree-red-gold-").And.EndWith(id.ToString("N")[^6..]);
    }
}
