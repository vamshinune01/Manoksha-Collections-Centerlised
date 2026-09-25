using System.Net;
using System.Net.Http.Json;
using Manoksha.IntegrationTests.Infrastructure;
using Manoksha.Integrations.Sms;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.IntegrationTests;

/// <summary>Customer/reseller mobile OTP (SPEC §5.2, §5.3; ADR-001 §2).</summary>
[Collection(ApiCollection.Name)]
public class OtpAuthTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Customer_registers_with_mobile_otp_and_email()
    {
        var mobile = ApiClient.NewMobile();
        var tokens = await factory.RegisterCustomerAsync(mobile);
        var me = await (await factory.Authorized(tokens.AccessToken).GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        me["accountType"]!.GetValue<string>().Should().Be("Customer");
        me["audience"]!.GetValue<string>().Should().Be("customer");
        me["mobile"]!.GetValue<string>().Should().Be(mobile);

        // Second sign-in goes straight to AUTHENTICATED.
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        var verify = await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = ApiClient.LastOtpFor(mobile) })).ReadJsonAsync();
        verify["status"]!.GetValue<string>().Should().Be("AUTHENTICATED");
    }

    [Fact]
    public async Task Otp_is_single_use()
    {
        var mobile = ApiClient.NewMobile();
        await factory.RegisterCustomerAsync(mobile);
        var code = ApiClient.LastOtpFor(mobile);
        var reuse = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code });
        (await reuse.ErrorCodeAsync()).Should().Be("OTP_INVALID_OR_EXPIRED");
    }

    [Fact]
    public async Task Wrong_codes_exhaust_the_challenge()
    {
        var mobile = ApiClient.NewMobile();
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        var correct = ApiClient.LastOtpFor(mobile);
        var wrong = correct == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
        {
            (await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = wrong })).ErrorCodeAsync())
                .Should().Be("OTP_INVALID_OR_EXPIRED");
        }
        (await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = wrong })).ErrorCodeAsync())
            .Should().Be("OTP_ATTEMPTS_EXCEEDED");
        // Even the right code no longer works.
        (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = correct })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Otp_expires()
    {
        var mobile = ApiClient.NewMobile();
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        var verify = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = ApiClient.LastOtpFor(mobile) });
        (await verify.ErrorCodeAsync()).Should().Be("OTP_INVALID_OR_EXPIRED");
    }

    [Fact]
    public async Task Resend_cooldown_is_enforced()
    {
        var mobile = ApiClient.NewMobile();
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        var again = await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" });
        again.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var body = await again.ReadJsonAsync();
        body["code"]!.GetValue<string>().Should().Be("OTP_RESEND_TOO_SOON");
        body["retryAfterSeconds"]!.GetValue<int>().Should().BePositive();
    }

    [Fact]
    public async Task Sms_failure_fails_login_without_fallback()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/otp/request",
            new { mobile = ManokshaApiFactory.FailingSmsNumber, context = "customer" });
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.ErrorCodeAsync()).Should().Be("OTP_DELIVERY_FAILED");
    }

    [Fact]
    public async Task Reseller_cannot_self_register_and_unknown_number_gets_no_sms()
    {
        var mobile = ApiClient.NewMobile();
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "reseller" })).StatusCode.Should().Be(HttpStatusCode.OK);
        FakeSmsSender.LastMessageTo(mobile).Should().BeNull("no reseller account exists for this number");
        var verify = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "reseller", code = "123456" });
        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Same_mobile_customer_and_reseller_are_separate_identities()
    {
        var mobile = ApiClient.NewMobile();
        var customerTokens = await factory.RegisterCustomerAsync(mobile);

        // Reseller creation/activation arrives in Phase 4; create an ACTIVE reseller identity directly for this test.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ManokshaDbContext>();
            var reseller = User.CreatePendingReseller(mobile, "Reseller Shop", null, DateTimeOffset.UtcNow, factory.OwnerUserId);
            reseller.Activate(DateTimeOffset.UtcNow);
            db.Add(reseller);
            await db.SaveChangesAsync();
        }

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "reseller" })).EnsureSuccessStatusCode();
        var resellerLogin = await (await client.PostAsJsonAsync("/api/v1/auth/otp/verify",
            new { mobile, context = "reseller", code = ApiClient.LastOtpFor(mobile) })).ReadJsonAsync();
        resellerLogin["status"]!.GetValue<string>().Should().Be("AUTHENTICATED");
        var resellerTokens = ApiClient.ToTokens(resellerLogin);

        var customerMe = await (await factory.Authorized(customerTokens.AccessToken).GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        var resellerMe = await (await factory.Authorized(resellerTokens.AccessToken).GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        customerMe["userId"]!.GetValue<Guid>().Should().NotBe(resellerMe["userId"]!.GetValue<Guid>());
        customerMe["audience"]!.GetValue<string>().Should().Be("customer");
        resellerMe["audience"]!.GetValue<string>().Should().Be("reseller");
        resellerMe["accountType"]!.GetValue<string>().Should().Be("Reseller");

        // A code issued for the customer context cannot be used in the reseller context.
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        var crossContext = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "reseller", code = ApiClient.LastOtpFor(mobile) });
        crossContext.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Invalid_mobile_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile = "12345", context = "customer" });
        (await response.ErrorCodeAsync()).Should().Be("INVALID_MOBILE_NUMBER");
    }
}
