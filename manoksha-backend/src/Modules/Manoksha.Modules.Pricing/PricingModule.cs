using Manoksha.Application.Modules;
using Manoksha.Modules.Pricing.Application;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Modules.Pricing.Endpoints;
using Manoksha.Modules.Pricing.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Pricing;

public sealed class PricingModule : IModule
{
    public string Name => "Pricing";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, PricingModelConfiguration>();
        services.AddScoped<PriceCalculator>();
        services.AddScoped<IPriceCalculator>(sp => sp.GetRequiredService<PriceCalculator>());
        services.AddScoped<PricingService>();
        services.AddScoped<ResellerCatalogService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PricingEndpoints.Map(endpoints);
}
