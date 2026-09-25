using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>Supplier → PO → goods receipt → inventory (SPEC §8, ADR-001 §4, §13).</summary>
[Collection(ApiCollection.Name)]
public class PurchasingTests(ManokshaApiFactory factory)
{
    private static readonly Guid Karimnagar = DevelopmentSeedData.BranchKarimnagar;
    private static readonly Guid Hyderabad = DevelopmentSeedData.BranchHyderabad;

    [Fact]
    public async Task Partial_receipts_record_ordered_received_damaged_and_actual_cost()
    {
        var sku = await factory.CreateSkuAsync();
        var po = await factory.CreateIssuedPoAsync(Karimnagar, (sku, 10, 480m));
        var inventory = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Karimnagar);

        var first = await inventory.ReceiveAsync(po, "INV-1001", null, (sku, 6, 1, 500m)).OkJsonAsync();
        first["number"]!.GetValue<string>().Should().StartWith("GRN-");
        first["lines"]![0]!["acceptedQty"]!.GetValue<int>().Should().Be(5);
        first["lines"]![0]!["unitCost"].Should().BeNull("receiving staff without purchasing access do not see costs");

        (await factory.StockAsync(Karimnagar, sku)).Should().Be(5);
        (await factory.StockAsync(Karimnagar, sku, "Damaged")).Should().Be(1);
        (await factory.LayersAsync(Karimnagar, sku)).Should().Equal((500m, 6));

        var owner = await factory.OwnerClientAsync();
        var partial = await owner.GetAsync($"/api/v1/admin/purchasing/purchase-orders/{po["id"]}").OkJsonAsync();
        partial["status"]!.GetValue<string>().Should().Be("PartiallyReceived");
        partial["lines"]![0]!["remainingQty"]!.GetValue<int>().Should().Be(4);

        await inventory.ReceiveAsync(po, "INV-1002", null, (sku, 4, 0, 510m)).OkJsonAsync();
        var done = await owner.GetAsync($"/api/v1/admin/purchasing/purchase-orders/{po["id"]}").OkJsonAsync();
        done["status"]!.GetValue<string>().Should().Be("Received");
        (await factory.LayersAsync(Karimnagar, sku)).Should().Equal((500m, 6), (510m, 4));
    }

    [Fact]
    public async Task Over_receipt_is_blocked_until_the_po_is_amended()
    {
        var sku = await factory.CreateSkuAsync();
        var po = await factory.CreateIssuedPoAsync(Karimnagar, (sku, 5, 100m));
        var owner = await factory.OwnerClientAsync();

        var over = await owner.ReceiveAsync(po, "INV-2001", null, (sku, 6, 0, 100m));
        (await over.ErrorCodeAsync()).Should().Be("OVER_RECEIPT_NOT_ALLOWED");
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(0, "a rejected receipt must not create any stock");

        var amended = await owner.PostAsJsonAsync($"/api/v1/admin/purchasing/purchase-orders/{po["id"]}/lines",
            new { lineId = InventoryHelpers.LineId(po, sku), orderedQty = 6, expectedUnitCost = 100m, reason = "supplier sent one extra, accepted" }).OkJsonAsync();
        await owner.ReceiveAsync(amended, "INV-2001", null, (sku, 6, 0, 100m)).OkJsonAsync();
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(6);
    }

    [Fact]
    public async Task Serialized_receipt_creates_a_piece_with_a_unique_barcode_each()
    {
        var sku = await factory.CreateSkuAsync("Serialized", "Temple Necklace");
        var po = await factory.CreateIssuedPoAsync(Karimnagar, (sku, 3, 2500m));
        var owner = await factory.OwnerClientAsync();
        var grn = await owner.ReceiveAsync(po, "INV-3001", null, (sku, 3, 1, 2500m)).OkJsonAsync();

        var items = grn["items"]!.AsArray();
        items.Should().HaveCount(3);
        items.Select(i => i!["barcode"]!.GetValue<string>()).Should().OnlyHaveUniqueItems().And.OnlyContain(c => c.StartsWith("29"));
        items.Count(i => i!["status"]!.GetValue<string>() == "Damaged").Should().Be(1);

        var pos = await factory.UserClientAsync(SystemRoles.SalesEmployee, Karimnagar, "pos");
        var available = items.First(i => i!["status"]!.GetValue<string>() == "Available")!;
        var scan = await pos.GetAsync($"/api/v1/pos/scan/{available["barcode"]}").OkJsonAsync();
        scan["productName"]!.GetValue<string>().Should().Be("Temple Necklace");
        scan["itemStatus"]!.GetValue<string>().Should().Be("Available");
        scan["itemBranchId"]!.GetValue<Guid>().Should().Be(Karimnagar);
        scan["availability"]!.AsArray().Should().ContainSingle(a => a!["available"]!.GetValue<int>() == 2);
    }

    [Fact]
    public async Task Duplicate_goods_receipt_submission_creates_stock_once()
    {
        var sku = await factory.CreateSkuAsync();
        var po = await factory.CreateIssuedPoAsync(Karimnagar, (sku, 10, 50m));
        var owner = await factory.OwnerClientAsync();
        var key = Guid.NewGuid().ToString("N");

        var first = await owner.ReceiveAsync(po, "INV-4001", key, (sku, 4, 0, 50m)).OkJsonAsync();
        var second = await owner.ReceiveAsync(po, "INV-4001", key, (sku, 4, 0, 50m)).OkJsonAsync();
        second["id"]!.GetValue<Guid>().Should().Be(first["id"]!.GetValue<Guid>());
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(4);
    }

    [Fact]
    public async Task Concurrent_receipts_can_never_exceed_the_ordered_quantity()
    {
        var sku = await factory.CreateSkuAsync();
        var po = await factory.CreateIssuedPoAsync(Karimnagar, (sku, 10, 50m));
        var owner = await factory.OwnerClientAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => owner.ReceiveAsync(po, $"INV-5{i}", null, (sku, 6, 0, 50m))));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(6);
    }

    [Fact]
    public async Task Receiving_is_branch_scoped_and_purchasing_is_owner_controlled()
    {
        var sku = await factory.CreateSkuAsync();
        var po = await factory.CreateIssuedPoAsync(Hyderabad, (sku, 3, 50m));
        var knrInventory = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Karimnagar);

        (await knrInventory.ReceiveAsync(po, "INV-6001", null, (sku, 3, 0, 50m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var visible = await knrInventory.GetAsync("/api/v1/admin/purchasing/purchase-orders").OkJsonAsync();
        visible.AsArray().Should().NotContain(p => p!["id"]!.GetValue<Guid>() == po["id"]!.GetValue<Guid>());
        (await knrInventory.PostAsJsonAsync("/api/v1/admin/purchasing/suppliers", new { name = "x", isActive = true, reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Hyderabad);
        (await manager.PostAsJsonAsync("/api/v1/admin/purchasing/purchase-orders", new { supplierId = Guid.NewGuid(), receivingBranchId = Hyderabad, lines = Array.Empty<object>(), reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "purchasing is Owner-controlled unless delegated (SPEC §8)");
        await manager.ReceiveAsync(po, "INV-6001", null, (sku, 3, 0, 50m)).OkJsonAsync();
    }
}
