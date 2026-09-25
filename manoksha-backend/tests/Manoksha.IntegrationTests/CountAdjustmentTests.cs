using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Blind counts, discrepancies and adjustment approvals (SPEC §26, ADR-001 §14–16).</summary>
[Collection(ApiCollection.Name)]
public class CountAdjustmentTests(ManokshaApiFactory factory)
{
    private static readonly Guid Karimnagar = DevelopmentSeedData.BranchKarimnagar;
    private const string Base = "/api/v1/admin/inventory";

    private async Task SetLimitAsync(decimal value)
    {
        var owner = await factory.OwnerClientAsync();
        var settings = await owner.GetAsync("/api/v1/admin/settings").OkJsonAsync();
        var version = settings.AsArray().Single(s => s!["key"]!.GetValue<string>() == SettingKeys.AdjustmentManagerMaxValue)!["version"]!.GetValue<int>();
        await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.AdjustmentManagerMaxValue}", new { value, expectedVersion = version, reason = "test" }).OkJsonAsync();
    }

    [Fact]
    public async Task Blind_count_hides_system_quantity_and_creates_discrepancies()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 10, 40m);
        var counter = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Karimnagar);

        var count = await counter.PostAsJsonAsync($"{Base}/counts", new { branchId = Karimnagar, skuIds = new[] { sku } }).OkJsonAsync();
        count["lines"]![0]!["systemQty"].Should().BeNull();
        await counter.PutAsJsonAsync($"{Base}/counts/{count["id"]}/lines", new { lines = new[] { new { skuId = sku, countedQty = 8 } } }).OkJsonAsync();
        var submitted = await counter.PostAsync($"{Base}/counts/{count["id"]}/submit", null).OkJsonAsync();
        submitted["lines"]![0]!["systemQty"]!.GetValue<int>().Should().Be(10);
        submitted["lines"]![0]!["variance"]!.GetValue<int>().Should().Be(-2);
        submitted["discrepancyIds"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Adjustments_need_a_different_approver_and_owner_above_the_limit()
    {
        await SetLimitAsync(0m);
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 10, 100m);
        var inventory = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Karimnagar);
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Karimnagar);
        var owner = await factory.OwnerClientAsync();

        var adj = await inventory.PostAsJsonAsync($"{Base}/adjustments", new
        {
            branchId = Karimnagar, skuId = sku, kind = "StatusChange", fromStatus = "Available", toStatus = "Damaged", quantity = 2, reasonCode = "DAMAGED", notes = "Water leak",
        }).OkJsonAsync();
        adj["requiresOwner"]!.GetValue<bool>().Should().BeTrue("default limit is ₹0");

        var managerTry = await manager.PostAsJsonAsync($"{Base}/adjustments/{adj["id"]}/approve", new { note = "ok" });
        (await managerTry.ErrorCodeAsync()).Should().Be("OWNER_APPROVAL_REQUIRED");
        (await factory.StockAsync(Karimnagar, sku, "Damaged")).Should().Be(0);

        var applied = await owner.PostAsJsonAsync($"{Base}/adjustments/{adj["id"]}/approve", new { note = "confirmed" }).OkJsonAsync();
        applied["status"]!.GetValue<string>().Should().Be("Applied");
        applied["valueAtCost"]!.GetValue<decimal>().Should().Be(200m);
        (await factory.StockAsync(Karimnagar, sku, "Damaged")).Should().Be(2);

        // Twice-approval does nothing more.
        (await (await owner.PostAsJsonAsync($"{Base}/adjustments/{adj["id"]}/approve", new { note = "again" })).ErrorCodeAsync()).Should().Be("ADJUSTMENT_ALREADY_DECIDED");

        // Within a raised limit, the branch manager may approve — but never their own request.
        await SetLimitAsync(1000m);
        var mine = await manager.PostAsJsonAsync($"{Base}/adjustments", new
        {
            branchId = Karimnagar, skuId = sku, kind = "WriteOff", fromStatus = "Damaged", quantity = 1, reasonCode = "DISPOSED", notes = "Beyond repair",
        }).OkJsonAsync();
        (await (await manager.PostAsJsonAsync($"{Base}/adjustments/{mine["id"]}/approve", new { note = "x" })).ErrorCodeAsync()).Should().Be("SELF_APPROVAL_NOT_ALLOWED");
        var otherManager = await factory.UserClientAsync(SystemRoles.BranchManager, Karimnagar);
        await otherManager.PostAsJsonAsync($"{Base}/adjustments/{mine["id"]}/approve", new { note = "ok" }).OkJsonAsync();
        (await factory.StockAsync(Karimnagar, sku, "Damaged")).Should().Be(1);
        (await factory.LayersAsync(Karimnagar, sku)).Sum(l => l.Remaining).Should().Be(9, "write-off consumes FIFO cost");
        await SetLimitAsync(0m);
    }

    [Fact]
    public async Task Owner_cannot_approve_own_adjustment()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 2, 10m);
        var owner = await factory.OwnerClientAsync();
        var adj = await owner.PostAsJsonAsync($"{Base}/adjustments", new
        {
            branchId = Karimnagar, skuId = sku, kind = "StatusChange", fromStatus = "Available", toStatus = "Blocked", quantity = 1, reasonCode = "HOLD", notes = "Quality check",
        }).OkJsonAsync();
        (await (await owner.PostAsJsonAsync($"{Base}/adjustments/{adj["id"]}/approve", new { note = "x" })).ErrorCodeAsync()).Should().Be("SELF_APPROVAL_NOT_ALLOWED");
    }

    [Fact]
    public async Task Found_stock_requires_approver_cost_and_resolves_the_count_discrepancy()
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 5, 70m);
        var counter = await factory.UserClientAsync(SystemRoles.InventoryEmployee, Karimnagar);
        var count = await counter.PostAsJsonAsync($"{Base}/counts", new { branchId = Karimnagar, skuIds = new[] { sku } }).OkJsonAsync();
        await counter.PutAsJsonAsync($"{Base}/counts/{count["id"]}/lines", new { lines = new[] { new { skuId = sku, countedQty = 7 } } }).OkJsonAsync();
        var submitted = await counter.PostAsync($"{Base}/counts/{count["id"]}/submit", null).OkJsonAsync();
        var discrepancyId = submitted["discrepancyIds"]![0]!.GetValue<Guid>();

        var adj = await counter.PostAsJsonAsync($"{Base}/adjustments", new
        {
            branchId = Karimnagar, skuId = sku, kind = "Found", quantity = 2, reasonCode = "COUNT_SURPLUS", notes = "Found in back store", discrepancyId,
        }).OkJsonAsync();
        var owner = await factory.OwnerClientAsync();
        (await (await owner.PostAsJsonAsync($"{Base}/adjustments/{adj["id"]}/approve", new { note = "ok" })).ErrorCodeAsync()).Should().Be("UNIT_COST_REQUIRED");
        await owner.PostAsJsonAsync($"{Base}/adjustments/{adj["id"]}/approve", new { note = "ok", unitCost = 65.50m }).OkJsonAsync();

        (await factory.StockAsync(Karimnagar, sku)).Should().Be(7);
        (await factory.LayersAsync(Karimnagar, sku)).Should().Equal((70m, 5), (65.50m, 2));
        var d = await owner.GetAsync($"{Base}/discrepancies/{discrepancyId}").OkJsonAsync();
        d["status"]!.GetValue<string>().Should().Be("Resolved");
        d["resolution"]!.GetValue<string>().Should().Be("ADJUSTED");
    }

    [Theory]
    [InlineData("UPDATE inventory.inventory_movements SET quantity = 999")]
    [InlineData("DELETE FROM inventory.inventory_movements")]
    [InlineData("DELETE FROM inventory.cost_layer_consumptions")]
    public async Task Movement_and_cost_history_cannot_be_edited(string sql)
    {
        var sku = await factory.CreateSkuAsync();
        await factory.StockUpAsync(Karimnagar, sku, 1, 1m);
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        (await FluentActions.Awaiting(() => cmd.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Every_stock_change_leaves_a_movement()
    {
        var sku = await factory.CreateSkuAsync();
        var grn = await factory.StockUpAsync(Karimnagar, sku, 4, 12m);
        var owner = await factory.OwnerClientAsync();
        var movements = await owner.GetAsync($"{Base}/movements?skuId={sku}").OkJsonAsync();
        var m = movements.AsArray().Single()!;
        m["movementType"]!.GetValue<string>().Should().Be("GOODS_RECEIPT");
        m["toStatus"]!.GetValue<string>().Should().Be("Available");
        m["quantity"]!.GetValue<int>().Should().Be(4);
        m["referenceNumber"]!.GetValue<string>().Should().Be(grn["number"]!.GetValue<string>());
    }
}
