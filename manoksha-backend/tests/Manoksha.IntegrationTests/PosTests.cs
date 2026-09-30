using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>
/// POS store sales (SPEC §19.3, §20, §23, §33; Phase 8 decisions): retail pricing, staff ≤5% / manager ≤15% / Owner above, approval by
/// PIN on the device (never self-approval), UPI-only by setting with split payments, exact pieces sold once, idempotent finalize.
/// </summary>
[Collection(ApiCollection.Name)]
public class PosTests(ManokshaApiFactory factory)
{
    private static readonly Guid Knr = DevelopmentSeedData.BranchKarimnagar;
    private static readonly Guid Hyd = DevelopmentSeedData.BranchHyderabad;

    private async Task<(HttpClient Pos, Guid UserId, string Password)> StaffAsync(string role, Guid branch)
    {
        var (userId, email, password) = await factory.CreateInternalUserAsync(role, branch);
        return (factory.Authorized((await factory.LoginAsync(email, password, "pos")).AccessToken), userId, password);
    }

    private static async Task SetPinAsync(HttpClient client, string password, string pin) =>
        (await client.PutAsJsonAsync("/api/v1/auth/me/approval-pin", new { currentPassword = password, pin })).StatusCode.Should().Be(HttpStatusCode.NoContent);

    private async Task<Guid> SkuAsync(decimal price, int stock = 5)
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(price);
        await factory.StockUpAsync(Knr, sku, stock, Math.Round(price / 2, 2));
        return sku;
    }

    private static object Upi(decimal amount, string reference = "UPI123456789") => new { method = "UPI", amount, reference };

    private static Task<HttpResponseMessage> SellAsync(HttpClient pos, object request, string? key = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales") { Content = JsonContent.Create(request) };
        msg.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return pos.SendAsync(msg);
    }

    private static object Sale(Guid sku, int qty, decimal? unitPrice, string? reason, object[] payments, object? approval = null, Guid[]? items = null, Guid? branch = null) => new
    {
        branchId = branch ?? Knr,
        lines = new[] { new { skuId = sku, quantity = qty, itemIds = items, unitPrice, priceReason = reason } },
        payments,
        approval,
        customerName = (string?)null,
        customerMobile = (string?)null,
    };

    [Fact]
    public async Task Retail_sale_paid_by_upi_sells_the_stock_and_produces_a_receipt()
    {
        var sku = await SkuAsync(800m);
        var (seller, sellerId, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);

        var context = await seller.GetAsync("/api/v1/pos/context").OkJsonAsync();
        context["branches"]!.AsArray().Select(b => b!["id"]!.GetValue<Guid>()).Should().Equal(Knr);
        context["paymentMethods"]!.AsArray().Select(m => m!.GetValue<string>()).Should().Equal("UPI");
        context["staffMaxDiscountPct"]!.GetValue<decimal>().Should().Be(5m);
        context["managerMaxDiscountPct"]!.GetValue<decimal>().Should().Be(15m);

        var sale = await SellAsync(seller, Sale(sku, 2, null, null, [Upi(1600m)])).OkJsonAsync();
        sale["number"]!.GetValue<string>().Should().MatchRegex("^MC-POS-\\d{6}$");
        sale["grandTotal"]!.GetValue<decimal>().Should().Be(1600m, "no shipping on store sales");
        (await factory.StockAsync(Knr, sku)).Should().Be(3);
        (await factory.LayersAsync(Knr, sku)).Sum(l => l.Remaining).Should().Be(3);

        var receipt = await seller.GetAsync($"/api/v1/pos/sales/{sale["orderId"]}/receipt").OkJsonAsync();
        receipt["lines"]![0]!["quantity"]!.GetValue<int>().Should().Be(2);
        receipt["payments"]![0]!["method"]!.GetValue<string>().Should().Be("UPI");
        receipt["cashier"]!.GetValue<string>().Should().Be($"Test {SystemRoles.SalesEmployee}");
        receipt["discountTotal"]!.GetValue<decimal>().Should().Be(0m);
        (await seller.GetAsync($"/api/v1/pos/sales/today?branchId={Knr}").OkJsonAsync()).AsArray()
            .Should().Contain(s => s!["orderId"]!.GetValue<Guid>() == sale["orderId"]!.GetValue<Guid>());

        var owner = await factory.OwnerClientAsync();
        var adminView = await owner.GetAsync($"/api/v1/admin/orders/{sale["orderId"]}").OkJsonAsync();
        adminView["status"]!.GetValue<string>().Should().Be("Completed");
        adminView["channel"]!.GetValue<string>().Should().Be("Store");
        adminView["costOfGoods"]!.GetValue<decimal>().Should().Be(800m);
        sellerId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Payments_must_use_accepted_methods_carry_references_and_add_up()
    {
        var sku = await SkuAsync(500m);
        var (seller, _, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);
        (await (await SellAsync(seller, Sale(sku, 1, null, null, []))).ErrorCodeAsync()).Should().Be("PAYMENT_REQUIRED");
        (await (await SellAsync(seller, Sale(sku, 1, null, null, [new { method = "CASH", amount = 500m, reference = (string?)null }]))).ErrorCodeAsync())
            .Should().Be("PAYMENT_METHOD_NOT_ACCEPTED", "only UPI is enabled by default");
        (await (await SellAsync(seller, Sale(sku, 1, null, null, [new { method = "UPI", amount = 500m, reference = "" }]))).ErrorCodeAsync())
            .Should().Be("PAYMENT_REFERENCE_REQUIRED");
        (await (await SellAsync(seller, Sale(sku, 1, null, null, [Upi(400m)]))).ErrorCodeAsync()).Should().Be("PAYMENT_TOTAL_MISMATCH");
        (await factory.StockAsync(Knr, sku)).Should().Be(5, "nothing is sold unless the sale is finalized");

        // Split payment (two UPI transfers).
        await SellAsync(seller, Sale(sku, 1, null, null, [Upi(300m, "UPIREF0001"), Upi(200m, "UPIREF0002")])).OkJsonAsync();

        // The Owner enables cash in settings → accepted without a reference.
        var owner = await factory.OwnerClientAsync();
        var setting = (await owner.GetAsync("/api/v1/admin/settings").OkJsonAsync()).AsArray().Single(s => s!["key"]!.GetValue<string>() == SettingKeys.PosPaymentMethods)!;
        var version = setting["version"]!.GetValue<int>();
        await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.PosPaymentMethods}", new { value = "UPI,CASH", expectedVersion = version, reason = "accept cash" }).OkJsonAsync();
        try
        {
            await SellAsync(seller, Sale(sku, 1, null, null, [new { method = "CASH", amount = 500m, reference = (string?)null }])).OkJsonAsync();
        }
        finally
        {
            await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.PosPaymentMethods}", new { value = "UPI", expectedVersion = version + 1, reason = "restore" }).OkJsonAsync();
        }
    }

    [Fact]
    public async Task Discounts_follow_the_seller_manager_and_owner_limits_with_pin_approval()
    {
        var sku = await SkuAsync(1000m, stock: 10);
        var (seller, _, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);
        var (manager, managerId, managerPassword) = await StaffAsync(SystemRoles.BranchManager, Knr);
        await SetPinAsync(manager, managerPassword, "4826");

        // Within the seller's own 5 %: allowed with a reason.
        (await (await SellAsync(seller, Sale(sku, 1, 960m, null, [Upi(960m)]))).ErrorCodeAsync()).Should().Be("PRICE_REASON_REQUIRED");
        await SellAsync(seller, Sale(sku, 1, 960m, "Regular customer", [Upi(960m)])).OkJsonAsync();
        (await (await SellAsync(seller, Sale(sku, 1, 1100m, "more", [Upi(1100m)]))).ErrorCodeAsync()).Should().Be("PRICE_INVALID", "never above retail");

        // 10 %: needs a manager's PIN.
        var quote = await seller.PostAsJsonAsync("/api/v1/pos/sales/quote", Sale(sku, 1, 900m, "Festival offer", [])).OkJsonAsync();
        quote["approvalRequired"]!.GetValue<string>().Should().Be("MANAGER");
        (await (await SellAsync(seller, Sale(sku, 1, 900m, "Festival offer", [Upi(900m)]))).ErrorCodeAsync()).Should().Be("APPROVAL_REQUIRED");
        var approvers = await seller.GetAsync($"/api/v1/pos/approvers?branchId={Knr}&level=MANAGER").OkJsonAsync();
        approvers.AsArray().Should().Contain(a => a!["userId"]!.GetValue<Guid>() == managerId && a["hasPin"]!.GetValue<bool>());
        (await (await SellAsync(seller, Sale(sku, 1, 900m, "Festival offer", [Upi(900m)], new { approverUserId = managerId, pin = "0000" }))).ErrorCodeAsync())
            .Should().Be("APPROVAL_PIN_INVALID");
        var approved = await SellAsync(seller, Sale(sku, 1, 900m, "Festival offer", [Upi(900m)], new { approverUserId = managerId, pin = "4826" })).OkJsonAsync();
        (await factory.ScalarAsync<string>($"SELECT approval_level FROM orders.pos_price_overrides WHERE order_id = '{approved["orderId"]}'")).Should().Be("MANAGER");
        (await factory.ScalarAsync<Guid>($"SELECT approver_user_id FROM orders.pos_price_overrides WHERE order_id = '{approved["orderId"]}'")).Should().Be(managerId);
        (await seller.GetAsync($"/api/v1/pos/sales/{approved["orderId"]}/receipt").OkJsonAsync())["approvedBy"]!.GetValue<string>().Should().Be("Test BRANCH_MANAGER");
        var posSale = (await (await factory.OwnerClientAsync()).GetAsync($"/api/v1/admin/orders/{approved["orderId"]}").OkJsonAsync())["posSale"]!;
        posSale["payments"]![0]!["amount"]!.GetValue<decimal>().Should().Be(900m);
        posSale["priceOverrides"]![0]!["reason"]!.GetValue<string>().Should().Be("Festival offer");
        posSale["priceOverrides"]![0]!["approver"]!.GetValue<string>().Should().Be("Test BRANCH_MANAGER");

        // 20 %: only the Owner; a manager's PIN is not enough.
        (await (await SellAsync(seller, Sale(sku, 1, 800m, "Damaged box", [Upi(800m)], new { approverUserId = managerId, pin = "4826" }))).ErrorCodeAsync())
            .Should().Be("APPROVER_NOT_AUTHORIZED");
        var ownerPos = factory.Authorized((await factory.LoginAsync(ManokshaApiFactory.OwnerEmail, ManokshaApiFactory.OwnerPassword, "pos")).AccessToken);
        await SetPinAsync(ownerPos, ManokshaApiFactory.OwnerPassword, "9137");
        await SellAsync(seller, Sale(sku, 1, 800m, "Damaged box", [Upi(800m)], new { approverUserId = factory.OwnerUserId, pin = "9137" })).OkJsonAsync();

        // A manager sells up to 15 % alone, cannot approve their own larger discount.
        (await manager.PostAsJsonAsync("/api/v1/pos/sales/quote", Sale(sku, 1, 880m, "Loyal", [])).OkJsonAsync())["approvalRequired"]!.GetValue<string>().Should().Be("NONE");
        (await (await SellAsync(manager, Sale(sku, 1, 700m, "Clearance", [Upi(700m)], new { approverUserId = managerId, pin = "4826" }))).ErrorCodeAsync())
            .Should().Be("SELF_APPROVAL_NOT_ALLOWED");

        // Price-override history cannot be edited.
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var tamper = new NpgsqlCommand($"UPDATE orders.pos_price_overrides SET final_unit_price = 1 WHERE order_id = '{approved["orderId"]}'", c);
        (await FluentActions.Awaiting(() => tamper.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Wrong_pins_lock_the_approver_for_a_while()
    {
        var sku = await SkuAsync(1000m);
        var (seller, _, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);
        var (manager, managerId, managerPassword) = await StaffAsync(SystemRoles.BranchManager, Knr);
        await SetPinAsync(manager, managerPassword, "5719");
        for (var i = 0; i < 5; i++)
        {
            await SellAsync(seller, Sale(sku, 1, 900m, "Offer", [Upi(900m)], new { approverUserId = managerId, pin = "1111" }));
        }
        (await (await SellAsync(seller, Sale(sku, 1, 900m, "Offer", [Upi(900m)], new { approverUserId = managerId, pin = "5719" }))).ErrorCodeAsync())
            .Should().Be("APPROVAL_PIN_LOCKED");
        (await factory.StockAsync(Knr, sku)).Should().Be(5);
    }

    [Fact]
    public async Task A_serialized_piece_scanned_twice_is_sold_only_once()
    {
        var sku = await factory.CreateSkuAsync("Serialized");
        var owner = await factory.OwnerClientAsync();
        var productId = await factory.ScalarAsync<Guid>($"SELECT product_id FROM catalog.skus WHERE id = '{sku}'");
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/status", new { status = "Active", reason = "launch" }).OkJsonAsync();
        await factory.SetRetailPriceAsync(sku, 4500m);
        await factory.StockUpAsync(Knr, sku, 2, 2000m);
        var piece = await factory.ScalarAsync<Guid>($"SELECT id FROM inventory.inventory_items WHERE sku_id = '{sku}' AND status = 'Available' ORDER BY id LIMIT 1");
        var (seller, _, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);

        (await (await SellAsync(seller, Sale(sku, 1, null, null, [Upi(4500m)]))).ErrorCodeAsync()).Should().Be("ITEMS_REQUIRED");
        await SellAsync(seller, Sale(sku, 1, null, null, [Upi(4500m)], items: [piece])).OkJsonAsync();
        var again = await SellAsync(seller, Sale(sku, 1, null, null, [Upi(4500m, "UPI987654321")], items: [piece]));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ErrorCodeAsync()).Should().Be("ITEM_NOT_AVAILABLE");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM orders.order_lines WHERE sku_id = '{sku}'")).Should().Be(1, "the second sale created nothing");
        (await factory.StockAsync(Knr, sku)).Should().Be(1);
    }

    [Fact]
    public async Task Retrying_a_sale_after_a_dropped_connection_is_one_sale()
    {
        var sku = await SkuAsync(250m);
        var (seller, _, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);
        var key = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => SellAsync(seller, Sale(sku, 2, null, null, [Upi(500m)]), key)));
        var numbers = new HashSet<string>();
        foreach (var r in results.Where(r => r.StatusCode == HttpStatusCode.OK))
        {
            numbers.Add((await r.ReadJsonAsync())["number"]!.GetValue<string>());
        }
        numbers.Add((await SellAsync(seller, Sale(sku, 2, null, null, [Upi(500m)]), key).OkJsonAsync())["number"]!.GetValue<string>());
        numbers.Should().ContainSingle();
        (await factory.StockAsync(Knr, sku)).Should().Be(3);
    }

    [Fact]
    public async Task Sellers_sell_only_at_their_branch_and_only_from_the_pos_app()
    {
        var sku = await SkuAsync(300m);
        var (seller, _, _) = await StaffAsync(SystemRoles.SalesEmployee, Knr);
        (await SellAsync(seller, Sale(sku, 1, null, null, [Upi(300m)], branch: Hyd))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = await factory.UserClientAsync(SystemRoles.SalesEmployee, Knr);
        (await SellAsync(admin, Sale(sku, 1, null, null, [Upi(300m)]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "POS routes need a POS sign-in");
        (await seller.GetAsync($"/api/v1/pos/items?branchId={Knr}&q=Priced").OkJsonAsync()).AsArray().Should().Contain(i => i!["skuId"]!.GetValue<Guid>() == sku);
    }
}
