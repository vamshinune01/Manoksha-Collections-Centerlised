using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Manoksha.Modules.Identity.Infrastructure;

namespace Manoksha.IntegrationTests;

[Collection(ApiCollection.Name)]
public class InternalAuthTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task New_employee_must_change_temporary_password_first()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var email = $"n{Guid.NewGuid():N}"[..13] + "@test.manoksha";
        var created = await (await owner.PostAsJsonAsync("/api/v1/admin/users", new { email, displayName = "New", reason = "hire" })).ReadJsonAsync();

        var login = await (await factory.InternalLoginRawAsync(email, created["temporaryPassword"]!.GetValue<string>())).ReadJsonAsync();
        login["status"]!.GetValue<string>().Should().Be("PASSWORD_CHANGE_REQUIRED");
        login["tokens"].Should().BeNull();

        var weak = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/password/first-change",
            new { challengeToken = login["challengeToken"]!.GetValue<string>(), newPassword = "short" });
        (await weak.ErrorCodeAsync()).Should().Be("PASSWORD_POLICY");
    }

    [Fact]
    public async Task Wrong_password_is_generic_and_locks_after_repeated_failures()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchHyderabad);

        var unknown = await factory.InternalLoginRawAsync("nobody@test.manoksha", "Whatever-123");
        (await unknown.ErrorCodeAsync()).Should().Be("INVALID_CREDENTIALS");

        for (var i = 0; i < 5; i++)
        {
            (await (await factory.InternalLoginRawAsync(email, "Wrong-Passw0rd")).ErrorCodeAsync()).Should().Be("INVALID_CREDENTIALS");
        }
        var locked = await factory.InternalLoginRawAsync(email, password);
        locked.StatusCode.Should().Be((HttpStatusCode)423);

        factory.Clock.Advance(TimeSpan.FromMinutes(16));
        (await factory.InternalLoginRawAsync(email, password)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_session()
    {
        var first = await factory.LoginOwnerAsync();
        var anonymous = factory.CreateClient();

        var rotated = await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await rotated.ReadJsonAsync();
        var secondRefresh = second["refreshToken"]!.GetValue<string>();
        var secondAccess = second["accessToken"]!.GetValue<string>();

        // Replay of the already-used token = theft signal → whole session revoked.
        (await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = secondRefresh })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.Authorized(secondAccess).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_access_immediately()
    {
        var tokens = await factory.LoginOwnerAsync();
        (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = tokens.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await factory.Authorized(tokens.AccessToken).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mfa_required_role_must_enroll_then_verify_with_replay_protection()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var roles = await (await owner.GetAsync("/api/v1/admin/roles")).ReadJsonAsync();
        if (!roles.AsArray().Any(r => r!["code"]!.GetValue<string>() == ManokshaApiFactory.MfaTestRole))
        {
            (await owner.PostAsJsonAsync("/api/v1/admin/roles", new
            {
                code = ManokshaApiFactory.MfaTestRole, name = "MFA test", scope = "Global",
                permissions = new[] { Permissions.Reports.View }, reason = "test",
            })).StatusCode.Should().Be(HttpStatusCode.Created);
        }
        var (_, email, password) = await factory.CreateInternalUserAsync(ManokshaApiFactory.MfaTestRole, null);

        var login = await (await factory.InternalLoginRawAsync(email, password)).ReadJsonAsync();
        login["status"]!.GetValue<string>().Should().Be("MFA_ENROLLMENT_REQUIRED");
        var challenge = login["challengeToken"]!.GetValue<string>();

        var start = await (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/mfa/enroll/start", new { challengeToken = challenge })).ReadJsonAsync();
        var secret = start["secret"]!.GetValue<string>();
        start["otpAuthUri"]!.GetValue<string>().Should().StartWith("otpauth://totp/");

        var code = Totp.Compute(Totp.Base32Decode(secret), Totp.TimeStep(factory.Clock.UtcNow));
        var confirm = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/mfa/enroll/confirm", new { challengeToken = challenge, code });
        var confirmed = await confirm.ReadJsonAsync();
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, confirmed.ToJsonString());
        confirmed["status"]!.GetValue<string>().Should().Be("AUTHENTICATED");

        // Next sign-in requires the second factor; the same time-step code cannot be replayed.
        var again = await (await factory.InternalLoginRawAsync(email, password)).ReadJsonAsync();
        again["status"]!.GetValue<string>().Should().Be("MFA_REQUIRED");
        var replay = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/mfa/verify",
            new { challengeToken = again["challengeToken"]!.GetValue<string>(), code });
        (await replay.ErrorCodeAsync()).Should().Be("MFA_CODE_INVALID");

        factory.Clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
        var fresh = Totp.Compute(Totp.Base32Decode(secret), Totp.TimeStep(factory.Clock.UtcNow));
        var verified = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/internal/mfa/verify",
            new { challengeToken = again["challengeToken"]!.GetValue<string>(), code = fresh });
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
