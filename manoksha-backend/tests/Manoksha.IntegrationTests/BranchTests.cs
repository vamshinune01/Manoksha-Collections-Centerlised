using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>Branch master and Owner-controlled fulfillment priority (SPEC §11).</summary>
[Collection(ApiCollection.Name)]
public class BranchTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Seeded_priority_is_karimnagar_hyderabad_mulugu_in_creation_order()
    {
        var owner = await factory.OwnerClientAsync();
        var history = await owner.GetAsync("/api/v1/admin/fulfillment-priority/history").OkJsonAsync();
        // The first full list (version 3) is exactly the SPEC example.
        var v3 = history.AsArray().Single(v => v!["version"]!.GetValue<int>() == 3)!;
        v3["entries"]!.AsArray().Select(e => e!["branchCode"]!.GetValue<string>()).Should().Equal("KNR", "HYD", "MLG");
    }

    [Fact]
    public async Task New_branch_joins_priority_at_lowest_position()
    {
        var owner = await factory.OwnerClientAsync();
        var code = "T" + Random.Shared.Next(1000, 9999);
        var created = await owner.PostAsJsonAsync("/api/v1/admin/branches", new { code, name = "Test " + code, reason = "expansion" }).OkJsonAsync(HttpStatusCode.Created);
        var priority = await owner.GetAsync("/api/v1/admin/fulfillment-priority").OkJsonAsync();
        var entries = priority["entries"]!.AsArray();
        entries[^1]!["branchId"]!.GetValue<Guid>().Should().Be(created["id"]!.GetValue<Guid>());
        entries.Select(e => e!["priority"]!.GetValue<int>()).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Owner_reorders_priority_and_change_is_audited_with_old_and_new_order()
    {
        var owner = await factory.OwnerClientAsync();
        var current = await owner.GetAsync("/api/v1/admin/fulfillment-priority").OkJsonAsync();
        var version = current["version"]!.GetValue<int>();
        var ids = current["entries"]!.AsArray().Select(e => e!["branchId"]!.GetValue<Guid>()).ToList();
        var reversed = Enumerable.Reverse(ids).ToList();

        var updated = await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority",
            new { branchIds = reversed, expectedVersion = version, reason = "Hyderabad stock is larger this season" }).OkJsonAsync();
        updated["version"]!.GetValue<int>().Should().Be(version + 1);
        updated["entries"]!.AsArray().Select(e => e!["branchId"]!.GetValue<Guid>()).Should().Equal(reversed);

        var audit = await owner.GetAsync($"/api/v1/admin/audit?action=branches.fulfillment_priority.changed&entityId={version + 1}").OkJsonAsync();
        var entry = audit["items"]!.AsArray().Single()!;
        entry["actorUserId"]!.GetValue<Guid>().Should().Be(factory.OwnerUserId);
        entry["reason"]!.GetValue<string>().Should().Be("Hyderabad stock is larger this season");
        entry["before"]!.GetValue<string>().Should().Contain($"\"version\": {version}");
        entry["after"]!.GetValue<string>().Should().Contain($"\"version\": {version + 1}");

        // Restore original order for other tests.
        await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = ids, expectedVersion = version + 1, reason = "restore" }).OkJsonAsync();
    }

    [Fact]
    public async Task Priority_must_contain_every_branch_and_detect_stale_edits()
    {
        var owner = await factory.OwnerClientAsync();
        var current = await owner.GetAsync("/api/v1/admin/fulfillment-priority").OkJsonAsync();
        var version = current["version"]!.GetValue<int>();
        var ids = current["entries"]!.AsArray().Select(e => e!["branchId"]!.GetValue<Guid>()).ToList();

        var partial = await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = ids.Skip(1), expectedVersion = version, reason = "x" });
        (await partial.ErrorCodeAsync()).Should().Be("PRIORITY_MUST_LIST_ALL_BRANCHES");

        var duplicate = await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = ids.Skip(1).Append(ids[1]), expectedVersion = version, reason = "x" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stale = await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = ids, expectedVersion = version - 1, reason = "x" });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Branch_manager_cannot_change_priority_or_branches()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.BranchManager, DevelopmentSeedData.BranchKarimnagar);
        var manager = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        (await manager.GetAsync("/api/v1/admin/fulfillment-priority")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = Array.Empty<Guid>(), expectedVersion = 1, reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsJsonAsync("/api/v1/admin/branches", new { code = "ZZZ", name = "x", reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Branch_codes_are_unique()
    {
        var owner = await factory.OwnerClientAsync();
        var response = await owner.PostAsJsonAsync("/api/v1/admin/branches", new { code = "knr", name = "Duplicate", reason = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).Should().Be("BRANCH_CODE_EXISTS");
    }

    [Fact]
    public async Task Roles_can_only_be_assigned_to_existing_active_branches()
    {
        var owner = await factory.OwnerClientAsync();
        var (userId, _, _) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar);
        var role = await factory.RoleIdAsync(SystemRoles.InventoryEmployee);

        var unknown = await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/role-assignments", new { roleId = role, branchId = Guid.NewGuid(), reason = "x" });
        (await unknown.ErrorCodeAsync()).Should().Be("BRANCH_NOT_FOUND");

        var code = "I" + Random.Shared.Next(1000, 9999);
        var branch = await owner.PostAsJsonAsync("/api/v1/admin/branches", new { code, name = "Closing " + code, reason = "x" }).OkJsonAsync(HttpStatusCode.Created);
        var branchId = branch["id"]!.GetValue<Guid>();
        await owner.PostAsJsonAsync($"/api/v1/admin/branches/{branchId}/status", new { isActive = false, reason = "closed" }).OkJsonAsync();
        var inactive = await owner.PostAsJsonAsync($"/api/v1/admin/users/{userId}/role-assignments", new { roleId = role, branchId, reason = "x" });
        (await inactive.ErrorCodeAsync()).Should().Be("BRANCH_INACTIVE");
    }
}
