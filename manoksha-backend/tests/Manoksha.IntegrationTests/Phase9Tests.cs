using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Manoksha.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Phase 9 (SPEC §28, §29, §31, §33; ADR-001 §37–40): notifications from business events (in-app + email, retries, failed-notification
/// exception), sensitive alerts, the Exception Center, dashboards and reports with Owner-only cost figures, the reseller dashboard.
/// </summary>
[Collection(ApiCollection.Name)]
public class Phase9Tests(ManokshaApiFactory factory)
{
    private static readonly Guid Knr = DevelopmentSeedData.BranchKarimnagar;

    private async Task<Guid> NewBranchAsync()
    {
        var owner = await factory.OwnerClientAsync();
        var code = "T" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        return (await owner.PostAsJsonAsync("/api/v1/admin/branches", new { code, name = "Test " + code, address = (object?)null, reason = "phase 9 test" })
            .OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();
    }

    private async Task<HttpClient> StaffAsync(string role, Guid branch, string client = "admin")
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(role, branch);
        return factory.Authorized((await factory.LoginAsync(email, password, client)).AccessToken);
    }

    private static async Task SellAsync(HttpClient pos, Guid branch, Guid sku, int qty, decimal total)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
        {
            Content = JsonContent.Create(new
            {
                branchId = branch,
                lines = new[] { new { skuId = sku, quantity = qty, itemIds = (Guid[]?)null, unitPrice = (decimal?)null, priceReason = (string?)null } },
                payments = new[] { new { method = "UPI", amount = total, reference = "UPI" + Guid.NewGuid().ToString("N")[..10] } },
            }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        await pos.SendAsync(msg).OkJsonAsync();
    }

    private static async Task<JsonArray> InboxAsync(HttpClient client) =>
        (await client.GetAsync("/api/v1/admin/notifications").OkJsonAsync())["items"]!.AsArray();

    [Fact]
    public async Task Reseller_order_notifies_the_packing_branch_and_emails_the_reseller_once()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(500m);
        await factory.StockUpAsync(Knr, sku, 3, 200m);
        var (reseller, resellerId, mobile) = await factory.FundedResellerAsync(5000m);
        var order = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)).OkJsonAsync())["order"]!;
        var number = order["number"]!.GetValue<string>();

        await factory.DispatchOutboxAsync();
        await factory.DispatchOutboxAsync(); // a second run creates nothing new (dedupe keys)

        var knrManager = await StaffAsync(SystemRoles.BranchManager, Knr);
        var hydManager = await StaffAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchHyderabad);
        (await InboxAsync(knrManager)).Should().Contain(n => n!["title"]!.GetValue<string>().Contains(number), "the fulfilling branch packs the order");
        (await InboxAsync(hydManager)).Should().NotContain(n => n!["title"]!.GetValue<string>().Contains(number), "other branches never see it");

        var email = $"r{mobile[^6..]}@example.com";
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM notifications.email_deliveries WHERE reference = '{number}' AND to_address = '{email}'")).Should().Be(1);
        await factory.SendEmailsAsync();
        factory.Emails.To(email).Should().ContainSingle(m => m.Subject == $"Order {number} confirmed");
        (await factory.ScalarAsync<string>($"SELECT status FROM notifications.email_deliveries WHERE reference = '{number}' AND to_address = '{email}'")).Should().Be("Sent");

        // Reading clears the badge for that user only.
        var unread = (await knrManager.GetAsync("/api/v1/admin/notifications/unread-count").OkJsonAsync())["unread"]!.GetValue<int>();
        unread.Should().BeGreaterThan(0);
        (await knrManager.PostAsync("/api/v1/admin/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await knrManager.GetAsync("/api/v1/admin/notifications/unread-count").OkJsonAsync())["unread"]!.GetValue<int>().Should().Be(0);
        resellerId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Email_failure_never_undoes_the_order_is_retried_and_ends_in_the_exception_center()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(300m);
        await factory.StockUpAsync(Knr, sku, 2, 100m);
        var (reseller, _, mobile) = await factory.FundedResellerAsync(2000m);
        var email = $"r{mobile[^6..]}@example.com";
        factory.Emails.FailFor[email] = true;
        var order = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)).OkJsonAsync())["order"]!;
        var number = order["number"]!.GetValue<string>();
        await factory.DispatchOutboxAsync();

        var id = await factory.ScalarAsync<Guid>($"SELECT id FROM notifications.email_deliveries WHERE reference = '{number}' AND to_address = '{email}'");
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await factory.ScalarAsync<int>($"UPDATE notifications.email_deliveries SET next_attempt_at = now() - interval '1 day' WHERE id = '{id}' RETURNING 1");
            await factory.SendEmailsAsync();
        }
        (await factory.ScalarAsync<string>($"SELECT status FROM notifications.email_deliveries WHERE id = '{id}'")).Should().Be("Failed");
        (await reseller.GetAsync($"/api/v1/reseller/orders/{order["id"]}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Confirmed");

        var owner = await factory.OwnerClientAsync();
        var center = await owner.GetAsync("/api/v1/admin/exceptions?type=FAILED_NOTIFICATION").OkJsonAsync();
        center["items"]!.AsArray().Should().Contain(i => i!["id"]!.GetValue<Guid>() == id);

        factory.Emails.FailFor.TryRemove(email, out _);
        await owner.PostAsync($"/api/v1/admin/notifications/emails/{id}/retry", null).OkJsonAsync();
        await factory.SendEmailsAsync();
        (await factory.ScalarAsync<string>($"SELECT status FROM notifications.email_deliveries WHERE id = '{id}'")).Should().Be("Sent");
        (await owner.GetAsync("/api/v1/admin/exceptions?type=FAILED_NOTIFICATION").OkJsonAsync())["items"]!.AsArray()
            .Should().NotContain(i => i!["id"]!.GetValue<Guid>() == id);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM audit.audit_log WHERE action = 'notifications.email.retried' AND entity_id = '{id}'")).Should().Be(1);
    }

    [Fact]
    public async Task Staff_lockout_raises_a_sensitive_alert_emailed_to_the_owner_and_resolved_with_a_note()
    {
        var (userId, email, _) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Knr);
        for (var i = 0; i < 5; i++)
        {
            await factory.InternalLoginRawAsync(email, "wrong-password-" + i);
        }
        await factory.DispatchOutboxAsync();

        var alertId = await factory.ScalarAsync<Guid>($"SELECT id FROM notifications.alerts WHERE kind = 'ACCOUNT_LOCKED' AND subject_user_id = '{userId}'");
        alertId.Should().NotBeEmpty();
        (await factory.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.email_deliveries WHERE to_address = '{ManokshaApiFactory.OwnerEmail}' AND subject LIKE 'CRITICAL:%' AND reference = 'Test {SystemRoles.SalesEmployee}'"))
            .Should().BeGreaterThan(0);

        var owner = await factory.OwnerClientAsync();
        (await owner.GetAsync("/api/v1/admin/exceptions?type=SENSITIVE_ALERT").OkJsonAsync())["items"]!.AsArray()
            .Should().Contain(i => i!["id"]!.GetValue<Guid>() == alertId);
        (await (await owner.PostAsJsonAsync($"/api/v1/admin/alerts/{alertId}/resolve", new { note = "" })).ErrorCodeAsync()).Should().Be("RESOLUTION_NOTE_REQUIRED");
        await owner.PostAsJsonAsync($"/api/v1/admin/alerts/{alertId}/resolve", new { note = "Staff forgot the password; reset it." }).OkJsonAsync();
        (await owner.GetAsync("/api/v1/admin/exceptions?type=SENSITIVE_ALERT").OkJsonAsync())["items"]!.AsArray()
            .Should().NotContain(i => i!["id"]!.GetValue<Guid>() == alertId);

        // Branch managers never see Owner-only exception types.
        var manager = await StaffAsync(SystemRoles.BranchManager, Knr);
        var managerView = await manager.GetAsync("/api/v1/admin/exceptions").OkJsonAsync();
        managerView["items"]!.AsArray().Should().NotContain(i => i!["type"]!.GetValue<string>() == "SENSITIVE_ALERT" || i["type"]!.GetValue<string>() == "PAYMENT_RECONCILIATION");
    }

    [Fact]
    public async Task Unfulfilled_checkout_appears_in_the_exception_center_until_support_follows_up()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(250m); // no stock anywhere
        var (reseller, _, _) = await factory.FundedResellerAsync(1000m);
        var result = await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)).OkJsonAsync();
        var reference = result["inquiry"]!["reference"]!.GetValue<string>();
        var owner = await factory.OwnerClientAsync();

        var items = (await owner.GetAsync("/api/v1/admin/exceptions?type=UNFULFILLED_CHECKOUT").OkJsonAsync())["items"]!.AsArray();
        var item = items.Single(i => i!["reference"]!.GetValue<string>() == reference)!;
        (await owner.GetAsync("/api/v1/admin/exceptions").OkJsonAsync())["counts"]!.AsArray()
            .Single(c => c!["type"]!.GetValue<string>() == "UNFULFILLED_CHECKOUT")!["open"]!.GetValue<int>().Should().BeGreaterThan(0);

        var manager = await StaffAsync(SystemRoles.BranchManager, Knr);
        (await manager.PostAsJsonAsync($"/api/v1/admin/fulfillment-inquiries/{item["id"]}/close", new { note = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await owner.PostAsJsonAsync($"/api/v1/admin/fulfillment-inquiries/{item["id"]}/close", new { note = "Called the reseller; will restock next week." }).OkJsonAsync();
        (await owner.GetAsync("/api/v1/admin/exceptions?type=UNFULFILLED_CHECKOUT").OkJsonAsync())["items"]!.AsArray()
            .Should().NotContain(i => i!["reference"]!.GetValue<string>() == reference);
        (await (await owner.PostAsJsonAsync($"/api/v1/admin/fulfillment-inquiries/{item["id"]}/close", new { note = "again" })).ErrorCodeAsync()).Should().Be("INQUIRY_CLOSED");
    }

    [Fact]
    public async Task Dashboard_and_reports_show_fifo_gross_profit_to_the_owner_only()
    {
        var branch = await NewBranchAsync();
        var (sku, _) = await factory.CreatePricedSkuAsync(1000m);
        await factory.StockUpAsync(branch, sku, 3, 400m);
        var seller = await StaffAsync(SystemRoles.SalesEmployee, branch, "pos");
        await SellAsync(seller, branch, sku, 2, 2000m);

        var owner = await factory.OwnerClientAsync();
        var dash = await owner.GetAsync($"/api/v1/admin/dashboard?branchId={branch}").OkJsonAsync();
        dash["costVisible"]!.GetValue<bool>().Should().BeTrue();
        var today = dash["salesToday"]!;
        today["orders"]!.GetValue<int>().Should().Be(1);
        today["units"]!.GetValue<int>().Should().Be(2);
        today["revenue"]!.GetValue<decimal>().Should().Be(2000m);
        today["cost"]!.GetValue<decimal>().Should().Be(800m);
        today["grossProfit"]!.GetValue<decimal>().Should().Be(1200m);
        today["marginPct"]!.GetValue<decimal>().Should().Be(60m);
        dash["inventory"]!["availableUnits"]!.GetValue<int>().Should().Be(1);
        dash["inventory"]!["stockValue"]!.GetValue<decimal>().Should().Be(400m);
        dash["lowStock"]!.AsArray().Should().Contain(r => r!["skuId"]!.GetValue<Guid>() == sku, "1 available is at or below the default threshold of 2");

        var report = await owner.GetAsync($"/api/v1/admin/reports/sales?branchId={branch}&groupBy=channel").OkJsonAsync();
        var store = report["rows"]!.AsArray().Single(r => r!["key"]!.GetValue<string>() == "Store")!;
        store["grossProfit"]!.GetValue<decimal>().Should().Be(1200m);
        report["total"]!["revenue"]!.GetValue<decimal>().Should().Be(2000m);
        var products = await owner.GetAsync($"/api/v1/admin/reports/products?branchId={branch}").OkJsonAsync();
        products["rows"]!.AsArray().Single(r => r!["skuId"]!.GetValue<Guid>() == sku)!["cost"]!.GetValue<decimal>().Should().Be(800m);
        var valuation = await owner.GetAsync($"/api/v1/admin/reports/inventory-valuation?branchId={branch}").OkJsonAsync();
        valuation["totalValue"]!.GetValue<decimal>().Should().Be(400m);

        // The branch manager sees the same sales but never cost, profit or stock value, and only their own branch.
        var manager = await StaffAsync(SystemRoles.BranchManager, branch);
        var mine = await manager.GetAsync("/api/v1/admin/dashboard").OkJsonAsync();
        mine["costVisible"]!.GetValue<bool>().Should().BeFalse();
        mine["salesToday"]!["revenue"]!.GetValue<decimal>().Should().Be(2000m);
        mine["salesToday"]!["grossProfit"].Should().BeNull();
        mine["salesToday"]!["byChannel"]!.AsArray().Should().OnlyContain(c => c!["cost"] == null);
        mine["inventory"]!["stockValue"].Should().BeNull();
        mine["branches"]!.AsArray().Select(b => b!["id"]!.GetValue<Guid>()).Should().Equal(branch);
        mine["resellers"].Should().BeNull();
        (await manager.GetAsync($"/api/v1/admin/dashboard?branchId={Knr}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync("/api/v1/admin/reports/inventory-valuation")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync("/api/v1/admin/reports/sales?groupBy=reseller")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync($"/api/v1/admin/reports/sales?branchId={branch}&groupBy=channel").OkJsonAsync())["rows"]!.AsArray()
            .Should().OnlyContain(r => r!["grossProfit"] == null);

        // Sales staff have no reports.
        (await (await StaffAsync(SystemRoles.SalesEmployee, branch)).GetAsync("/api/v1/admin/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Daily_jobs_raise_one_low_stock_alert_per_branch_and_one_owner_summary_per_day()
    {
        var branch = await NewBranchAsync();
        var (sku, _) = await factory.CreatePricedSkuAsync(800m);
        await factory.StockUpAsync(branch, sku, 1, 300m);
        var today = DateOnly.FromDateTime(factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

        for (var run = 0; run < 2; run++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await NotificationJobs.LowStockAlertsAsync(scope.ServiceProvider, today, CancellationToken.None);
            await NotificationJobs.DailySummaryAsync(scope.ServiceProvider, today, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<Manoksha.Persistence.ManokshaDbContext>().SaveChangesAsync();
        }
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE event_type = 'reporting.low_stock' AND branch_id = '{branch}'"))
            .Should().Be(1);
        (await factory.ScalarAsync<long>(
            $"SELECT count(*) FROM notifications.email_deliveries WHERE event_type = 'reporting.daily_summary' AND reference = '{today:yyyy-MM-dd}' AND to_address = '{ManokshaApiFactory.OwnerEmail}'"))
            .Should().Be(1);

        var manager = await StaffAsync(SystemRoles.BranchManager, branch);
        (await InboxAsync(manager)).Should().Contain(n => n!["title"]!.GetValue<string>().Contains("low on stock"));
    }

    [Fact]
    public async Task Reseller_dashboard_shows_only_the_resellers_own_figures()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(400m);
        await factory.StockUpAsync(Knr, sku, 2, 150m);
        var (reseller, _, _) = await factory.FundedResellerAsync(3000m);
        var order = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)).OkJsonAsync())["order"]!;
        var (other, _, _) = await factory.FundedResellerAsync(0m);

        var mine = await reseller.GetAsync("/api/v1/reseller/dashboard").OkJsonAsync();
        mine["ordersThisMonth"]!.GetValue<int>().Should().Be(1);
        mine["spentThisMonth"]!.GetValue<decimal>().Should().Be(order["grandTotal"]!.GetValue<decimal>());
        mine["openOrders"]!.GetValue<int>().Should().Be(1);
        mine["walletBalance"]!.GetValue<decimal>().Should().Be(3000m - order["grandTotal"]!.GetValue<decimal>());
        (await other.GetAsync("/api/v1/reseller/dashboard").OkJsonAsync())["ordersAllTime"]!.GetValue<int>().Should().Be(0);
    }
}
