using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Reseller onboarding, activation, status sign-in rules and commercial terms (SPEC §5.3, §6, §27.6; ADR-001 §17–19).</summary>
[Collection(ApiCollection.Name)]
public class ResellerTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Owner_onboards_reseller_pending_with_zero_wallet_then_otp_activates()
    {
        var mobile = ApiClient.NewMobile();
        var created = await factory.CreateResellerAsync(mobile, 12m);
        created["status"]!.GetValue<string>().Should().Be("Pending");
        created["resellerNumber"]!.GetValue<string>().Should().MatchRegex("^RS-\\d{5}$");
        created["walletBalance"]!.GetValue<decimal>().Should().Be(0m);
        created["currentTerms"]!["version"]!.GetValue<int>().Should().Be(1);
        created["currentTerms"]!["discountPct"]!.GetValue<decimal>().Should().Be(12m);
        var resellerId = created["id"]!.GetValue<Guid>();

        var reseller = await factory.ResellerClientAsync(mobile);
        var me = await reseller.GetAsync("/api/v1/reseller/me").OkJsonAsync();
        me["status"]!.GetValue<string>().Should().Be("Active");
        me["canPlaceOrders"]!.GetValue<bool>().Should().BeTrue();

        var owner = await factory.OwnerClientAsync();
        var detail = await owner.GetAsync($"/api/v1/admin/resellers/{resellerId}").OkJsonAsync();
        detail["activatedAt"].Should().NotBeNull();
        detail["statusHistory"]!.AsArray().Select(s => s!["toStatus"]!.GetValue<string>()).Should().Equal("Active", "Pending");
        var audit = await owner.GetAsync($"/api/v1/admin/audit?entityType=Reseller&entityId={resellerId}").OkJsonAsync();
        audit["items"]!.AsArray().Select(a => a!["action"]!.GetValue<string>()).Should().Contain(["resellers.reseller.created", "resellers.reseller.activated"]);
    }

    [Fact]
    public async Task Reseller_mobile_is_unique_and_only_the_owner_can_onboard()
    {
        var mobile = ApiClient.NewMobile();
        await factory.CreateResellerAsync(mobile);
        var owner = await factory.OwnerClientAsync();
        var duplicate = await owner.PostAsJsonAsync("/api/v1/admin/resellers", new
        {
            contactName = "Dup", mobile, email = "d@example.com", addressLine = "x", city = "x", state = "x", pin = "500001", resellerDiscountPct = 5m, reason = "x",
        });
        (await duplicate.ErrorCodeAsync()).Should().Be("RESELLER_MOBILE_EXISTS");

        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        (await manager.PostAsJsonAsync("/api/v1/admin/resellers", new { contactName = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_rules_frozen_read_only_suspended_locked_out_closed_read_only()
    {
        var mobile = ApiClient.NewMobile();
        var id = (await factory.CreateResellerAsync(mobile))["id"]!.GetValue<Guid>();
        var reseller = await factory.ResellerClientAsync(mobile);
        var owner = await factory.OwnerClientAsync();
        async Task SetStatus(string status) =>
            await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{id}/status", new { status, reason = "test " + status }).OkJsonAsync();

        await SetStatus("Frozen");
        var frozen = await reseller.GetAsync("/api/v1/reseller/me").OkJsonAsync();
        frozen["canPlaceOrders"]!.GetValue<bool>().Should().BeFalse();
        (await factory.ResellerOtpLoginRawAsync(mobile)).StatusCode.Should().Be(HttpStatusCode.OK, "FROZEN resellers keep read-only sign-in");

        await SetStatus("Active");
        await SetStatus("Suspended");
        (await reseller.GetAsync("/api/v1/reseller/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "suspension revokes sessions");
        (await (await factory.ResellerOtpLoginRawAsync(mobile)).ErrorCodeAsync()).Should().Be("RESELLER_SUSPENDED");

        await SetStatus("Closed");
        var closedLogin = await factory.ResellerClientAsync(mobile);
        var closed = await closedLogin.GetAsync("/api/v1/reseller/me").OkJsonAsync();
        closed["status"]!.GetValue<string>().Should().Be("Closed");
        closed["canPlaceOrders"]!.GetValue<bool>().Should().BeFalse();

        (await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{id}/status", new { status = "Active", reason = "x" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Pending_reseller_can_be_closed_and_then_cannot_activate()
    {
        var mobile = ApiClient.NewMobile();
        var id = (await factory.CreateResellerAsync(mobile))["id"]!.GetValue<Guid>();
        var owner = await factory.OwnerClientAsync();
        (await (await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{id}/status", new { status = "Frozen", reason = "x" })).ErrorCodeAsync())
            .Should().Be("RESELLER_STATUS_INVALID", "a pending reseller only becomes active through OTP verification");
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{id}/status", new { status = "Closed", reason = "created by mistake" }).OkJsonAsync();
        (await factory.ResellerOtpLoginRawAsync(mobile)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Commercial_terms_are_versioned_audited_and_immutable()
    {
        var mobile = ApiClient.NewMobile();
        var id = (await factory.CreateResellerAsync(mobile, 10m))["id"]!.GetValue<Guid>();
        var owner = await factory.OwnerClientAsync();
        var updated = await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{id}/commercial-terms",
            new { resellerDiscountPct = 12.5m, notes = "Festival volume", reason = "Higher monthly volume" }).OkJsonAsync();
        updated["currentTerms"]!["version"]!.GetValue<int>().Should().Be(2);
        updated["termsHistory"]!.AsArray().Should().HaveCount(2);

        var reseller = await factory.ResellerClientAsync(mobile);
        var mine = await reseller.GetAsync("/api/v1/reseller/commercial-terms").OkJsonAsync();
        mine.AsArray().Select(t => t!["discountPct"]!.GetValue<decimal>()).Should().Equal(12.5m, 10m);
        mine[0]!["isCurrent"]!.GetValue<bool>().Should().BeTrue();

        var audit = await owner.GetAsync($"/api/v1/admin/audit?action=resellers.commercial_terms.changed&entityId={id}").OkJsonAsync();
        var entry = audit["items"]!.AsArray().Single()!;
        entry["before"]!.GetValue<string>().Should().Contain("\"discountPct\": 10");
        entry["after"]!.GetValue<string>().Should().Contain("\"discountPct\": 12.5");

        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE resellers.commercial_terms SET discount_pct = 50", c);
        (await FluentActions.Awaiting(() => cmd.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Resellers_see_only_their_own_data()
    {
        var a = await factory.NewActiveResellerAsync();
        var bMobile = ApiClient.NewMobile();
        await factory.CreateResellerAsync(bMobile);
        var meA = await a.GetAsync("/api/v1/reseller/me").OkJsonAsync();
        meA["mobile"]!.GetValue<string>().Should().NotBe(bMobile);
        (await a.GetAsync("/api/v1/admin/resellers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var customer = factory.Authorized((await factory.RegisterCustomerAsync(ApiClient.NewMobile())).AccessToken);
        (await customer.GetAsync("/api/v1/reseller/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/reseller/catalog")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
