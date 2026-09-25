using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Application.Modules;

/// <summary>A bounded module of the modular monolith. Registered by both the API and the Worker host.</summary>
public interface IModule
{
    string Name { get; }

    /// <summary>Services needed by every host (API and Worker): persistence, domain and application services.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);

    /// <summary>HTTP-only services (authentication, authorization policies). Registered by the API host only.</summary>
    void AddApiServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>Maps HTTP endpoints (API host only).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

/// <summary>Runs at startup after migrations (e.g. seeding permission catalog, default settings).</summary>
public interface IStartupSeeder
{
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}

/// <summary>LOCAL/DEVELOPMENT ONLY sample data, run by the `seed-dev` CLI command (refused outside Development).</summary>
public interface IDevelopmentSeeder
{
    int Order { get; }

    Task SeedAsync(DevelopmentSeedContext context, CancellationToken cancellationToken);
}

/// <param name="Password">Password for seeded internal users (from MANOKSHA_DEV_SEED_PASSWORD).</param>
public sealed record DevelopmentSeedContext(string Password);
