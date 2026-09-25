using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Application.Security;

/// <summary>
/// Seeded system roles and their default permissions (design §9). Defaults for non-Owner roles are seeded
/// once; the Owner may later adjust them. OWNER always has every permission and is not editable.
/// Items the SPEC marks "if granted/delegated" are intentionally NOT defaults.
/// </summary>
public static class SystemRoles
{
    public const string Owner = "OWNER";
    public const string BranchManager = "BRANCH_MANAGER";
    public const string SalesEmployee = "SALES_EMPLOYEE";
    public const string InventoryEmployee = "INVENTORY_EMPLOYEE";

    public static readonly IReadOnlyList<SystemRoleDefinition> Definitions =
    [
        new(Owner, "Owner", RoleScope.Global, P.All.Select(p => p.Code).ToArray()),
        new(BranchManager, "Branch Manager", RoleScope.Branch,
        [
            P.Branches.View, P.Employees.View,
            P.Attendance.Self, P.Attendance.View, P.Attendance.Correct,
            P.Catalog.View, P.Catalog.BarcodesPrint,
            P.Purchasing.GoodsReceiptRecord,
            P.Inventory.View, P.Inventory.Count, P.Inventory.AdjustRequest, P.Inventory.AdjustApprove,
            P.Transfers.Create, P.Transfers.Approve, P.Transfers.Dispatch, P.Transfers.Receive,
            P.Pricing.View,
            P.Pos.Sell, P.Pos.PriceOverride, P.Pos.PriceOverrideApprove,
            P.Orders.View, P.Orders.Fulfill, P.Orders.FulfillmentExceptionRaise,
            P.Approvals.View, P.Approvals.Decide,
            P.Exceptions.View, P.Notifications.View, P.Reports.View,
        ]),
        new(SalesEmployee, "Sales Employee", RoleScope.Branch,
        [
            P.Attendance.Self, P.Catalog.View, P.Inventory.View, P.Pricing.View,
            P.Pos.Sell, P.Orders.View, P.Orders.FulfillmentExceptionRaise, P.Notifications.View,
        ]),
        new(InventoryEmployee, "Inventory Employee", RoleScope.Branch,
        [
            P.Attendance.Self, P.Catalog.View, P.Catalog.BarcodesPrint,
            P.Purchasing.GoodsReceiptRecord,
            P.Inventory.View, P.Inventory.Count, P.Inventory.AdjustRequest,
            P.Transfers.Create, P.Transfers.Dispatch, P.Transfers.Receive,
            P.Orders.FulfillmentExceptionRaise, P.Notifications.View,
        ]),
    ];
}

public enum RoleScope
{
    /// <summary>Assignment applies to all branches (no branch id).</summary>
    Global = 1,

    /// <summary>Assignment requires a branch id and applies only there.</summary>
    Branch = 2,
}

public sealed record SystemRoleDefinition(string Code, string Name, RoleScope Scope, IReadOnlyList<string> DefaultPermissions);
