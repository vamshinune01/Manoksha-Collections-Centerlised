using Manoksha.Application.Modules;
using Manoksha.Modules.Orders.Application;
using Manoksha.Modules.Orders.Endpoints;
using Manoksha.Modules.Orders.Persistence;
using Manoksha.Modules.Payments.Contracts;
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
        services.AddScoped<FulfillmentRouter>();
        services.AddScoped<InquiryService>();
        services.AddScoped<CustomerCheckoutService>();
        services.AddScoped<OnlineOrderService>();
        services.AddScoped<StorefrontService>();
        services.AddScoped<OrderPaymentHandler>();
        services.AddScoped<IPaymentPurposeHandler>(sp => sp.GetRequiredService<OrderPaymentHandler>());
        services.AddScoped<ReservationSweeper>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => OrderEndpoints.Map(endpoints);
}

/// <summary>Background operations the Worker host runs for the Orders module.</summary>
public static class OrderJobs
{
    /// <returns>Number of orders whose expired reservation was released.</returns>
    public static Task<int> ReleaseExpiredReservationsAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        scopedServices.GetRequiredService<ReservationSweeper>().RunOnceAsync(ct);
}
