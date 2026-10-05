using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Vendor (dropship) selling model, ADR-001 §41–48: vendors and product IDs, online discount for shoppers only, each reseller's own
/// % per vendor, shipping per vendor, one order with a parcel per vendor, out-of-stock switch, Owner margin as cost, store selling off.
/// </summary>
[Collection(ApiCollection.Name)]
public class VendorFlowTests(ManokshaApiFactory factory)
{
    private static string NewCode() => new(Enumerable.Range(0, 4).Select(_ => (char)('A' + Random.Shared.Next(26))).ToArray());

    private async Task<JsonNode> VendorAsync(decimal shippingFee, decimal? margin)
    {
        var owner = await factory.OwnerClientAsync();
        var code = NewCode();
        return await owner.PostAsJsonAsync("/api/v1/admin/catalog/vendors",
            new { code, name = "Vendor " + code, shippingFee, ownerMarginPct = margin, reason = "new vendor" }).OkJsonAsync(HttpStatusCode.Created);
    }

    /// <summary>A single-variant vendor product, active and priced; returns its SKU and product code.</summary>
    private async Task<(Guid Sku, Guid ProductId, string ProductCode)> VendorProductAsync(Guid vendorId, decimal price, decimal? onlineDiscount = null)
    {
        var owner = await factory.OwnerClientAsync();
        var category = await owner.PostAsJsonAsync("/api/v1/admin/catalog/categories", new { name = "C " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1, isActive = true, reason = "x" }).OkJsonAsync();
        var product = await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId = category["id"]!.GetValue<Guid>(), name = "Saree " + Guid.NewGuid().ToString("N")[..6], trackingMode = "Quantity",
            availableForRetail = true, availableForReseller = true, reason = "upload", vendorId,
        }).OkJsonAsync(HttpStatusCode.Created);
        var productId = product["id"]!.GetValue<Guid>();
        var detail = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/variants", new { generateBarcode = false, reason = "x" }).OkJsonAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/status", new { status = "Active", reason = "launch" }).OkJsonAsync();
        var sku = detail["variants"]![0]!["skuId"]!.GetValue<Guid>();
        await factory.SetRetailPriceAsync(sku, price);
        if (onlineDiscount is { } d)
        {
            await owner.PutAsJsonAsync($"/api/v1/admin/pricing/products/{productId}/online-discount", new { discountPct = d, reason = "festival" }).OkJsonAsync();
        }
        return (sku, productId, product["productCode"]!.GetValue<string>());
    }

    private async Task SetResellerVendorDiscountsAsync(Guid resellerId, params (Guid VendorId, decimal Pct)[] discounts)
    {
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{resellerId}/commercial-terms", new
        {
            resellerDiscountPct = 0m, notes = (string?)null, reason = "vendor terms",
            vendorDiscounts = discounts.Select(d => new { vendorId = d.VendorId, discountPct = d.Pct }),
        }).OkJsonAsync();
    }

    private static Task<JsonNode> CartSummaryAsync(HttpClient client, string path, params (Guid Sku, int Qty)[] lines) =>
        client.PostAsJsonAsync(path, new { lines = lines.Select(l => new { skuId = l.Sku, quantity = l.Qty }) }).OkJsonAsync();

    [Fact]
    public async Task Vendor_products_get_sequential_product_ids_and_vendors_are_owner_managed()
    {
        var vendor = await VendorAsync(100m, 17m);
        var vendorId = vendor["id"]!.GetValue<Guid>();
        var code = vendor["code"]!.GetValue<string>();
        var (_, _, first) = await VendorProductAsync(vendorId, 1200m);
        var (sku, _, second) = await VendorProductAsync(vendorId, 800m);
        first.Should().Be($"{code}-000001");
        second.Should().Be($"{code}-000002");

        var shop = factory.CreateClient();
        var byCode = await shop.GetAsync($"/api/v1/catalog/products?q={second}").OkJsonAsync();
        var item = byCode["items"]!.AsArray().Single()!;
        item["skuId"]!.GetValue<Guid>().Should().Be(sku);
        item["vendorName"]!.GetValue<string>().Should().Be("Vendor " + code);
        item["productCode"]!.GetValue<string>().Should().Be(second);
        (await shop.GetAsync($"/api/v1/catalog/products?q=2&vendorId={vendorId}").OkJsonAsync())["items"]!.AsArray()
            .Should().ContainSingle(i => i!["productCode"]!.GetValue<string>() == second, "the number alone finds it too");

        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        (await manager.PostAsJsonAsync("/api/v1/admin/catalog/vendors", new { code = NewCode(), name = "X", shippingFee = 0m, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var seen = (await manager.GetAsync("/api/v1/admin/catalog/vendors").OkJsonAsync()).AsArray().Single(v => v!["id"]!.GetValue<Guid>() == vendorId)!;
        seen["ownerMarginPct"].Should().BeNull("the Owner's margin is Owner-only");
    }

    [Fact]
    public async Task Online_shoppers_pay_the_online_price_plus_each_vendors_shipping_and_payment_confirms_without_stock()
    {
        var a = await VendorAsync(100m, 17m);
        var b = await VendorAsync(150m, 20m);
        var (skuA, _, _) = await VendorProductAsync(a["id"]!.GetValue<Guid>(), 1000m, onlineDiscount: 15m);
        var (skuB, _, _) = await VendorProductAsync(b["id"]!.GetValue<Guid>(), 500m);

        var summary = await CartSummaryAsync(factory.CreateClient(), "/api/v1/catalog/cart-summary", (skuA, 2), (skuB, 1));
        summary["merchandise"]!.GetValue<decimal>().Should().Be(2 * 850m + 500m);
        summary["shipping"]!.GetValue<decimal>().Should().Be(250m);
        summary["groups"]!.AsArray().Should().HaveCount(2);

        var customer = await factory.CustomerClientAsync();
        var result = await customer.OnlineCheckoutAsync(null, (skuA, 2), (skuB, 1)).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("PAYMENT_PENDING");
        var orderId = result["order"]!["id"]!.GetValue<Guid>();
        result["order"]!["grandTotal"]!.GetValue<decimal>().Should().Be(2450m);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM orders.reservations WHERE order_id = '{orderId}'")).Should().Be(0, "vendor products hold no stock");

        await factory.SimulatorApproveAsync(StorefrontHelpers.ProviderRef(result)).OkJsonAsync();
        var order = await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync();
        order["status"]!.GetValue<string>().Should().Be("Confirmed");
        order["fulfillmentMode"]!.GetValue<string>().Should().Be("Vendor");
        var parcels = order["parcels"]!.AsArray();
        parcels.Should().HaveCount(2);
        parcels.Sum(p => p!["shippingFee"]!.GetValue<decimal>()).Should().Be(250m);
        order["lines"]!.AsArray().Single(l => l!["skuId"]!.GetValue<Guid>() == skuA)!["discountSource"]!.GetValue<string>().Should().Be("ONLINE_PRODUCT");

        // Cost = retail × (1 − Owner margin): 2 × 830 + 400; the Owner sees it, the shopper never does.
        var adminView = await (await factory.OwnerClientAsync()).GetAsync($"/api/v1/admin/orders/{orderId}").OkJsonAsync();
        adminView["costOfGoods"]!.GetValue<decimal>().Should().Be(2 * 830m + 400m);
        order["costOfGoods"].Should().BeNull();
    }

    [Fact]
    public async Task Each_reseller_gets_exactly_their_own_percentage_per_vendor_and_zero_when_none_is_set()
    {
        var a = await VendorAsync(100m, 17m);
        var b = await VendorAsync(100m, null);
        var aId = a["id"]!.GetValue<Guid>();
        var (skuA, _, _) = await VendorProductAsync(aId, 1000m, onlineDiscount: 15m);
        var (skuB, _, _) = await VendorProductAsync(b["id"]!.GetValue<Guid>(), 500m);

        var (first, firstId, _) = await factory.FundedResellerAsync(5000m);
        var (second, secondId, _) = await factory.FundedResellerAsync(5000m);
        await SetResellerVendorDiscountsAsync(firstId, (aId, 10m));
        await SetResellerVendorDiscountsAsync(secondId, (aId, 12m));

        var mine = await CartSummaryAsync(first, "/api/v1/reseller/cart-summary", (skuA, 1), (skuB, 1));
        mine["lines"]!.AsArray().Single(l => l!["skuId"]!.GetValue<Guid>() == skuA)!["unitPrice"]!.GetValue<decimal>().Should().Be(900m, "their own 10%, not the 15% online discount");
        mine["lines"]!.AsArray().Single(l => l!["skuId"]!.GetValue<Guid>() == skuB)!["unitPrice"]!.GetValue<decimal>().Should().Be(500m, "no % set for this vendor");
        (await CartSummaryAsync(second, "/api/v1/reseller/cart-summary", (skuA, 1)))["lines"]![0]!["unitPrice"]!.GetValue<decimal>().Should().Be(880m);

        var placed = await first.CheckoutAsync(null, CheckoutHelpers.Delivery(), (skuA, 1), (skuB, 1)).OkJsonAsync();
        placed["outcome"]!.GetValue<string>().Should().Be("CONFIRMED");
        placed["order"]!["grandTotal"]!.GetValue<decimal>().Should().Be(900m + 500m + 200m);
        placed["walletBalance"]!.GetValue<decimal>().Should().Be(5000m - 1600m);
        placed["order"]!["parcels"]!.AsArray().Should().HaveCount(2);
        var lineA = placed["order"]!["lines"]!.AsArray().Single(l => l!["skuId"]!.GetValue<Guid>() == skuA)!;
        lineA["discountSource"]!.GetValue<string>().Should().Be("RESELLER_VENDOR");
        lineA["commercialTermVersion"]!.GetValue<int>().Should().Be(2, "the order keeps the terms version it was priced with");

        var terms = await first.GetAsync("/api/v1/reseller/me").OkJsonAsync();
        terms["vendorDiscounts"]!.AsArray().Should().ContainSingle(v => v!["vendorId"]!.GetValue<Guid>() == aId && v["discountPct"]!.GetValue<decimal>() == 10m);
    }

    [Fact]
    public async Task Parcels_ship_separately_the_order_follows_and_buyers_get_each_tracking_number()
    {
        var a = await VendorAsync(100m, 10m);
        var b = await VendorAsync(100m, 10m);
        var (skuA, _, _) = await VendorProductAsync(a["id"]!.GetValue<Guid>(), 700m);
        var (skuB, _, _) = await VendorProductAsync(b["id"]!.GetValue<Guid>(), 300m);
        var (reseller, _, mobile) = await factory.FundedResellerAsync(3000m);
        var order = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (skuA, 1), (skuB, 1)).OkJsonAsync())["order"]!;
        var id = order["id"]!.GetValue<Guid>();
        var parcels = order["parcels"]!.AsArray().Select(p => p!["id"]!.GetValue<Guid>()).ToList();
        var owner = await factory.OwnerClientAsync();
        var path = $"/api/v1/admin/orders/{id}/parcels";

        (await (await owner.PostAsJsonAsync($"/api/v1/admin/orders/{id}/processing", new { note = (string?)null })).ErrorCodeAsync()).Should().Be("VENDOR_ORDER");
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        (await manager.PostAsJsonAsync($"{path}/{parcels[0]}/ordered", new { vendorReference = "Z-1" })).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        (await owner.PostAsJsonAsync($"{path}/{parcels[0]}/ordered", new { vendorReference = "INV-77", note = (string?)null }).OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Processing");
        (await owner.PostAsJsonAsync($"{path}/{parcels[0]}/shipped", new { courier = "DELHIVERY", trackingNumber = "DL123", note = (string?)null }).OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Processing");
        (await owner.PostAsJsonAsync($"{path}/{parcels[1]}/shipped", new { courier = "OTHER", courierName = "Blue Dart", trackingNumber = (string?)null, note = (string?)null }).OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Shipped");
        (await (await owner.PostAsJsonAsync($"{path}/{parcels[0]}/shipped", new { courier = "DELHIVERY" })).ErrorCodeAsync()).Should().Be("PARCEL_STATUS_INVALID");
        await owner.PostAsJsonAsync($"{path}/{parcels[0]}/delivered", new { deliveredOn = (DateOnly?)null, note = (string?)null }).OkJsonAsync();
        (await owner.PostAsJsonAsync($"{path}/{parcels[1]}/delivered", new { deliveredOn = (DateOnly?)null, note = (string?)null }).OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Delivered");

        var resellerView = await reseller.GetAsync($"/api/v1/reseller/orders/{id}").OkJsonAsync();
        resellerView["parcels"]!.AsArray().Should().OnlyContain(p => p!["vendorReference"] == null, "internal references are hidden from buyers");
        resellerView["parcels"]!.AsArray().Should().Contain(p => p!["trackingNumber"]!.GetValue<string>() == "DL123");

        await factory.DispatchOutboxAsync();
        await factory.SendEmailsAsync();
        var email = $"r{mobile[^6..]}@example.com";
        factory.Emails.To(email).Count(m => m.Subject.Contains("have shipped", StringComparison.Ordinal)).Should().Be(2, "one email per parcel");
        factory.Emails.To(email).Should().Contain(m => m.Subject == $"Order {order["number"]!.GetValue<string>()} delivered");
    }

    [Fact]
    public async Task Out_of_stock_items_cannot_be_bought_and_cancelling_before_shipping_refunds_the_wallet()
    {
        var a = await VendorAsync(100m, null);
        var (sku, productId, _) = await VendorProductAsync(a["id"]!.GetValue<Guid>(), 600m);
        var (sku2, _, _) = await VendorProductAsync(a["id"]!.GetValue<Guid>(), 400m);
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/out-of-stock", new { outOfStock = true, reason = "vendor sold out" }).OkJsonAsync();

        var (reseller, _, _) = await factory.FundedResellerAsync(2000m);
        (await (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1))).ErrorCodeAsync()).Should().Be("SKU_OUT_OF_STOCK");
        (await factory.CreateClient().GetAsync($"/api/v1/catalog/products/{productId}").OkJsonAsync())["variants"]![0]!["inStock"]!.GetValue<bool>().Should().BeFalse();

        var order = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku2, 2)).OkJsonAsync())["order"]!;
        (await reseller.BalanceAsync()).Should().Be(2000m - 900m);
        var cancelled = await owner.PostAsJsonAsync($"/api/v1/admin/orders/{order["id"]}/cancel", new { reason = "vendor cannot supply", lines = (object?)null }).OkJsonAsync();
        cancelled["walletRefunded"]!.GetValue<decimal>().Should().Be(900m);
        cancelled["order"]!["parcels"]!.AsArray().Should().OnlyContain(p => p!["status"]!.GetValue<string>() == "Cancelled");
        (await reseller.BalanceAsync()).Should().Be(2000m);

        var shipped = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku2, 1)).OkJsonAsync())["order"]!;
        await owner.PostAsJsonAsync($"/api/v1/admin/orders/{shipped["id"]}/parcels/{shipped["parcels"]![0]!["id"]}/shipped", new { courier = "XPRESSBEES" }).OkJsonAsync();
        (await (await owner.PostAsJsonAsync($"/api/v1/admin/orders/{shipped["id"]}/cancel", new { reason = "too late" })).ErrorCodeAsync()).Should().Be("ORDER_STATUS_INVALID");
    }

    [Fact]
    public async Task Vendor_and_branch_products_cannot_share_a_cart()
    {
        var a = await VendorAsync(100m, null);
        var (vendorSku, _, _) = await VendorProductAsync(a["id"]!.GetValue<Guid>(), 300m);
        var (branchSku, _) = await factory.CreatePricedSkuAsync(300m);
        await factory.StockUpAsync(DevelopmentSeedData.BranchKarimnagar, branchSku, 2, 100m);
        var (reseller, _, _) = await factory.FundedResellerAsync(2000m);
        (await (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (vendorSku, 1), (branchSku, 1))).ErrorCodeAsync()).Should().Be("CART_MIXED");
    }

    [Fact]
    public async Task With_store_selling_off_the_pos_is_closed_and_new_products_need_a_vendor()
    {
        var pos = await factory.UserClientAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar, "pos");
        await factory.SetSettingAsync("operations.store_selling_enabled", "false");
        try
        {
            var context = await pos.GetAsync("/api/v1/pos/context");
            context.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await context.ErrorCodeAsync()).Should().Be("STORE_SELLING_PAUSED");
            var owner = await factory.OwnerClientAsync();
            (await owner.GetAsync("/api/v1/auth/me").OkJsonAsync())["storeSellingEnabled"]!.GetValue<bool>().Should().BeFalse();
            var category = await owner.PostAsJsonAsync("/api/v1/admin/catalog/categories", new { name = "C " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1, isActive = true, reason = "x" }).OkJsonAsync();
            var noVendor = await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
            {
                categoryId = category["id"]!.GetValue<Guid>(), name = "No vendor", trackingMode = "Quantity", availableForRetail = true, availableForReseller = true, reason = "x",
            });
            (await noVendor.ErrorCodeAsync()).Should().Be("VENDOR_REQUIRED");
        }
        finally
        {
            await factory.SetSettingAsync("operations.store_selling_enabled", "true");
        }
    }
}
