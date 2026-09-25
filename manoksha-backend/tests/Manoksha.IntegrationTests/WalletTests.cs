using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Prepaid wallet ledger, deposits and adjustments (SPEC §16, §17; ADR-001 §20).</summary>
[Collection(ApiCollection.Name)]
public class WalletTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Deposit_needs_reference_and_screenshot_and_credits_once_after_owner_approval()
    {
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(0m);
        (await (await reseller.SubmitDepositRawAsync(5000m, null)).ErrorCodeAsync()).Should().Be("DEPOSIT_REFERENCE_REQUIRED");
        (await (await reseller.SubmitDepositRawAsync(5000m, CheckoutHelpers.Utr(), withProof: false)).ErrorCodeAsync()).Should().Be("DEPOSIT_PROOF_REQUIRED");
        (await (await reseller.SubmitDepositRawAsync(5000m, CheckoutHelpers.Utr(), proof: "not an image"u8.ToArray())).ErrorCodeAsync()).Should().Be("DEPOSIT_PROOF_TYPE_INVALID");

        var utr = CheckoutHelpers.Utr();
        var deposit = await reseller.SubmitDepositAsync(5000m, utr);
        deposit["status"]!.GetValue<string>().Should().Be("Pending");
        (await reseller.BalanceAsync()).Should().Be(0m, "nothing is credited before Owner approval");
        (await (await reseller.SubmitDepositRawAsync(5000m, utr.ToLowerInvariant())).ErrorCodeAsync()).Should().Be("DEPOSIT_REFERENCE_USED");

        var owner = await factory.OwnerClientAsync();
        (await owner.GetAsync($"/api/v1/admin/wallet/deposits/{deposit["id"]}/proof")).Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        var approved = await owner.PostAsJsonAsync($"/api/v1/admin/wallet/deposits/{deposit["id"]}/approve", new { note = "Verified in PhonePe business app" }).OkJsonAsync();
        approved["status"]!.GetValue<string>().Should().Be("Credited");
        (await (await owner.PostAsJsonAsync($"/api/v1/admin/wallet/deposits/{deposit["id"]}/approve", new { note = "again" })).ErrorCodeAsync()).Should().Be("DEPOSIT_ALREADY_DECIDED");

        (await reseller.BalanceAsync()).Should().Be(5000m);
        var ledger = await owner.GetAsync($"/api/v1/admin/wallet/resellers/{resellerId}/ledger").OkJsonAsync();
        var entry = ledger["entries"]!.AsArray().Single()!;
        entry["type"]!.GetValue<string>().Should().Be("Deposit");
        entry["balanceBefore"]!.GetValue<decimal>().Should().Be(0m);
        entry["balanceAfter"]!.GetValue<decimal>().Should().Be(5000m);
    }

    [Fact]
    public async Task Concurrent_double_approval_creates_a_single_credit()
    {
        var (reseller, _, _) = await factory.FundedResellerAsync(0m);
        var deposit = await reseller.SubmitDepositAsync(1200m, CheckoutHelpers.Utr());
        var owner = await factory.OwnerClientAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            owner.PostAsJsonAsync($"/api/v1/admin/wallet/deposits/{deposit["id"]}/approve", new { note = "click" })));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        (await reseller.BalanceAsync()).Should().Be(1200m);
    }

    [Fact]
    public async Task Rejected_deposit_is_kept_with_proof_and_never_credits()
    {
        var (reseller, _, _) = await factory.FundedResellerAsync(0m);
        var deposit = await reseller.SubmitDepositAsync(800m, CheckoutHelpers.Utr());
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/wallet/deposits/{deposit["id"]}/reject", new { reason = "Amount not received" }).OkJsonAsync();
        (await reseller.BalanceAsync()).Should().Be(0m);
        var mine = await reseller.GetAsync("/api/v1/reseller/wallet/deposits").OkJsonAsync();
        mine[0]!["status"]!.GetValue<string>().Should().Be("Rejected");
        mine[0]!["reviewNote"]!.GetValue<string>().Should().Be("Amount not received");
        (await owner.GetAsync($"/api/v1/admin/wallet/deposits/{deposit["id"]}/proof")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Manual_adjustment_is_owner_only_and_can_never_go_negative()
    {
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(300m);
        var owner = await factory.OwnerClientAsync();
        var overdraw = await owner.PostAsJsonAsync($"/api/v1/admin/wallet/resellers/{resellerId}/adjustments", new { direction = "Debit", amount = 300.01m, reason = "x" });
        (await overdraw.ErrorCodeAsync()).Should().Be("INSUFFICIENT_WALLET_BALANCE");
        await owner.PostAsJsonAsync($"/api/v1/admin/wallet/resellers/{resellerId}/adjustments", new { direction = "Debit", amount = 100m, reason = "courier charge recovery" }).OkJsonAsync();
        (await reseller.BalanceAsync()).Should().Be(200m);

        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        (await manager.PostAsJsonAsync($"/api/v1/admin/wallet/resellers/{resellerId}/adjustments", new { direction = "Credit", amount = 1m, reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsJsonAsync($"/api/v1/admin/wallet/deposits/{Guid.NewGuid()}/approve", new { note = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("UPDATE wallet.ledger_entries SET amount = 1")]
    [InlineData("DELETE FROM wallet.ledger_entries")]
    public async Task Ledger_entries_cannot_be_edited_or_deleted(string sql)
    {
        await factory.FundedResellerAsync(10m);
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        (await FluentActions.Awaiting(() => cmd.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Frozen_reseller_cannot_submit_deposits()
    {
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(0m);
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{resellerId}/status", new { status = "Frozen", reason = "review" }).OkJsonAsync();
        (await (await reseller.SubmitDepositRawAsync(100m, CheckoutHelpers.Utr())).ErrorCodeAsync()).Should().Be("RESELLER_CANNOT_TRANSACT");
    }
}
