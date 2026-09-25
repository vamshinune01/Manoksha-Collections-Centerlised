using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Retail prices and reseller pricing (SPEC §15; ADR-001 §6, §11).</summary>
[Collection(ApiCollection.Name)]
public class PricingTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Product_reseller_discount_overrides_reseller_discount_without_stacking()
    {
        var (sku, productId) = await factory.CreatePricedSkuAsync(1000m);
        var reseller = await factory.NewActiveResellerAsync(10m);
        var owner = await factory.OwnerClientAsync();

        var normal = await reseller.QuoteAsync(sku);
        normal["discountSource"]!.GetValue<string>().Should().Be("RESELLER");
        normal["finalUnitPrice"]!.GetValue<decimal>().Should().Be(900m);
        normal["commercialTermVersion"]!.GetValue<int>().Should().Be(1);

        await owner.PutAsJsonAsync($"/api/v1/admin/pricing/products/{productId}/reseller-discount", new { discountPct = 15m, reason = "clearance" }).OkJsonAsync();
        var overridden = await reseller.QuoteAsync(sku);
        overridden["discountSource"]!.GetValue<string>().Should().Be("PRODUCT_RESELLER");
        overridden["discountPct"]!.GetValue<decimal>().Should().Be(15m);
        overridden["finalUnitPrice"]!.GetValue<decimal>().Should().Be(850m, "never 765 (stacked)");

        await owner.PostAsJsonAsync($"/api/v1/admin/pricing/products/{productId}/reseller-discount/clear", new { reason = "clearance over" }).OkJsonAsync();
        (await reseller.QuoteAsync(sku))["finalUnitPrice"]!.GetValue<decimal>().Should().Be(900m);

        var catalog = await reseller.GetAsync("/api/v1/reseller/catalog?pageSize=100").OkJsonAsync();
        catalog["items"]!.AsArray().Should().Contain(i => i!["skuId"]!.GetValue<Guid>() == sku && i["resellerPrice"]!.GetValue<decimal>() == 900m);
    }

    [Fact]
    public async Task Prices_are_rounded_half_up_to_paisa_and_term_changes_apply_to_future_quotes()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(999.99m);
        var mobile = ApiClient.NewMobile();
        var id = (await factory.CreateResellerAsync(mobile, 10m))["id"]!.GetValue<Guid>();
        var reseller = await factory.ResellerClientAsync(mobile);
        var before = await reseller.QuoteAsync(sku);
        before["finalUnitPrice"]!.GetValue<decimal>().Should().Be(899.99m);

        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{id}/commercial-terms", new { resellerDiscountPct = 12.5m, reason = "upgrade" }).OkJsonAsync();
        var after = await reseller.QuoteAsync(sku);
        after["finalUnitPrice"]!.GetValue<decimal>().Should().Be(874.99m);
        after["commercialTermVersion"]!.GetValue<int>().Should().Be(2);
        after["commercialTermId"]!.GetValue<Guid>().Should().NotBe(before["commercialTermId"]!.GetValue<Guid>());
    }

    [Fact]
    public async Task Products_not_for_resellers_and_unpriced_skus_are_excluded_and_rejected()
    {
        var (blocked, _) = await factory.CreatePricedSkuAsync(500m, forResellers: false);
        var (unpriced, _) = await factory.CreatePricedSkuAsync(0m);
        var reseller = await factory.NewActiveResellerAsync();

        var catalog = await reseller.GetAsync("/api/v1/reseller/catalog?pageSize=100").OkJsonAsync();
        catalog["items"]!.AsArray().Should().NotContain(i => i!["skuId"]!.GetValue<Guid>() == blocked || i["skuId"]!.GetValue<Guid>() == unpriced);

        var direct = await reseller.PostAsJsonAsync("/api/v1/reseller/price-quote", new { skuIds = new[] { blocked } });
        (await direct.ErrorCodeAsync()).Should().Be("SKU_NOT_AVAILABLE_FOR_RESELLER");
        var noPrice = await reseller.PostAsJsonAsync("/api/v1/reseller/price-quote", new { skuIds = new[] { unpriced } });
        (await noPrice.ErrorCodeAsync()).Should().Be("SKU_NOT_PRICED");
    }

    [Fact]
    public async Task Retail_prices_are_owner_controlled_versioned_and_audited()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(700m);
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        (await manager.PutAsJsonAsync($"/api/v1/admin/pricing/skus/{sku}/retail-price", new { price = 1m, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync($"/api/v1/admin/pricing/skus/{sku}/history")).StatusCode.Should().Be(HttpStatusCode.OK, "managers may view prices");

        await factory.SetRetailPriceAsync(sku, 750m);
        var owner = await factory.OwnerClientAsync();
        var history = await owner.GetAsync($"/api/v1/admin/pricing/skus/{sku}/history").OkJsonAsync();
        history.AsArray().Select(h => h!["price"]!.GetValue<decimal>()).Should().Equal(750m, 700m);
        history[1]!["effectiveTo"].Should().NotBeNull();
        history[0]!["effectiveTo"].Should().BeNull();

        var audit = await owner.GetAsync($"/api/v1/admin/audit?action=pricing.retail_price.changed&entityId={sku}").OkJsonAsync();
        audit["items"]!.AsArray().Should().HaveCount(2);

        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var rewrite = new NpgsqlCommand($"UPDATE pricing.retail_prices SET price = 1 WHERE sku_id = '{sku}'", c);
        (await FluentActions.Awaiting(() => rewrite.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Pos_scan_shows_the_retail_price()
    {
        var (sku, productId) = await factory.CreatePricedSkuAsync(1299m);
        var owner = await factory.OwnerClientAsync();
        var product = await owner.GetAsync($"/api/v1/admin/catalog/products/{productId}").OkJsonAsync();
        var code = product["variants"]![0]!["barcodes"]![0]!["code"]!.GetValue<string>();
        var pos = await factory.UserClientAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar, "pos");
        var scan = await pos.GetAsync($"/api/v1/pos/scan/{code}").OkJsonAsync();
        scan["retailPrice"]!.GetValue<decimal>().Should().Be(1299m);
        scan["skuId"]!.GetValue<Guid>().Should().Be(sku);
    }
}
