using Manoksha.Modules.Audit.Persistence;
using Manoksha.Modules.Branches.Persistence;
using Manoksha.Modules.Catalog.Persistence;
using Manoksha.Modules.Employees.Persistence;
using Manoksha.Modules.Identity.Persistence;
using Manoksha.Modules.Settings.Persistence;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Manoksha.Migrations;

/// <summary>Used only by `dotnet ef` to generate migrations. Keep the module list in sync with ModuleCatalog.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ManokshaDbContext>
{
    public ManokshaDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("MANOKSHA_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=manoksha_design;Username=postgres";
        var builder = new DbContextOptionsBuilder<ManokshaDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, new NpgsqlDataSourceBuilder(connection).Build());
        return new ManokshaDbContext(builder.Options,
        [
            new IdentityModelConfiguration(),
            new AuditModelConfiguration(),
            new SettingsModelConfiguration(),
            new BranchesModelConfiguration(),
            new EmployeesModelConfiguration(),
            new CatalogModelConfiguration(),
        ]);
    }
}
