using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Account onboarding: first-run Owner setup page (closed once an Owner exists; setup code required) and staff invite links
/// (Owner-created accounts only — no open staff sign-up, SPEC §5.1).
/// </summary>
[Collection(ApiCollection.Name)]
public class OnboardingTests(ManokshaApiFactory factory)
{
    private static object Setup(string code, string email) => new { setupCode = code, email, fullName = "New Owner", password = "Owner-Setup-Passw0rd" };

    [Fact]
    public async Task Owner_setup_page_needs_the_code_and_closes_once_an_owner_exists()
    {
        var anonymous = factory.CreateClient();
        var status = await anonymous.GetAsync("/api/v1/setup/status").OkJsonAsync();
        status["ownerExists"]!.GetValue<bool>().Should().BeTrue();
        status["setupEnabled"]!.GetValue<bool>().Should().BeTrue();

        (await (await anonymous.PostAsJsonAsync("/api/v1/setup/owner", Setup("wrong-code-000000000", "x@example.com"))).ErrorCodeAsync())
            .Should().Be("SETUP_CODE_INVALID");
        (await (await anonymous.PostAsJsonAsync("/api/v1/setup/owner", Setup(ManokshaApiFactory.OwnerSetupCode, "x@example.com"))).ErrorCodeAsync())
            .Should().Be("SETUP_COMPLETE", "an Owner already exists, so setup is closed");

        // Simulate a fresh database without an Owner: the page then creates exactly one Owner.
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        var revokedAt = DateTimeOffset.UtcNow;
        await Exec(c, $"UPDATE identity.user_role_assignments SET revoked_at = '{revokedAt:O}' WHERE user_id = '{factory.OwnerUserId}' AND revoked_at IS NULL");
        var email = $"owner-{Guid.NewGuid():N}"[..20] + "@example.com";
        try
        {
            (await anonymous.GetAsync("/api/v1/setup/status").OkJsonAsync())["ownerExists"]!.GetValue<bool>().Should().BeFalse();
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => anonymous.PostAsJsonAsync("/api/v1/setup/owner",
                Setup(ManokshaApiFactory.OwnerSetupCode, i == 0 ? email : $"other{i}-{email}"))));
            results.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1, "only one Owner can ever be created by setup");
            var created = results.Single(r => r.StatusCode == HttpStatusCode.NoContent);
            var ownerEmails = await factory.ColumnAsync(
                "SELECT u.email FROM identity.users u JOIN identity.user_role_assignments a ON a.user_id = u.id JOIN identity.roles r ON r.id = a.role_id WHERE r.code = 'OWNER' AND a.revoked_at IS NULL");
            ownerEmails.Should().ContainSingle();
            var tokens = await factory.LoginAsync(ownerEmails[0], "Owner-Setup-Passw0rd");
            (await factory.Authorized(tokens.AccessToken).GetAsync("/api/v1/auth/me").OkJsonAsync())["isOwner"]!.GetValue<bool>().Should().BeTrue();
            created.Should().NotBeNull();
        }
        finally
        {
            await Exec(c, "UPDATE identity.user_role_assignments SET revoked_at = now() WHERE revoked_at IS NULL AND role_id = (SELECT id FROM identity.roles WHERE code = 'OWNER')");
            await Exec(c, $"UPDATE identity.user_role_assignments SET revoked_at = NULL WHERE user_id = '{factory.OwnerUserId}' AND revoked_at = '{revokedAt:O}'");
        }
        (await anonymous.GetAsync("/api/v1/setup/status").OkJsonAsync())["ownerExists"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Staff_invite_link_lets_the_person_set_their_own_password_once()
    {
        var owner = await factory.OwnerClientAsync();
        var email = $"inv-{Guid.NewGuid():N}"[..20] + "@example.com";
        var created = await owner.PostAsJsonAsync("/api/v1/admin/users", new { email, displayName = "Ravi Teja", mobile = (string?)null, reason = "New manager", sendInvite = true })
            .OkJsonAsync(HttpStatusCode.Created);
        created["temporaryPassword"].Should().BeNull("an invited person chooses their own password");
        var token = created["inviteToken"]!.GetValue<string>();
        created["inviteExpiresAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(72), TimeSpan.FromHours(1));
        (await factory.InternalLoginRawAsync(email, "anything-1")).StatusCode.Should().NotBe(HttpStatusCode.OK, "no password is set before the invite is accepted");

        var anonymous = factory.CreateClient();
        var invite = await anonymous.GetAsync($"/api/v1/invitations/{token}").OkJsonAsync();
        invite["displayName"]!.GetValue<string>().Should().Be("Ravi Teja");
        invite["emailMasked"]!.GetValue<string>().Should().Contain("@example.com").And.NotBe(email);
        (await (await anonymous.PostAsJsonAsync($"/api/v1/invitations/{token}/accept", new { password = "short" })).ErrorCodeAsync()).Should().Be("PASSWORD_POLICY");
        (await anonymous.PostAsJsonAsync($"/api/v1/invitations/{token}/accept", new { password = "Ravi-Chosen-Passw0rd" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await factory.LoginAsync(email, "Ravi-Chosen-Passw0rd");

        (await anonymous.PostAsJsonAsync($"/api/v1/invitations/{token}/accept", new { password = "Another-Passw0rd" })).StatusCode.Should().Be(HttpStatusCode.Gone, "links are single-use");
        (await anonymous.GetAsync($"/api/v1/invitations/not-a-real-token")).StatusCode.Should().Be(HttpStatusCode.Gone);

        // A new link revokes older unused ones; only user managers can issue links.
        var userId = created["userId"]!.GetValue<Guid>();
        var first = await owner.PostAsync($"/api/v1/admin/users/{userId}/invitations", null).OkJsonAsync();
        var second = await owner.PostAsync($"/api/v1/admin/users/{userId}/invitations", null).OkJsonAsync();
        (await anonymous.GetAsync($"/api/v1/invitations/{first["token"]}")).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await anonymous.GetAsync($"/api/v1/invitations/{second["token"]}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        (await manager.PostAsync($"/api/v1/admin/users/{userId}/invitations", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Employees_can_be_invited_too()
    {
        var owner = await factory.OwnerClientAsync();
        var email = $"emp-{Guid.NewGuid():N}"[..20] + "@example.com";
        var r = await owner.PostAsJsonAsync("/api/v1/admin/employees", new
        {
            fullName = "Sita Devi", email, existingUserId = (Guid?)null, mobile = (string?)null, assignedBranchId = DevelopmentSeedData.BranchKarimnagar,
            joinedOn = (DateOnly?)null, reason = "New sales staff", sendInvite = true,
        }).OkJsonAsync(HttpStatusCode.Created);
        r["temporaryPassword"].Should().BeNull();
        var token = r["inviteToken"]!.GetValue<string>();
        (await factory.CreateClient().PostAsJsonAsync($"/api/v1/invitations/{token}/accept", new { password = "Sita-Chosen-Passw0rd" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await factory.LoginAsync(email, "Sita-Chosen-Passw0rd");
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
}
