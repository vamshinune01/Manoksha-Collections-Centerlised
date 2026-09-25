using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Reseller checkout: wallet debit + stock commit + order creation in one transaction, Owner branch priority, no split orders,
/// idempotency and concurrency (SPEC §11, §12, §16, §19.2, §32, §33).
/// </summary>
[Collection(ApiCollection.Name)]
public class ResellerCheckoutTests(ManokshaApiFactory factory)
{
    private static readonly Guid Knr = DevelopmentSeedData.BranchKarimnagar;
    private static readonly Guid Hyd = DevelopmentSeedData.BranchHyderabad;
    private static readonly Guid Mlg = DevelopmentSeedData.BranchMulugu;

    private async Task<Guid> StockedSkuAsync(decimal price, params (Guid Branch, int Qty)[] stock)
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(price);
        foreach (var (branch, qty) in stock)
        {
            await factory.StockUpAsync(branch, sku, qty, Math.Round(price / 2, 2));
        }
        return sku;
    }

    [Fact]
    public async Task Checkout_debits_wallet_commits_stock_and_snapshots_prices()
    {
        var sku = await StockedSkuAsync(1000m, (Knr, 5));
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(5000m, 10m);

        var result = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 2)).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("CONFIRMED");
        var order = result["order"]!;
        order["status"]!.GetValue<string>().Should().Be("Confirmed");
        order["number"]!.GetValue<string>().Should().MatchRegex("^MC-ORD-\\d{6}$");
        order["merchandiseTotal"]!.GetValue<decimal>().Should().Be(1800m);
        order["shippingFee"]!.GetValue<decimal>().Should().Be(100m, "₹100 per separate order");
        order["grandTotal"]!.GetValue<decimal>().Should().Be(1900m);
        order["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Knr);
        var line = order["lines"]![0]!;
        line["retailUnitPrice"]!.GetValue<decimal>().Should().Be(1000m);
        line["discountSource"]!.GetValue<string>().Should().Be("RESELLER");
        line["finalUnitPrice"]!.GetValue<decimal>().Should().Be(900m);
        order["helpWhatsAppUrl"]!.GetValue<string>().Should().StartWith("https://wa.me/919741404304");
        result["walletBalance"]!.GetValue<decimal>().Should().Be(3100m);
        (await factory.StockAsync(Knr, sku)).Should().Be(3);

        // Later price and discount changes never touch the historical order.
        await factory.SetRetailPriceAsync(sku, 1500m);
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{resellerId}/commercial-terms", new { resellerDiscountPct = 30m, reason = "x" }).OkJsonAsync();
        var again = await reseller.GetAsync($"/api/v1/reseller/orders/{order["id"]}").OkJsonAsync();
        again["lines"]![0]!["finalUnitPrice"]!.GetValue<decimal>().Should().Be(900m);
        again["grandTotal"]!.GetValue<decimal>().Should().Be(1900m);

        var adminView = await owner.GetAsync($"/api/v1/admin/orders/{order["id"]}").OkJsonAsync();
        adminView["costOfGoods"]!.GetValue<decimal>().Should().Be(1000m, "2 units × ₹500 FIFO cost");
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var tamper = new NpgsqlCommand($"UPDATE orders.order_lines SET final_unit_price = 1 WHERE order_id = '{order["id"]}'", c);
        (await FluentActions.Awaiting(() => tamper.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Insufficient_wallet_rejects_checkout_without_any_effect()
    {
        var sku = await StockedSkuAsync(1000m, (Knr, 5));
        var (reseller, _, _) = await factory.FundedResellerAsync(999m, 0m);
        var response = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be("INSUFFICIENT_WALLET_BALANCE", "₹1000 + ₹100 shipping exceeds ₹999");
        (await reseller.BalanceAsync()).Should().Be(999m);
        (await factory.StockAsync(Knr, sku)).Should().Be(5);
        (await reseller.GetAsync("/api/v1/reseller/orders").OkJsonAsync()).AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_place_order_click_creates_one_order_and_one_debit()
    {
        var sku = await StockedSkuAsync(500m, (Knr, 5));
        var (reseller, _, _) = await factory.FundedResellerAsync(2000m, 0m);
        var key = Guid.NewGuid().ToString("N");
        var delivery = CheckoutHelpers.Delivery();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => reseller.CheckoutAsync(key, delivery, (sku, 1))));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var ids = new List<Guid>();
        foreach (var r in results)
        {
            ids.Add((await r.ReadJsonAsync())["order"]!["id"]!.GetValue<Guid>());
        }
        ids.Distinct().Should().ContainSingle();
        (await reseller.BalanceAsync()).Should().Be(1400m);
        (await factory.StockAsync(Knr, sku)).Should().Be(4);
    }

    [Fact]
    public async Task Two_orders_racing_for_the_same_wallet_balance_cannot_overdraw()
    {
        var sku = await StockedSkuAsync(900m, (Knr, 10));
        var (reseller, _, _) = await factory.FundedResellerAsync(1500m, 0m); // enough for exactly one ₹1000 order

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1))));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).Should().Be(2);
        (await reseller.BalanceAsync()).Should().Be(500m);
        (await factory.StockAsync(Knr, sku)).Should().Be(9, "failed checkouts released nothing because they committed nothing");
    }

    [Fact]
    public async Task Two_resellers_cannot_both_buy_the_last_piece()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(2000m);
        await factory.StockUpAsync(Knr, sku, 1, 900m);
        var a = await factory.FundedResellerAsync(10000m);
        var b = await factory.FundedResellerAsync(10000m);

        var results = await Task.WhenAll(
            a.Client.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)),
            b.Client.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)));
        var outcomes = new List<string>();
        foreach (var r in results)
        {
            outcomes.Add((await r.ReadJsonAsync())["outcome"]!.GetValue<string>());
        }
        outcomes.Should().BeEquivalentTo(["CONFIRMED", "UNFULFILLABLE"]);
        (await factory.StockAsync(Knr, sku)).Should().Be(0);
    }

    [Fact]
    public async Task Priority_one_unavailable_routes_to_priority_two()
    {
        var sku = await StockedSkuAsync(100m, (Hyd, 3));
        var (reseller, _, _) = await factory.FundedResellerAsync(1000m);
        var result = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 2)).OkJsonAsync();
        result["order"]!["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Hyd);
    }

    [Fact]
    public async Task Priority_one_and_two_unavailable_routes_to_priority_three()
    {
        var sku = await StockedSkuAsync(100m, (Knr, 1), (Hyd, 1), (Mlg, 3));
        var (reseller, _, _) = await factory.FundedResellerAsync(1000m);
        var result = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 2)).OkJsonAsync();
        result["order"]!["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Mlg);
        (await factory.StockAsync(Knr, sku)).Should().Be(1, "partial stock at higher-priority branches is untouched");
        (await factory.StockAsync(Hyd, sku)).Should().Be(1);
    }

    [Fact]
    public async Task Order_is_never_split_and_no_branch_means_inquiry_without_debit()
    {
        var skuA = await StockedSkuAsync(100m, (Knr, 2));
        var skuB = await StockedSkuAsync(100m, (Hyd, 2));
        var (reseller, _, _) = await factory.FundedResellerAsync(1000m);

        var result = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (skuA, 1), (skuB, 1)).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("UNFULFILLABLE");
        result["order"].Should().BeNull();
        var inquiry = result["inquiry"]!;
        var reference = inquiry["reference"]!.GetValue<string>();
        reference.Should().MatchRegex("^MC-FUL-\\d{8}$");
        inquiry["message"]!.GetValue<string>().Should().Contain(reference).And.Contain("9741404304");
        inquiry["whatsAppUrl"]!.GetValue<string>().Should().StartWith("https://wa.me/919741404304");

        (await reseller.BalanceAsync()).Should().Be(1000m);
        (await factory.StockAsync(Knr, skuA)).Should().Be(2);
        (await factory.StockAsync(Hyd, skuB)).Should().Be(2);

        var owner = await factory.OwnerClientAsync();
        var inquiries = await owner.GetAsync("/api/v1/admin/fulfillment-inquiries").OkJsonAsync();
        var stored = inquiries.AsArray().Single(i => i!["reference"]!.GetValue<string>() == reference)!;
        stored["evaluations"]!.GetValue<string>().Should().Contain("INSUFFICIENT_STOCK").And.Contain("KNR").And.Contain("HYD");
    }

    [Fact]
    public async Task Products_not_for_resellers_are_rejected_at_checkout()
    {
        var (blocked, _) = await factory.CreatePricedSkuAsync(100m, forResellers: false);
        await factory.StockUpAsync(Knr, blocked, 2, 10m);
        var (reseller, _, _) = await factory.FundedResellerAsync(1000m);
        (await (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (blocked, 1))).ErrorCodeAsync()).Should().Be("SKU_NOT_AVAILABLE_FOR_RESELLER");
    }

    [Fact]
    public async Task Frozen_reseller_cannot_place_orders()
    {
        var sku = await StockedSkuAsync(100m, (Knr, 2));
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(1000m);
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{resellerId}/status", new { status = "Frozen", reason = "review" }).OkJsonAsync();
        (await (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1))).ErrorCodeAsync()).Should().Be("RESELLER_CANNOT_ORDER");
        (await reseller.GetAsync("/api/v1/reseller/orders")).StatusCode.Should().Be(HttpStatusCode.OK, "read-only access remains");
    }

    [Fact]
    public async Task Delivery_details_are_required_and_customers_are_private_to_each_reseller()
    {
        var sku = await StockedSkuAsync(100m, (Knr, 5));
        var a = await factory.FundedResellerAsync(1000m);
        var b = await factory.FundedResellerAsync(1000m);

        var missing = await a.Client.CheckoutAsync(null, new { name = "X" }, (sku, 1));
        (await missing.ErrorCodeAsync()).Should().Be("DELIVERY_DETAILS_REQUIRED");

        var endCustomerMobile = ApiClient.NewMobile();
        var order = (await a.Client.CheckoutAsync(null, CheckoutHelpers.Delivery(endCustomerMobile), (sku, 1)).OkJsonAsync())["order"]!;
        var aCustomers = await a.Client.GetAsync("/api/v1/reseller/customers").OkJsonAsync();
        var saved = aCustomers.AsArray().Single(c => c!["details"]!["mobile"]!.GetValue<string>().EndsWith(endCustomerMobile[^10..], StringComparison.Ordinal))!;

        // Reseller B sees none of A's customers or orders, even by id.
        (await b.Client.GetAsync("/api/v1/reseller/customers").OkJsonAsync()).AsArray().Should().BeEmpty();
        (await b.Client.GetAsync($"/api/v1/reseller/orders/{order["id"]}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var useOthers = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reseller/checkout")
        {
            Content = JsonContent.Create(new { lines = new[] { new { skuId = sku, quantity = 1 } }, resellerCustomerId = saved["id"]!.GetValue<Guid>() }),
        };
        useOthers.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await b.Client.SendAsync(useOthers)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deposit = await a.Client.SubmitDepositAsync(100m, CheckoutHelpers.Utr());
        (await b.Client.GetAsync($"/api/v1/reseller/wallet/deposits/{deposit["id"]}/proof")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
