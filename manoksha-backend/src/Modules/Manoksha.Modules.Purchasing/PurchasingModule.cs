using Manoksha.Application.Modules;
using Manoksha.Modules.Purchasing.Application;
using Manoksha.Modules.Purchasing.Endpoints;
using Manoksha.Modules.Purchasing.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Purchasing;

public sealed class PurchasingModule : IModule
{
    public string Name => "Purchasing";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, PurchasingModelConfiguration>();
        services.AddScoped<SupplierService>();
        services.AddScoped<PurchaseOrderService>();
        services.AddScoped<GoodsReceiptService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PurchasingEndpoints.Map(endpoints);
}
