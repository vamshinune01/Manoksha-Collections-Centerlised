using Manoksha.Application.Modules;
using Manoksha.Modules.Inventory.Application;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Inventory.Endpoints;
using Manoksha.Modules.Inventory.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Inventory;

public sealed class InventoryModule : IModule
{
    public string Name => "Inventory";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, InventoryModelConfiguration>();
        services.AddScoped<StockEngine>();
        services.AddScoped<InventoryAccess>();
        services.AddScoped<IStockReceiver, StockReceiver>();
        services.AddScoped<IStockAllocator, StockAllocator>();
        services.AddScoped<TransferService>();
        services.AddScoped<DiscrepancyService>();
        services.AddScoped<CountService>();
        services.AddScoped<AdjustmentService>();
        services.AddScoped<InventoryQueryService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => InventoryEndpoints.Map(endpoints);
}
