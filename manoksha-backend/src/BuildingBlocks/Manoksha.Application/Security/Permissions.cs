using System.Reflection;

namespace Manoksha.Application.Security;

/// <summary>
/// The V1 permission catalog. Codes are stable identifiers stored in the database. Permissions marked
/// <see cref="OwnerOnlyAttribute"/> can never be granted to any role other than OWNER (e.g. employee
/// permission changes, wallet adjustments, retail pricing — SPEC §4, §15, §26).
/// </summary>
public static class Permissions
{
    public static class Identity
    {
        public const string UsersView = "identity.users.view";
        public const string UsersManage = "identity.users.manage";
        public const string RolesView = "identity.roles.view";
        [OwnerOnly] public const string RolesManage = "identity.roles.manage";
        [OwnerOnly] public const string UserRolesAssign = "identity.user_roles.assign";
        public const string SessionsRevoke = "identity.sessions.revoke";
    }

    public static class Settings
    {
        public const string View = "settings.view";
        [OwnerOnly] public const string Manage = "settings.manage";
    }

    public static class Audit
    {
        [OwnerOnly] public const string View = "audit.view";
    }

    public static class Branches
    {
        public const string View = "branches.view";
        [OwnerOnly] public const string Manage = "branches.manage";
        [OwnerOnly] public const string FulfillmentPriorityManage = "fulfillment.priority.manage";
    }

    public static class Employees
    {
        public const string View = "employees.view";
        public const string Manage = "employees.manage";
    }

    public static class Attendance
    {
        public const string Self = "attendance.self";
        public const string View = "attendance.view";
        public const string Correct = "attendance.correct";
    }

    public static class Catalog
    {
        public const string View = "catalog.view";
        public const string Manage = "catalog.manage";
        public const string BarcodesPrint = "barcodes.print";
    }

    public static class Purchasing
    {
        public const string View = "purchasing.view";
        public const string Manage = "purchasing.manage";
        public const string GoodsReceiptRecord = "goods_receipt.record";
    }

    public static class Inventory
    {
        public const string View = "inventory.view";
        public const string Count = "inventory.count";
        public const string AdjustRequest = "inventory.adjust.request";
        public const string AdjustApprove = "inventory.adjust.approve";
        public const string DiscrepancyResolve = "inventory.discrepancy.resolve";
    }

    public static class Transfers
    {
        public const string Create = "transfers.create";
        public const string Approve = "transfers.approve";
        public const string Dispatch = "transfers.dispatch";
        public const string Receive = "transfers.receive";
    }

    public static class Pricing
    {
        public const string View = "pricing.view";
        [OwnerOnly] public const string Manage = "pricing.manage";
    }

    public static class Resellers
    {
        public const string View = "resellers.view";
        [OwnerOnly] public const string Manage = "resellers.manage";
    }

    public static class Wallet
    {
        public const string View = "wallet.view";
        [OwnerOnly] public const string DepositApprove = "wallet.deposit.approve";
        [OwnerOnly] public const string Adjust = "wallet.adjust";
    }

    public static class Pos
    {
        public const string Sell = "pos.sell";
        public const string PriceOverride = "pos.price_override";
        public const string PriceOverrideApprove = "pos.price_override.approve";
    }

    public static class Orders
    {
        public const string View = "orders.view";
        public const string Fulfill = "orders.fulfill";
        public const string FulfillmentExceptionRaise = "orders.fulfillment_exception.raise";
        public const string Reroute = "orders.reroute";
        public const string Cancel = "orders.cancel";
    }

    public static class Approvals
    {
        public const string View = "approvals.view";
        public const string Decide = "approvals.decide";
    }

    public static class Exceptions
    {
        public const string View = "exceptions.view";
        public const string Manage = "exceptions.manage";
        [OwnerOnly] public const string ReconciliationManage = "reconciliation.manage";
    }

    public static class Notifications
    {
        public const string View = "notifications.view";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        [OwnerOnly] public const string Global = "reports.global";
    }

    private static readonly Lazy<IReadOnlyList<PermissionDefinition>> AllLazy = new(Discover);

    public static IReadOnlyList<PermissionDefinition> All => AllLazy.Value;

    public static bool Exists(string code) => All.Any(p => p.Code == code);

    public static bool IsOwnerOnly(string code) => All.Any(p => p.Code == code && p.OwnerOnly);

    private static List<PermissionDefinition> Discover()
    {
        var list = new List<PermissionDefinition>();
        foreach (var group in typeof(Permissions).GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (var field in group.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
            {
                var code = (string)field.GetRawConstantValue()!;
                list.Add(new PermissionDefinition(code, group.Name, field.GetCustomAttribute<OwnerOnlyAttribute>() is not null));
            }
        }
        return list;
    }
}

public sealed record PermissionDefinition(string Code, string Module, bool OwnerOnly);

[AttributeUsage(AttributeTargets.Field)]
public sealed class OwnerOnlyAttribute : Attribute;
