using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Branch fulfillment (SPEC §19, §22, §27.1; ADR-001 §8; Phase 7 decisions): processing → packed → shipped (courier required,
/// tracking optional) → delivered, fulfillment exceptions, whole-order reroute, no reroute target, administrative cancellation
/// with automatic stock return (damaged/missing excepted), wallet reversal and payment reconciliation.
/// </summary>
[Collection(ApiCollection.Name)]
public class FulfillmentTests(ManokshaApiFactory factory)
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

    private async Task<(HttpClient Reseller, JsonNode Order)> ResellerOrderAsync(Guid sku, int qty, decimal balance = 10_000m)
    {
        var (reseller, _, _) = await factory.FundedResellerAsync(balance, 0m);
        var result = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, qty)).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("CONFIRMED");
        return (reseller, result["order"]!);
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, Guid orderId, string step, object body) => c.PostAsJsonAsync($"/api/v1/admin/orders/{orderId}/{step}", body);

    [Fact]
    public async Task Branch_processes_packs_ships_and_delivers_its_orders()
    {
        var sku = await StockedSkuAsync(500m, (Knr, 5));
        var (reseller, order) = await ResellerOrderAsync(sku, 2);
        var id = order["id"]!.GetValue<Guid>();
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Knr);
        var otherManager = await factory.UserClientAsync(SystemRoles.BranchManager, Hyd);

        (await manager.GetAsync($"/api/v1/admin/fulfillment/queue?branchId={Knr}").OkJsonAsync()).AsArray().Should().Contain(o => o!["id"]!.GetValue<Guid>() == id);
        (await Post(otherManager, id, "processing", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "branches act only on their own orders");
        (await (await Post(manager, id, "packed", new { })).ErrorCodeAsync()).Should().Be("ORDER_STATUS_INVALID");

        await Post(manager, id, "processing", new { note = "picking" }).OkJsonAsync();
        await Post(manager, id, "packed", new { }).OkJsonAsync();
        (await (await Post(manager, id, "shipped", new { courier = "" })).ErrorCodeAsync()).Should().Be("COURIER_INVALID");
        (await (await Post(manager, id, "shipped", new { courier = "OTHER" })).ErrorCodeAsync()).Should().Be("COURIER_NAME_REQUIRED");
        var shipped = await Post(manager, id, "shipped", new { courier = "DELHIVERY" }).OkJsonAsync();
        shipped["status"]!.GetValue<string>().Should().Be("Shipped");
        shipped["shipment"]!["courierLabel"]!.GetValue<string>().Should().Be("Delhivery");
        shipped["shipment"]!["trackingNumber"].Should().BeNull("tracking number is optional");

        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");
        (await (await Post(manager, id, "delivered", new { deliveredOn = future })).ErrorCodeAsync()).Should().Be("DELIVERY_DATE_INVALID");
        var delivered = await Post(manager, id, "delivered", new { note = "Delhivery confirmed" }).OkJsonAsync();
        delivered["status"]!.GetValue<string>().Should().Be("Delivered");
        delivered["history"]!.AsArray().Select(h => h!["toStatus"]!.GetValue<string>()).Should().Equal("Confirmed", "Processing", "Packed", "Shipped", "Delivered");

        var mine = await reseller.GetAsync($"/api/v1/reseller/orders/{id}").OkJsonAsync();
        mine["status"]!.GetValue<string>().Should().Be("Delivered");
        mine["shipment"]!["courierLabel"]!.GetValue<string>().Should().Be("Delhivery");
        mine["history"]!.AsArray().Should().OnlyContain(h => h!["note"] == null, "internal notes are not shown to resellers/customers");
        (await manager.GetAsync($"/api/v1/admin/fulfillment/queue?branchId={Knr}").OkJsonAsync()).AsArray().Should().NotContain(o => o!["id"]!.GetValue<Guid>() == id);
    }

    [Fact]
    public async Task Missing_item_after_confirmation_is_rerouted_whole_to_a_branch_that_can_fulfil_it()
    {
        var sku = await StockedSkuAsync(400m, (Knr, 2), (Hyd, 2));
        var (_, order) = await ResellerOrderAsync(sku, 2, balance: 1000m);
        var id = order["id"]!.GetValue<Guid>();
        order["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Knr);
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Knr);
        var owner = await factory.OwnerClientAsync();

        (await (await Post(manager, id, "fulfillment-exceptions", new { reason = "ITEM_NOT_FOUND", notes = "x" })).ErrorCodeAsync()).Should().Be("REASON_REQUIRED");
        var raised = await Post(manager, id, "fulfillment-exceptions", new
        {
            reason = "ITEM_NOT_FOUND", notes = "One saree not on the shelf", lines = new[] { new { skuId = sku, missingQty = 1, damagedQty = 0 } },
        }).OkJsonAsync();
        raised["status"]!.GetValue<string>().Should().Be("FulfillmentException");
        raised["openException"]!["lines"]![0]!["missingQty"]!.GetValue<int>().Should().Be(1);
        (await owner.GetAsync("/api/v1/admin/fulfillment-exceptions?status=Open").OkJsonAsync()).AsArray().Should().Contain(e => e!["orderId"]!.GetValue<Guid>() == id);

        var options = await owner.GetAsync($"/api/v1/admin/orders/{id}/reroute-options").OkJsonAsync();
        options.AsArray().Single(o => o!["branchId"]!.GetValue<Guid>() == Hyd)!["canFulfil"]!.GetValue<bool>().Should().BeTrue();
        options.AsArray().Single(o => o!["branchId"]!.GetValue<Guid>() == Mlg)!["canFulfil"]!.GetValue<bool>().Should().BeFalse();

        (await Post(manager, id, "reroute", new { targetBranchId = Hyd, reason = "Hyderabad has both" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "reroute is an authorized-role action");
        (await (await Post(owner, id, "reroute", new { targetBranchId = Mlg, reason = "try Mulugu" })).ErrorCodeAsync()).Should().Be("REROUTE_TARGET_CANNOT_FULFIL");
        var rerouted = await Post(owner, id, "reroute", new { targetBranchId = Hyd, reason = "Hyderabad has both" }).OkJsonAsync();
        rerouted["status"]!.GetValue<string>().Should().Be("Processing");
        rerouted["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Hyd);
        rerouted["grandTotal"]!.GetValue<decimal>().Should().Be(order["grandTotal"]!.GetValue<decimal>(), "prices never change");
        rerouted["costOfGoods"]!.GetValue<decimal>().Should().Be(400m, "cost is now the Hyderabad FIFO cost");

        (await factory.StockAsync(Hyd, sku)).Should().Be(0, "the complete order is sold at the new branch");
        (await factory.StockAsync(Knr, sku)).Should().Be(1, "the unit that is physically there returns to AVAILABLE");
        (await factory.LayersAsync(Knr, sku)).Sum(l => l.Remaining).Should().Be(1, "its FIFO cost layer is restored");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM inventory.discrepancies WHERE source_type = 'ORDER' AND source_id = '{id}' AND branch_id = '{Knr}' AND status = 'Open'"))
            .Should().Be(1, "the missing unit opens inventory reconciliation work (SPEC §22)");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM orders.order_reroutes WHERE order_id = '{id}' AND from_branch_id = '{Knr}' AND to_branch_id = '{Hyd}'")).Should().Be(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM audit.audit_log WHERE action = 'orders.order.rerouted' AND entity_id = '{id}'")).Should().Be(1);

        // A second reroute needs a new exception.
        (await (await Post(owner, id, "reroute", new { targetBranchId = Knr, reason = "again" })).ErrorCodeAsync()).Should().Be("REROUTE_NEEDS_EXCEPTION");
    }

    [Fact]
    public async Task No_reroute_target_stays_in_the_exception_queue_until_the_owner_cancels_with_a_wallet_reversal()
    {
        var sku = await StockedSkuAsync(300m, (Knr, 1));
        var (reseller, order) = await ResellerOrderAsync(sku, 1, balance: 1000m);
        var id = order["id"]!.GetValue<Guid>();
        (await reseller.BalanceAsync()).Should().Be(600m);
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Knr);
        var owner = await factory.OwnerClientAsync();
        await Post(manager, id, "processing", new { }).OkJsonAsync();
        await Post(manager, id, "fulfillment-exceptions", new { reason = "DAMAGED", notes = "Torn while packing", lines = new[] { new { skuId = sku, missingQty = 0, damagedQty = 1 } } })
            .OkJsonAsync();

        (await owner.GetAsync($"/api/v1/admin/orders/{id}/reroute-options").OkJsonAsync()).AsArray().Should().OnlyContain(o => !o!["canFulfil"]!.GetValue<bool>());
        (await Post(manager, id, "cancel", new { reason = "Cannot fulfil" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "managers cancel only if the Owner grants it");
        (await (await Post(owner, id, "cancel", new { reason = "" })).ErrorCodeAsync()).Should().Be("REASON_REQUIRED");

        var cancelled = await Post(owner, id, "cancel", new { reason = "No branch can fulfil; reseller informed" }).OkJsonAsync();
        cancelled["order"]!["status"]!.GetValue<string>().Should().Be("Cancelled");
        cancelled["walletRefunded"]!.GetValue<decimal>().Should().Be(400m, "₹300 + ₹100 shipping via a REVERSAL entry");
        (await reseller.BalanceAsync()).Should().Be(1000m);
        (await factory.StockAsync(Knr, sku, "Damaged")).Should().Be(1, "the damaged unit goes to DAMAGED, never back to AVAILABLE (ADR-001 §8)");
        (await factory.StockAsync(Knr, sku)).Should().Be(0);
        (await factory.ScalarAsync<string>($"SELECT status FROM orders.fulfillment_exceptions WHERE order_id = '{id}'")).Should().Be("Cancelled");
        (await (await Post(owner, id, "cancel", new { reason = "again please" })).ErrorCodeAsync()).Should().Be("ORDER_STATUS_INVALID");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM wallet.ledger_entries WHERE order_id = '{id}' AND type = 'Reversal'")).Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_a_paid_online_order_returns_stock_and_opens_payment_reconciliation()
    {
        var sku = await StockedSkuAsync(700m, (Knr, 3));
        var customer = await factory.CustomerClientAsync();
        var (id, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 2));
        (await factory.SimulatorApproveAsync(providerRef)).EnsureSuccessStatusCode();
        (await customer.PaymentStatusAsync(id))["orderStatus"]!.GetValue<string>().Should().Be("Confirmed");
        (await factory.StockAsync(Knr, sku)).Should().Be(1);

        var owner = await factory.OwnerClientAsync();
        var result = await Post(owner, id, "cancel", new { reason = "Customer asked on WhatsApp", lines = new[] { new { skuId = sku, missingQty = 0, damagedQty = 1 } } })
            .OkJsonAsync();
        result["reconciliationCase"]!.GetValue<string>().Should().MatchRegex("^MC-REC-\\d{6}$");
        (await factory.StockAsync(Knr, sku)).Should().Be(2, "one unit returns to AVAILABLE");
        (await factory.StockAsync(Knr, sku, "Damaged")).Should().Be(1);
        (await factory.LayersAsync(Knr, sku)).Sum(l => l.Remaining).Should().Be(3, "cost layers cover all on-hand units again (available + damaged)");
        var rec = (await owner.GetAsync("/api/v1/admin/payment-reconciliations").OkJsonAsync()).AsArray().Single(c => c!["referenceId"]!.GetValue<Guid>() == id)!;
        rec["reasonCode"]!.GetValue<string>().Should().Be("ORDER_CANCELLED_AFTER_PAYMENT");
        rec["status"]!.GetValue<string>().Should().Be("Open", "never marked refunded unless an external refund is recorded");
        (await customer.GetAsync($"/api/v1/customer/orders/{id}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Cancelled");
        (await customer.PostAsJsonAsync($"/api/v1/admin/orders/{id}/cancel", new { reason = "self cancel" })).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "customers never cancel (SPEC §21)");
    }

    [Fact]
    public async Task Shipped_orders_cannot_be_cancelled_and_payment_pending_orders_are_not_in_the_queue()
    {
        var sku = await StockedSkuAsync(200m, (Knr, 3));
        var (_, order) = await ResellerOrderAsync(sku, 1);
        var id = order["id"]!.GetValue<Guid>();
        var owner = await factory.OwnerClientAsync();
        await Post(owner, id, "processing", new { }).OkJsonAsync();
        await Post(owner, id, "packed", new { }).OkJsonAsync();
        await Post(owner, id, "shipped", new { courier = "XPRESSBEES", trackingNumber = "XB123456" }).OkJsonAsync();
        (await (await Post(owner, id, "cancel", new { reason = "too late" })).ErrorCodeAsync()).Should().Be("ORDER_STATUS_INVALID", "cancellation is allowed only until Packed");

        var customer = await factory.CustomerClientAsync();
        var (pendingId, _, _) = await customer.ReservedCheckoutAsync((sku, 1));
        (await owner.GetAsync($"/api/v1/admin/fulfillment/queue?branchId={Knr}").OkJsonAsync()).AsArray()
            .Should().NotContain(o => o!["id"]!.GetValue<Guid>() == pendingId, "unpaid orders are never sent to the branch");
        (await (await Post(owner, pendingId, "processing", new { })).ErrorCodeAsync()).Should().Be("ORDER_STATUS_INVALID");
    }

    [Fact]
    public async Task Serialized_pieces_are_reported_by_barcode_and_resolution_in_place_resumes_processing()
    {
        var sku = await factory.CreateSkuAsync("Serialized");
        var owner = await factory.OwnerClientAsync();
        var productId = await factory.ScalarAsync<Guid>($"SELECT product_id FROM catalog.skus WHERE id = '{sku}'");
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/status", new { status = "Active", reason = "launch" }).OkJsonAsync();
        await factory.SetRetailPriceAsync(sku, 2500m);
        await factory.StockUpAsync(Knr, sku, 2, 1200m);
        var (_, order) = await ResellerOrderAsync(sku, 1);
        var id = order["id"]!.GetValue<Guid>();
        var pieceBarcode = await factory.ScalarAsync<string>(
            $"SELECT b.code FROM catalog.barcodes b JOIN orders.order_lines l ON b.inventory_item_id = ANY(l.item_ids) WHERE l.order_id = '{id}'");
        var otherBarcode = await factory.ScalarAsync<string>(
            $"SELECT b.code FROM catalog.barcodes b JOIN inventory.inventory_items i ON i.id = b.inventory_item_id WHERE i.sku_id = '{sku}' AND i.status = 'Available'");
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Knr);

        (await (await Post(manager, id, "fulfillment-exceptions", new
        {
            reason = "ITEM_NOT_FOUND", notes = "Piece not found", lines = new[] { new { skuId = sku, missingQty = 0, damagedQty = 0, missingBarcodes = new[] { otherBarcode } } },
        })).ErrorCodeAsync()).Should().Be("ORDER_ITEMS_INVALID", "only pieces of this order can be reported");
        await Post(manager, id, "fulfillment-exceptions", new
        {
            reason = "ITEM_NOT_FOUND", notes = "Piece not found", lines = new[] { new { skuId = sku, missingQty = 0, damagedQty = 0, missingBarcodes = new[] { pieceBarcode } } },
        }).OkJsonAsync();

        var resumed = await Post(owner, id, "fulfillment-exceptions/resolve", new { note = "Found in the back room" }).OkJsonAsync();
        resumed["status"]!.GetValue<string>().Should().Be("Processing");
        resumed["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Knr);
        (await factory.ScalarAsync<string>($"SELECT status FROM orders.fulfillment_exceptions WHERE order_id = '{id}'")).Should().Be("ResolvedInPlace");
        (await factory.StockAsync(Knr, sku)).Should().Be(1, "resolving in place changes no stock");
    }

    [Fact]
    public async Task Concurrent_reroute_and_cancel_apply_only_once()
    {
        var sku = await StockedSkuAsync(250m, (Knr, 2), (Hyd, 4), (Mlg, 4));
        var (reseller, order) = await ResellerOrderAsync(sku, 2, balance: 1000m);
        var id = order["id"]!.GetValue<Guid>();
        var owner = await factory.OwnerClientAsync();
        await Post(owner, id, "fulfillment-exceptions", new { reason = "INVENTORY_MISMATCH", notes = "Count looks wrong" }).OkJsonAsync();

        var results = await Task.WhenAll(
            Post(owner, id, "reroute", new { targetBranchId = Hyd, reason = "to Hyderabad" }),
            Post(owner, id, "reroute", new { targetBranchId = Mlg, reason = "to Mulugu" }),
            Post(owner, id, "cancel", new { reason = "cancel it" }));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "the order row lock serializes them; later ones see the new status");

        var stock = await factory.StockAsync(Hyd, sku) + await factory.StockAsync(Mlg, sku) + await factory.StockAsync(Knr, sku);
        var status = (await owner.GetAsync($"/api/v1/admin/orders/{id}").OkJsonAsync())["status"]!.GetValue<string>();
        stock.Should().Be(status == "Cancelled" ? 10 : 8, "stock is sold at exactly one branch (or fully returned when cancelled)");
        (await reseller.BalanceAsync()).Should().Be(status == "Cancelled" ? 1000m : 400m);
    }
}
