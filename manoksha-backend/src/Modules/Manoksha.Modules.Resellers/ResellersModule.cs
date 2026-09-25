using Manoksha.Application.Modules;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Resellers.Application;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Resellers.Endpoints;
using Manoksha.Modules.Resellers.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Resellers;

public sealed class ResellersModule : IModule
{
    public string Name => "Resellers";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, ResellersModelConfiguration>();
        services.AddScoped<ResellerService>();
        services.AddScoped<IResellerDirectory>(sp => sp.GetRequiredService<ResellerService>());
        services.AddScoped<IResellerLoginGate>(sp => sp.GetRequiredService<ResellerService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ResellerEndpoints.Map(endpoints);
}
