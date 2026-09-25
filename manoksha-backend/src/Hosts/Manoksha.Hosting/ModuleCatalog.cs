using Manoksha.Application.Modules;
using Manoksha.Modules.Audit;
using Manoksha.Modules.Branches;
using Manoksha.Modules.Catalog;
using Manoksha.Modules.Employees;
using Manoksha.Modules.Identity;
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
    ];
}
