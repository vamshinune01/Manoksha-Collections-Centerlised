using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Manoksha.Modules.Wallet;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Failure handling and financial integrity around the wallet (SPEC §16, §32, §33).</summary>
[Collection(ApiCollection.Name)]
public class WalletSafetyTests(ManokshaApiFactory factory)
{
    private static readonly Guid Knr = DevelopmentSeedData.BranchKarimnagar;

    private async Task ExecSqlAsync(string sql)
    {
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Database_failure_mid_wallet_order_rolls_back_everything()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(1000m);
        await factory.StockUpAsync(Knr, sku, 3, 400m);
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(5000m, 0m);
        var owner = await factory.OwnerClientAsync();

        // A real database error raised while order lines are inserted — i.e. AFTER stock was committed and the wallet was debited.
        var fn = "fail_" + Guid.NewGuid().ToString("N")[..8];
        await ExecSqlAsync($"""
            CREATE FUNCTION orders.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'simulated database failure'; END; $$;
            CREATE TRIGGER {fn} BEFORE INSERT ON orders.order_lines FOR EACH ROW WHEN (NEW.sku_id = '{sku}') EXECUTE FUNCTION orders.{fn}();
            """);
        var key = Guid.NewGuid().ToString("N");
        try
        {
            var failed = await reseller.CheckoutAsync(key, CheckoutHelpers.Delivery(), (sku, 2));
            failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await ExecSqlAsync($"DROP TRIGGER {fn} ON orders.order_lines; DROP FUNCTION orders.{fn}();");
        }

        (await reseller.BalanceAsync()).Should().Be(5000m, "the debit was rolled back");
        (await factory.StockAsync(Knr, sku)).Should().Be(3, "the stock commit was rolled back");
        (await reseller.GetAsync("/api/v1/reseller/orders").OkJsonAsync()).AsArray().Should().BeEmpty();
        var ledger = await owner.GetAsync($"/api/v1/admin/wallet/resellers/{resellerId}/ledger").OkJsonAsync();
        ledger["entries"]!.AsArray().Should().ContainSingle("only the funding credit exists");
        (await factory.LayersAsync(Knr, sku)).Sum(l => l.Remaining).Should().Be(3, "FIFO cost consumption was rolled back");

        // The idempotency key was not burned by the failed attempt: the same click can be retried and succeeds once.
        var retry = await reseller.CheckoutAsync(key, CheckoutHelpers.Delivery(), (sku, 2)).OkJsonAsync();
        retry["outcome"]!.GetValue<string>().Should().Be("CONFIRMED");
        (await reseller.BalanceAsync()).Should().Be(2900m);
    }

    [Fact]
    public async Task Reversal_is_a_separate_linked_credit_and_happens_at_most_once()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(500m);
        await factory.StockUpAsync(Knr, sku, 2, 100m);
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(2000m, 0m);
        var order = (await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(), (sku, 1)).OkJsonAsync())["order"]!;
        var orderId = order["id"]!.GetValue<Guid>();
        (await reseller.BalanceAsync()).Should().Be(1400m);

        async Task<LedgerPosting> ReverseAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var wallets = scope.ServiceProvider.GetRequiredService<IWallets>();
            return await uow.ExecuteInTransactionAsync(ct => wallets.ReverseOrderDebitAsync(orderId, "Order cancelled by Owner (test)", ct));
        }

        // Concurrent reversal attempts: exactly one succeeds.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            try
            {
                await ReverseAsync();
                return "OK";
            }
            catch (BusinessRuleException ex)
            {
                return ex.Code;
            }
        }));
        attempts.Count(a => a == "OK").Should().Be(1);
        attempts.Where(a => a != "OK").Should().OnlyContain(a => a == "WALLET_DEBIT_ALREADY_REVERSED");

        (await reseller.BalanceAsync()).Should().Be(2000m);
        var owner = await factory.OwnerClientAsync();
        var entries = (await owner.GetAsync($"/api/v1/admin/wallet/resellers/{resellerId}/ledger").OkJsonAsync())["entries"]!.AsArray();
        var debit = entries.Single(e => e!["type"]!.GetValue<string>() == "Debit")!;
        var reversal = entries.Single(e => e!["type"]!.GetValue<string>() == "Reversal")!;
        reversal["direction"]!.GetValue<string>().Should().Be("Credit");
        reversal["amount"]!.GetValue<decimal>().Should().Be(600m);
        reversal["reversesEntryId"]!.GetValue<Guid>().Should().Be(debit["id"]!.GetValue<Guid>());
        reversal["orderId"]!.GetValue<Guid>().Should().Be(orderId);
        debit["amount"]!.GetValue<decimal>().Should().Be(600m, "the original debit is never changed");
    }

    [Fact]
    public async Task Integrity_check_is_healthy_after_all_activity_and_detects_tampering()
    {
        var owner = await factory.OwnerClientAsync();
        var before = await owner.GetAsync("/api/v1/admin/wallet/integrity").OkJsonAsync();
        before["walletsChecked"]!.GetValue<int>().Should().BePositive();
        before["issues"]!.AsArray().Should().BeEmpty("every wallet balance must equal its ledger after all test activity");

        var (_, resellerId, _) = await factory.FundedResellerAsync(100m);
        await ExecSqlAsync($"UPDATE wallet.wallets SET balance = balance + 50 WHERE reseller_id = '{resellerId}'");
        try
        {
            var report = await owner.GetAsync("/api/v1/admin/wallet/integrity").OkJsonAsync();
            var issue = report["issues"]!.AsArray().Single()!;
            issue["resellerId"]!.GetValue<Guid>().Should().Be(resellerId);
            issue["problem"]!.GetValue<string>().Should().Be("CACHED_BALANCE_DIFFERS_FROM_LEDGER");
            issue["cachedBalance"]!.GetValue<decimal>().Should().Be(150m);
            issue["ledgerBalance"]!.GetValue<decimal>().Should().Be(100m);

            // The scheduled job records findings in the audit log (and raises a CRITICAL event).
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                (await WalletJobs.RunIntegrityCheckAsync(scope.ServiceProvider, CancellationToken.None)).Should().Be(1);
            }
            var audit = await owner.GetAsync("/api/v1/admin/audit?action=wallet.integrity.mismatch").OkJsonAsync();
            audit["items"]!.AsArray().Should().NotBeEmpty();
        }
        finally
        {
            await ExecSqlAsync($"UPDATE wallet.wallets SET balance = balance - 50 WHERE reseller_id = '{resellerId}'");
        }
        (await owner.GetAsync("/api/v1/admin/wallet/integrity").OkJsonAsync())["issues"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Owner_sees_every_resellers_customers_but_others_cannot()
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(100m);
        await factory.StockUpAsync(Knr, sku, 2, 10m);
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(1000m);
        var customerMobile = ApiClient.NewMobile();
        await reseller.CheckoutAsync(null, CheckoutHelpers.Delivery(customerMobile), (sku, 1)).OkJsonAsync();

        var owner = await factory.OwnerClientAsync();
        var list = await owner.GetAsync($"/api/v1/admin/resellers/{resellerId}/customers").OkJsonAsync();
        list.AsArray().Should().ContainSingle(c => c!["details"]!["mobile"]!.GetValue<string>() == customerMobile);

        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Knr);
        (await manager.GetAsync($"/api/v1/admin/resellers/{resellerId}/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reseller.GetAsync($"/api/v1/admin/resellers/{resellerId}/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reseller_can_add_and_edit_their_own_customers_only()
    {
        var a = await factory.FundedResellerAsync(0m);
        var b = await factory.FundedResellerAsync(0m);
        var created = await a.Client.PostAsJsonAsync("/api/v1/reseller/customers", new { details = CheckoutHelpers.Delivery() }).OkJsonAsync();
        var id = created["id"]!.GetValue<Guid>();
        var updated = await a.Client.PutAsJsonAsync($"/api/v1/reseller/customers/{id}", new
        {
            details = new { name = "Anitha R", mobile = created["details"]!["mobile"]!.GetValue<string>(), addressLine = "New street", city = "Warangal", state = "Telangana", pin = "506002" },
        }).OkJsonAsync();
        updated["details"]!["name"]!.GetValue<string>().Should().Be("Anitha R");
        (await b.Client.PutAsJsonAsync($"/api/v1/reseller/customers/{id}", new { details = CheckoutHelpers.Delivery() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
