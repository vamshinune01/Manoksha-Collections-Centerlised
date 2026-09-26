using Manoksha.Application.Modules;
using Manoksha.Modules.Audit;
using Manoksha.Modules.Branches;
using Manoksha.Modules.Catalog;
using Manoksha.Modules.Employees;
using Manoksha.Modules.Identity;
using Manoksha.Modules.Inventory;
using Manoksha.Modules.Orders;
using Manoksha.Modules.Payments;
using Manoksha.Modules.Pricing;
using Manoksha.Modules.Resellers;
using Manoksha.Modules.Wallet;
using Manoksha.Modules.Purchasing;
using Manoksha.Modules.Settings;

namespace Manoksha.Hosting;

/// <summary>Every module of the monolith. Both the API and the Worker compose the same set.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new IdentityModule(),
        new AuditModule(),
        new SettingsModule(),
        new BranchesModule(),
        new EmployeesModule(),
        new CatalogModule(),
        new InventoryModule(),
        new PurchasingModule(),
        new WalletModule(),
        new ResellersModule(),
        new PricingModule(),
        new PaymentsModule(),
        new OrdersModule(),
    ];
}
