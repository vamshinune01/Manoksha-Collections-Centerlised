using Manoksha.Application.Modules;
using Manoksha.Modules.Orders.Application;
using Manoksha.Modules.Orders.Endpoints;
using Manoksha.Modules.Orders.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Orders;

public sealed class OrdersModule : IModule
{
    public string Name => "Orders";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, OrdersModelConfiguration>();
        services.AddScoped<WhatsApp>();
        services.AddScoped<ResellerCustomerService>();
        services.AddScoped<OrderQueryService>();
        services.AddScoped<ResellerCheckoutService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => OrderEndpoints.Map(endpoints);
}
