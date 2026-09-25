using Manoksha.Application.Modules;
using Manoksha.Modules.Catalog.Application;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Catalog.Endpoints;
using Manoksha.Modules.Catalog.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Catalog;

public sealed class CatalogModule : IModule
{
    public string Name => "Catalog";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, CatalogModelConfiguration>();
        services.AddScoped<CatalogSetupService>();
        services.AddScoped<ProductService>();
        services.AddScoped<BarcodeService>();
        services.AddScoped<ICatalogLookup>(sp => sp.GetRequiredService<ProductService>());
        services.AddScoped<IItemBarcodeIssuer>(sp => sp.GetRequiredService<BarcodeService>());
        services.AddScoped<ICatalogBarcodes>(sp => sp.GetRequiredService<BarcodeService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CatalogEndpoints.Map(endpoints);
}
