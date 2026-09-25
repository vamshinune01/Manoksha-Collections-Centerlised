using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>Branch transfers with carried FIFO cost, separation of duties and the discrepancy workflow (SPEC §10, ADR-001 §3, §7).</summary>
[Collection(ApiCollection.Name)]
public class TransferTests(ManokshaApiFactory factory)
{
    private static readonly Guid Karimnagar = DevelopmentSeedData.BranchKarimnagar;
    private static readonly Guid Hyderabad = DevelopmentSeedData.BranchHyderabad;
    private const string Base = "/api/v1/admin/inventory/transfers";

    private static Guid Line(JsonNode transfer) => transfer["lines"]![0]!["id"]!.GetValue<Guid>();

    private static async Task<JsonNode> RequestAsync(HttpClient client, Guid sku, int qty) =>
        await client.PostAsJsonAsync(Base, new { sourceBranchId = Karimnagar, destinationBranchId = Hyderabad, lines = new[] { new { skuId = sku, quantity = qty } }, reason = "Hyderabad running low" }).OkJsonAsync();

    [Fact]
    public async Task Full_transfer_moves_stock_and_carries_fifo_cost_basis()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 10, 500m);
        await factory.StockUpAsync(Karimnagar, sku, 5, 550m);

        var requester = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Karimnagar);
        var sourceManager = await factory.UserClientAsync(SystemRoles.BranchManager, Karimnagar);
        var destination = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Hyderabad);

        var t = await RequestAsync(requester, sku, 12);
        t = await sourceManager.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "ok" }).OkJsonAsync();
        t["ownerSelfAuthorized"]!.GetValue<bool>().Should().BeFalse();
        t = await requester.PostAsJsonAsync($"{Base}/{t["id"]}/prepare", new { lines = new[] { new { lineId = Line(t), quantity = 12 } } }).OkJsonAsync();
        (await factory.StockAsync(Karimnagar, sku, "TransferPending")).Should().Be(12);
        t = await requester.PostAsync($"{Base}/{t["id"]}/dispatch", null).OkJsonAsync();
        t["status"]!.GetValue<string>().Should().Be("InTransit");
        (await factory.StockAsync(Karimnagar, sku, "InTransit")).Should().Be(12);

        t = await destination.PostAsJsonAsync($"{Base}/{t["id"]}/receive", new { lines = new[] { new { lineId = Line(t), quantity = 12 } } }).OkJsonAsync();
        t["status"]!.GetValue<string>().Should().Be("Received");
        (await factory.StockAsync(Hyderabad, sku)).Should().Be(12);
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(3);

        // FIFO per SKU per branch: the oldest source layers travel with the stock at their original cost.
        (await factory.LayersAsync(Karimnagar, sku)).Should().Equal((550m, 3));
        (await factory.LayersAsync(Hyderabad, sku)).Should().Equal((500m, 10), (550m, 2));
    }

    [Fact]
    public async Task Requester_cannot_approve_own_transfer_but_owner_self_authorization_is_recorded()
    {
        var sku = await factory.CreateSkuAsync();
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Karimnagar);
        var t = await RequestAsync(manager, sku, 1);
        var self = await manager.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "me" });
        (await self.ErrorCodeAsync()).Should().Be("SELF_APPROVAL_NOT_ALLOWED");

        // The destination manager has no authority to release the source's stock.
        var hydManager = await factory.UserClientAsync(SystemRoles.BranchManager, Hyderabad);
        (await hydManager.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var owner = await factory.OwnerClientAsync();
        var ownerTransfer = await RequestAsync(owner, sku, 1);
        var approved = await owner.PostAsJsonAsync($"{Base}/{ownerTransfer["id"]}/approve", new { note = "urgent" }).OkJsonAsync();
        approved["ownerSelfAuthorized"]!.GetValue<bool>().Should().BeTrue();
        approved["approvedBy"]!.GetValue<Guid>().Should().Be(approved["requestedBy"]!.GetValue<Guid>());

        var audit = await owner.GetAsync($"/api/v1/admin/audit?action=inventory.transfer.approved&entityId={ownerTransfer["id"]}").OkJsonAsync();
        audit["items"]![0]!["after"]!.GetValue<string>().Should().Contain("\"ownerInitiatedAndAuthorized\": true");
    }

    [Fact]
    public async Task Short_receipt_creates_a_discrepancy_instead_of_completing()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 10, 100m);
        var owner = await factory.OwnerClientAsync();
        var t = await RequestAsync(owner, sku, 10);
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "ok" }).OkJsonAsync();
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/prepare", new { lines = new[] { new { lineId = Line(t), quantity = 10 } } }).OkJsonAsync();
        await owner.PostAsync($"{Base}/{t["id"]}/dispatch", null).OkJsonAsync();

        var received = await owner.PostAsJsonAsync($"{Base}/{t["id"]}/receive", new { lines = new[] { new { lineId = Line(t), quantity = 9 } } }).OkJsonAsync();
        received["status"]!.GetValue<string>().Should().Be("Discrepancy");
        (await factory.StockAsync(Hyderabad, sku)).Should().Be(9);
        (await factory.StockAsync(Karimnagar, sku, "InTransit")).Should().Be(1);

        var discrepancies = await owner.GetAsync($"/api/v1/admin/inventory/discrepancies?status=Open&branchId={Hyderabad}").OkJsonAsync();
        var d = discrepancies.AsArray().Single(x => x!["sourceId"]!.GetValue<Guid>() == t["id"]!.GetValue<Guid>())!;
        d["expectedQty"]!.GetValue<int>().Should().Be(10);
        d["actualQty"]!.GetValue<int>().Should().Be(9);

        // A manager cannot write it off; resolution is Owner-controlled by default.
        var hydManager = await factory.UserClientAsync(SystemRoles.BranchManager, Hyderabad);
        (await hydManager.PostAsJsonAsync($"/api/v1/admin/inventory/discrepancies/{d["id"]}/resolve", new { action = "WRITTEN_OFF", quantity = 1, notes = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resolved = await owner.PostAsJsonAsync($"/api/v1/admin/inventory/discrepancies/{d["id"]}/resolve",
            new { action = "RECEIVED_LATE", quantity = 1, notes = "Found in the courier bag" }).OkJsonAsync();
        resolved["status"]!.GetValue<string>().Should().Be("Resolved");
        (await factory.StockAsync(Hyderabad, sku)).Should().Be(10);
        (await owner.GetAsync($"{Base}/{t["id"]}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Received");
        (await factory.LayersAsync(Hyderabad, sku)).Sum(l => l.Remaining).Should().Be(10);
    }

    [Fact]
    public async Task Written_off_and_returned_shortfalls_settle_cost_correctly()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 6, 200m);
        var owner = await factory.OwnerClientAsync();
        var t = await RequestAsync(owner, sku, 6);
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "ok" }).OkJsonAsync();
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/prepare", new { lines = new[] { new { lineId = Line(t), quantity = 6 } } }).OkJsonAsync();
        await owner.PostAsync($"{Base}/{t["id"]}/dispatch", null).OkJsonAsync();
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/receive", new { lines = new[] { new { lineId = Line(t), quantity = 3 } } }).OkJsonAsync();
        var d = (await owner.GetAsync($"/api/v1/admin/inventory/discrepancies?status=Open&branchId={Hyderabad}").OkJsonAsync())
            .AsArray().Single(x => x!["sourceId"]!.GetValue<Guid>() == t["id"]!.GetValue<Guid>())!;

        await owner.PostAsJsonAsync($"/api/v1/admin/inventory/discrepancies/{d["id"]}/resolve", new { action = "RETURNED_TO_SOURCE", quantity = 2, notes = "Driver brought back" }).OkJsonAsync();
        var final = await owner.PostAsJsonAsync($"/api/v1/admin/inventory/discrepancies/{d["id"]}/resolve", new { action = "WRITTEN_OFF", quantity = 1, notes = "Lost in transit" }).OkJsonAsync();
        final["status"]!.GetValue<string>().Should().Be("Resolved");

        (await factory.StockAsync(Karimnagar, sku)).Should().Be(2);
        (await factory.StockAsync(Karimnagar, sku, "InTransit")).Should().Be(0);
        (await factory.LayersAsync(Karimnagar, sku)).Should().Equal((200m, 2));
        (await factory.LayersAsync(Hyderabad, sku)).Should().Equal((200m, 3));
    }

    [Fact]
    public async Task Serialized_transfer_is_by_scanned_piece()
    {
        var sku = await factory.CreateSkuAsync("Serialized");
        var grn = await factory.StockUpAsync(Karimnagar, sku, 3, 1500m);
        var itemIds = grn["items"]!.AsArray().Select(i => i!["itemId"]!.GetValue<Guid>()).ToList();
        var owner = await factory.OwnerClientAsync();
        var t = await RequestAsync(owner, sku, 2);
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "ok" }).OkJsonAsync();
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/prepare", new { lines = new[] { new { lineId = Line(t), itemIds = itemIds.Take(2) } } }).OkJsonAsync();
        await owner.PostAsync($"{Base}/{t["id"]}/dispatch", null).OkJsonAsync();

        var wrongPiece = await owner.PostAsJsonAsync($"{Base}/{t["id"]}/receive", new { lines = new[] { new { lineId = Line(t), itemIds = new[] { itemIds[2] } } } });
        (await wrongPiece.ErrorCodeAsync()).Should().Be("ITEM_NOT_IN_TRANSFER");

        var done = await owner.PostAsJsonAsync($"{Base}/{t["id"]}/receive", new { lines = new[] { new { lineId = Line(t), itemIds = itemIds.Take(2) } } }).OkJsonAsync();
        done["status"]!.GetValue<string>().Should().Be("Received");
        var items = await owner.GetAsync($"/api/v1/admin/inventory/items?skuId={sku}").OkJsonAsync();
        items.AsArray().Count(i => i!["branchId"]!.GetValue<Guid>() == Hyderabad).Should().Be(2);
    }

    [Fact]
    public async Task Preparing_more_than_available_fails_and_cancel_releases_prepared_stock()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 3, 10m);
        var owner = await factory.OwnerClientAsync();
        var t = await RequestAsync(owner, sku, 5);
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/approve", new { note = "ok" }).OkJsonAsync();
        var tooMany = await owner.PostAsJsonAsync($"{Base}/{t["id"]}/prepare", new { lines = new[] { new { lineId = Line(t), quantity = 5 } } });
        (await tooMany.ErrorCodeAsync()).Should().Be("INSUFFICIENT_STOCK");

        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/prepare", new { lines = new[] { new { lineId = Line(t), quantity = 3 } } }).OkJsonAsync();
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(0);
        await owner.PostAsJsonAsync($"{Base}/{t["id"]}/cancel", new { reason = "Not needed" }).OkJsonAsync();
        (await factory.StockAsync(Karimnagar, sku)).Should().Be(3);
    }
}
