using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Manoksha.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Phase 10 concurrency and failure-injection suite (SPEC §13, §32, §33): simultaneous sellers never sell the same stock twice, background
/// workers running side by side never duplicate work, and an outage of a dependency leaves no half-written business record.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConcurrencyAndFailureTests(ManokshaApiFactory factory)
{
    private static readonly Guid Knr = DevelopmentSeedData.BranchKarimnagar;

    private async Task<HttpClient> CashierAsync()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Knr);
        return factory.Authorized((await factory.LoginAsync(email, password, "pos")).AccessToken);
    }

    private static Task<HttpResponseMessage> SellAsync(HttpClient pos, Guid sku, decimal price, Guid[]? items = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pos/sales")
        {
            Content = JsonContent.Create(new
            {
                branchId = Knr,
                lines = new[] { new { skuId = sku, quantity = 1, itemIds = items, unitPrice = (decimal?)null, priceReason = (string?)null } },
                payments = new[] { new { method = "UPI", amount = price, reference = "UPI" + Guid.NewGuid().ToString("N")[..10] } },
            }),
        };
        msg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return pos.SendAsync(msg);
    }

    [Fact]
    public async Task Two_cashiers_scanning_the_same_piece_at_once_sell_it_once()
    {
        var sku = await factory.CreateSkuAsync("Serialized");
        var owner = await factory.OwnerClientAsync();
        var productId = await factory.ScalarAsync<Guid>($"SELECT product_id FROM catalog.skus WHERE id = '{sku}'");
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/status", new { status = "Active", reason = "launch" }).OkJsonAsync();
        await factory.SetRetailPriceAsync(sku, 3000m);
        await factory.StockUpAsync(Knr, sku, 1, 1200m);
        var piece = await factory.ScalarAsync<Guid>($"SELECT id FROM inventory.inventory_items WHERE sku_id = '{sku}'");
        var cashiers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CashierAsync()));

        var results = await Task.WhenAll(cashiers.Select(c => SellAsync(c, sku, 3000m, [piece])));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(1);
        results.Where(r => !r.IsSuccessStatusCode).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM orders.order_lines WHERE sku_id = '{sku}'")).Should().Be(1);
        (await factory.ScalarAsync<string>($"SELECT status FROM inventory.inventory_items WHERE id = '{piece}'")).Should().Be("Sold");
    }

    [Fact]
    public async Task Simultaneous_sales_of_the_last_units_never_drive_stock_negative()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(500m);
        await factory.StockUpAsync(Knr, sku, 2, 200m);
        var cashiers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CashierAsync()));

        var results = await Task.WhenAll(cashiers.Select(c => SellAsync(c, sku, 500m)));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(2, "only two units exist");
        (await factory.StockAsync(Knr, sku)).Should().Be(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM orders.order_lines WHERE sku_id = '{sku}'")).Should().Be(2);
        (await factory.ScalarAsync<long>($"SELECT COALESCE(SUM(remaining_qty), 0) FROM inventory.cost_layers WHERE sku_id = '{sku}'")).Should().Be(0);
    }

    [Fact]
    public async Task Parallel_workers_send_each_email_and_create_each_notification_once()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(700m);
        await factory.StockUpAsync(Knr, sku, 3, 300m);
        var orders = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var (reseller, _, _) = await factory.FundedResellerAsync(2000m);
            orders.Add((await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)).OkJsonAsync())["order"]!["number"]!.GetValue<string>());
        }

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => factory.DispatchOutboxAsync()));
        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await NotificationJobs.SendDueEmailsAsync(scope.ServiceProvider, CancellationToken.None);
        }));
        await factory.SendEmailsAsync();

        foreach (var number in orders)
        {
            (await factory.ScalarAsync<long>($"SELECT count(*) FROM notifications.notifications WHERE title LIKE '%{number}%'")).Should().Be(1);
            factory.Emails.Sent.Count(m => m.Subject == $"Order {number} confirmed").Should().Be(1, "no email is sent twice");
        }
    }

    [Fact]
    public async Task Storage_outage_during_a_deposit_upload_records_nothing_and_says_try_again()
    {
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(0m);
        var reference = CheckoutHelpers.Utr();
        factory.FailStorageWrites = true;
        try
        {
            var response = await reseller.SubmitDepositRawAsync(1500m, reference);
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await response.ErrorCodeAsync()).Should().Be("PROOF_UPLOAD_FAILED");
        }
        finally
        {
            factory.FailStorageWrites = false;
        }
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM wallet.deposit_requests WHERE reseller_id = '{resellerId}'")).Should().Be(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM wallet.files WHERE object_key LIKE '{resellerId:N}%'")).Should().Be(0);

        // Once storage is back the same request goes through.
        (await reseller.SubmitDepositAsync(1500m, reference))["status"]!.GetValue<string>().Should().Be("Pending");
    }

    [Fact]
    public async Task Signing_in_on_several_devices_at_once_always_succeeds()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.BranchManager, Knr);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => factory.InternalLoginRawAsync(email, password)));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, "a parallel sign-in must never fail with a conflict (it would also leak that the password was right)");
    }

    [Fact]
    public async Task Parallel_wrong_passwords_are_all_counted_and_lock_the_account()
    {
        var (userId, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Knr);
        var guesses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => factory.InternalLoginRawAsync(email, "guess-" + i)));
        guesses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var afterwards = await factory.InternalLoginRawAsync(email, password);
        afterwards.StatusCode.Should().Be((HttpStatusCode)423, "five wrong attempts lock the account even when they arrive together");
        (await factory.ScalarAsync<int>($"SELECT failed_login_count FROM identity.users WHERE id = '{userId}'")).Should().Be(0);
    }
}
