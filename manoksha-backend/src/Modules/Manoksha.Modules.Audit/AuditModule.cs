using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Modules.Audit.Application;
using Manoksha.Modules.Audit.Endpoints;
using Manoksha.Modules.Audit.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Audit;

public sealed class AuditModule : IModule
{
    public string Name => "Audit";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, AuditModelConfiguration>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditQueryService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AuditEndpoints.Map(endpoints);
}
