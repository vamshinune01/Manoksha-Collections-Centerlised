using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>Backend authorization — hiding a screen is not authorization (SPEC §2, §24).</summary>
[Collection(ApiCollection.Name)]
public class AuthorizationTests(ManokshaApiFactory factory)
{
    private static readonly Guid Branch = DevelopmentSeedData.BranchKarimnagar;

    [Fact]
    public async Task Owner_sees_all_permissions_and_owner_flag()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var me = await (await owner.GetAsync("/api/v1/auth/me")).ReadJsonAsync();
        me["isOwner"]!.GetValue<bool>().Should().BeTrue();
        me["globalPermissions"]!.AsArray().Should().HaveCount(Permissions.All.Count);
    }

    [Fact]
    public async Task Employee_attempting_owner_only_actions_is_forbidden()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Branch);
        var sales = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        var victimRole = await factory.RoleIdAsync(SystemRoles.BranchManager);

        (await sales.PostAsJsonAsync($"/api/v1/admin/users/{factory.OwnerUserId}/role-assignments",
            new { roleId = victimRole, branchId = Branch, reason = "escalate" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.PutAsJsonAsync("/api/v1/admin/settings/reservation.minutes",
            new { value = 30, expectedVersion = 1, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.GetAsync("/api/v1/admin/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.PostAsJsonAsync("/api/v1/admin/roles",
            new { code = "SNEAKY", name = "x", scope = "Global", permissions = Array.Empty<string>(), reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sales.PostAsJsonAsync("/api/v1/admin/users",
            new { email = "x@y.zz", displayName = "x", reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Branch_manager_cannot_change_role_permissions_or_assign_roles()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.BranchManager, Branch);
        var manager = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        var salesRole = await factory.RoleIdAsync(SystemRoles.SalesEmployee);

        (await manager.PutAsJsonAsync($"/api/v1/admin/roles/{salesRole}/permissions",
            new { permissions = new[] { Permissions.Pos.PriceOverride }, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsJsonAsync($"/api/v1/admin/users/{factory.OwnerUserId}/role-assignments",
            new { roleId = salesRole, branchId = Branch, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_cannot_grant_owner_only_permission_to_another_role()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var response = await owner.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            code = "WALLET_CLERK_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            name = "Wallet clerk",
            scope = "Global",
            permissions = new[] { Permissions.Wallet.Adjust },
            reason = "test",
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ErrorCodeAsync()).Should().Be("PERMISSION_OWNER_ONLY");
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected_by_default()
    {
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Customer_token_cannot_reach_admin_endpoints()
    {
        var customer = factory.Authorized((await factory.RegisterCustomerAsync(ApiClient.NewMobile())).AccessToken);
        (await customer.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await customer.GetAsync("/api/v1/admin/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pos_audience_token_cannot_use_admin_routes()
    {
        var pos = factory.Authorized((await factory.LoginAsync(ManokshaApiFactory.OwnerEmail, ManokshaApiFactory.OwnerPassword, "pos")).AccessToken);
        (await pos.GetAsync("/api/v1/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await pos.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revoking_a_role_invalidates_existing_tokens_immediately()
    {
        var (userId, email, password) = await factory.CreateInternalUserAsync(SystemRoles.BranchManager, Branch);
        var managerToken = (await factory.LoginAsync(email, password)).AccessToken;
        var manager = factory.Authorized(managerToken);
        (await manager.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var user = await (await owner.GetAsync($"/api/v1/admin/users/{userId}")).ReadJsonAsync();
        var assignmentId = user["roles"]![0]!["assignmentId"]!.GetValue<Guid>();
        (await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/role-assignments/{assignmentId}/revoke", new { reason = "moved on" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await manager.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disabled_user_loses_access_and_sessions()
    {
        var (userId, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Branch);
        var tokens = await factory.LoginAsync(email, password);
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);

        (await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/status", new { status = "Disabled", reason = "left company" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await factory.Authorized(tokens.AccessToken).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.InternalLoginRawAsync(email, password)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Last_owner_cannot_be_disabled_or_lose_owner_role()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var self = await (await owner.GetAsync($"/api/v1/admin/users/{factory.OwnerUserId}")).ReadJsonAsync();
        var assignmentId = self["roles"]![0]!["assignmentId"]!.GetValue<Guid>();

        var revoke = await owner.PostAsJsonAsync($"/api/v1/admin/users/{factory.OwnerUserId}/role-assignments/{assignmentId}/revoke", new { reason = "oops" });
        revoke.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await revoke.ErrorCodeAsync()).Should().Be("LAST_OWNER");

        var disable = await owner.PostAsJsonAsync($"/api/v1/admin/users/{factory.OwnerUserId}/status", new { status = "Disabled", reason = "oops" });
        (await disable.ErrorCodeAsync()).Should().Be("CANNOT_DEACTIVATE_SELF");
    }

    [Fact]
    public async Task Branch_scoped_role_requires_branch_and_global_role_rejects_branch()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var (userId, _, _) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Branch);

        var noBranch = await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/role-assignments",
            new { roleId = await factory.RoleIdAsync(SystemRoles.InventoryEmployee), branchId = (Guid?)null, reason = "x" });
        (await noBranch.ErrorCodeAsync()).Should().Be("ROLE_SCOPE_MISMATCH");

        var withBranch = await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/role-assignments",
            new { roleId = await factory.RoleIdAsync(SystemRoles.Owner), branchId = Branch, reason = "x" });
        (await withBranch.ErrorCodeAsync()).Should().Be("ROLE_SCOPE_MISMATCH");
    }

    [Fact]
    public async Task Branch_permissions_are_scoped_to_the_assigned_branch()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.BranchManager, Branch);
        var manager = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        var me = await (await manager.GetAsync("/api/v1/auth/me")).ReadJsonAsync();

        me["isOwner"]!.GetValue<bool>().Should().BeFalse();
        me["globalPermissions"]!.AsArray().Should().BeEmpty();
        me["branchPermissions"]![Branch.ToString()]!.AsArray().Select(p => p!.GetValue<string>())
            .Should().Contain(Permissions.Transfers.Approve).And.NotContain(Permissions.Orders.Cancel);
        me["branchPermissions"]!.AsObject().Should().ContainSingle();
    }
}
