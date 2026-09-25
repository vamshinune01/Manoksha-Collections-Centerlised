using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.SharedKernel;

namespace Manoksha.UnitTests;

public class PermissionCatalogTests
{
    [Fact]
    public void Codes_are_unique() =>
        Permissions.All.Select(p => p.Code).Should().OnlyHaveUniqueItems();

    [Fact]
    public void Owner_role_holds_every_permission() =>
        SystemRoles.Definitions.Single(r => r.Code == SystemRoles.Owner).DefaultPermissions
            .Should().BeEquivalentTo(Permissions.All.Select(p => p.Code));

    [Fact]
    public void No_non_owner_system_role_has_an_owner_only_permission()
    {
        foreach (var role in SystemRoles.Definitions.Where(r => r.Code != SystemRoles.Owner))
        {
            role.DefaultPermissions.Where(Permissions.IsOwnerOnly).Should().BeEmpty($"{role.Code} must not hold owner-only permissions");
            role.DefaultPermissions.Should().OnlyContain(c => Permissions.Exists(c));
        }
    }

    [Theory]
    [InlineData(Permissions.Identity.UserRolesAssign)]  // SPEC §26 employee permission change: Owner only
    [InlineData(Permissions.Identity.RolesManage)]
    [InlineData(Permissions.Wallet.Adjust)]             // SPEC §26 manual wallet adjustment: Owner only
    [InlineData(Permissions.Wallet.DepositApprove)]     // SPEC §17.2 Owner approval
    [InlineData(Permissions.Pricing.Manage)]            // SPEC §15 Owner controls prices
    [InlineData(Permissions.Branches.FulfillmentPriorityManage)] // SPEC §11
    [InlineData(Permissions.Audit.View)]
    public void Spec_owner_only_operations_are_owner_only(string code) => Permissions.IsOwnerOnly(code).Should().BeTrue();

    [Theory]
    [InlineData(Permissions.Purchasing.Manage)] // SPEC §8 "unless explicitly delegated"
    [InlineData(Permissions.Orders.Reroute)]    // SPEC §22 "Owner/authorized role"
    [InlineData(Permissions.Orders.Cancel)]     // ADR-001 §8 manager with configured permission
    public void Delegable_operations_are_not_owner_only(string code) => Permissions.IsOwnerOnly(code).Should().BeFalse();

    [Fact]
    public void Owner_only_permission_cannot_be_granted_to_custom_role()
    {
        var role = new Role("STORE_LEAD", "Store lead", null, RoleScope.Branch, isSystem: false, DateTimeOffset.UtcNow);
        var act = () => role.SetPermissions([Permissions.Pos.Sell, Permissions.Wallet.Adjust], allowOwnerOnly: false);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PERMISSION_OWNER_ONLY");
    }

    [Fact]
    public void Unknown_permission_is_rejected()
    {
        var role = new Role("STORE_LEAD", "Store lead", null, RoleScope.Branch, isSystem: false, DateTimeOffset.UtcNow);
        var act = () => role.SetPermissions(["made.up"], allowOwnerOnly: false);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PERMISSION_UNKNOWN");
    }

    [Fact]
    public void Default_branch_roles_do_not_include_delegable_extras()
    {
        var manager = SystemRoles.Definitions.Single(r => r.Code == SystemRoles.BranchManager).DefaultPermissions;
        manager.Should().NotContain(Permissions.Orders.Cancel, "cancellation needs explicitly configured permission (ADR-001 §8)");
        manager.Should().NotContain(Permissions.Employees.Manage);
        var sales = SystemRoles.Definitions.Single(r => r.Code == SystemRoles.SalesEmployee).DefaultPermissions;
        sales.Should().NotContain(Permissions.Pos.PriceOverride, "normal sales employees have no unrestricted price change (SPEC §20)");
    }
}
