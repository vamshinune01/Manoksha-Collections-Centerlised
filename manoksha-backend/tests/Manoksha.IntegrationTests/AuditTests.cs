using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Audit is written in the same transaction and cannot be edited or deleted (SPEC §30).</summary>
[Collection(ApiCollection.Name)]
public class AuditTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Role_assignment_is_audited_with_actor_reason_and_branch()
    {
        var (userId, _, _) = await factory.CreateInternalUserAsync(SystemRoles.InventoryEmployee, DevelopmentSeedData.BranchMulugu);
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);

        var page = await (await owner.GetAsync($"/api/v1/admin/audit?entityType=User&entityId={userId}")).ReadJsonAsync();
        var entries = page["items"]!.AsArray();
        var grant = entries.Single(e => e!["action"]!.GetValue<string>() == "identity.role_assignment.granted")!;
        grant["actorUserId"]!.GetValue<Guid>().Should().Be(factory.OwnerUserId);
        grant["actorType"]!.GetValue<string>().Should().Be("INTERNAL");
        grant["actorRoles"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Contain(SystemRoles.Owner);
        grant["reason"]!.GetValue<string>().Should().Be("test setup");
        grant["branchId"]!.GetValue<Guid>().Should().Be(DevelopmentSeedData.BranchMulugu);
        entries.Should().Contain(e => e!["action"]!.GetValue<string>() == "identity.internal_user.created");
        entries.Should().Contain(e => e!["action"]!.GetValue<string>() == "identity.password.changed");
    }

    [Theory]
    [InlineData("UPDATE audit.audit_log SET reason = 'tampered'")]
    [InlineData("DELETE FROM audit.audit_log")]
    [InlineData("TRUNCATE audit.audit_log")]
    [InlineData("UPDATE settings.system_setting_changes SET reason = 'tampered'")]
    [InlineData("DELETE FROM identity.login_events")]
    public async Task History_tables_reject_modification_at_database_level(string sql)
    {
        await factory.LoginOwnerAsync(); // ensure rows exist
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var act = () => command.ExecuteNonQueryAsync();
        (await act.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Audit_search_is_paged_newest_first()
    {
        await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar);
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var first = await (await owner.GetAsync("/api/v1/admin/audit?limit=2")).ReadJsonAsync();
        var items = first["items"]!.AsArray();
        items.Should().HaveCount(2);
        items[0]!["seq"]!.GetValue<long>().Should().BeGreaterThan(items[1]!["seq"]!.GetValue<long>());
        var next = first["nextBeforeSeq"]!.GetValue<long>();
        var second = await (await owner.GetAsync($"/api/v1/admin/audit?limit=2&beforeSeq={next}")).ReadJsonAsync();
        second["items"]!.AsArray()[0]!["seq"]!.GetValue<long>().Should().BeLessThan(next);
    }

    [Fact]
    public async Task Owner_role_permission_changes_are_audited_with_diff()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var code = "AUDIT_ROLE_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var created = await (await owner.PostAsJsonAsync("/api/v1/admin/roles",
            new { code, name = "Audit role", scope = "Branch", permissions = new[] { Permissions.Pos.Sell }, reason = "create" })).ReadJsonAsync();
        var roleId = created["id"]!.GetValue<Guid>();

        var put = await owner.PutAsJsonAsync($"/api/v1/admin/roles/{roleId}/permissions",
            new { permissions = new[] { Permissions.Pos.Sell, Permissions.Inventory.View }, reason = "needs stock view" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await (await owner.GetAsync($"/api/v1/admin/audit?entityType=Role&entityId={roleId}&action=identity.role.permissions_changed")).ReadJsonAsync();
        var entry = page["items"]!.AsArray().Single()!;
        entry["after"]!.GetValue<string>().Should().Contain(Permissions.Inventory.View);
        entry["reason"]!.GetValue<string>().Should().Be("needs stock view");
    }
}
