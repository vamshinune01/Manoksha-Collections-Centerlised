using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.Integrations;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Manoksha.Hosting;

public static class ManokshaHostBuilder
{
    /// <summary>Registers persistence, integrations and every module. Shared by API and Worker.</summary>
    public static IServiceCollection AddManokshaCore(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Manoksha")
            ?? throw new InvalidOperationException("ConnectionStrings:Manoksha is not configured.");

        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        services.AddManokshaPersistence(connectionString);
        services.AddManokshaIntegrations(configuration, environment);

        foreach (var module in ModuleCatalog.All)
        {
            module.AddServices(services, configuration, environment);
        }
        return services;
    }

    /// <summary>API host only: authentication, authorization and other HTTP services of every module.</summary>
    public static IServiceCollection AddManokshaApiModules(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in ModuleCatalog.All)
        {
            module.AddApiServices(services, configuration);
        }
        return services;
    }

    /// <summary>Applies pending migrations (when enabled) and runs module seeders.</summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, bool applyMigrations, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Manoksha.Startup");
        if (applyMigrations)
        {
            await MigrateAsync(sp, logger, cancellationToken);
        }
        foreach (var seeder in sp.GetServices<IStartupSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    public static async Task MigrateAsync(IServiceProvider scopedServices, ILogger logger, CancellationToken cancellationToken)
    {
        var db = scopedServices.GetRequiredService<ManokshaDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
