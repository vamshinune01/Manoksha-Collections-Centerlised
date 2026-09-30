using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Modules.Notifications.Application;
using Manoksha.Modules.Notifications.Endpoints;
using Manoksha.Modules.Notifications.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Notifications;

/// <summary>Business events → in-app notifications, emails and sensitive alerts (SPEC §29, design §17).</summary>
public sealed class NotificationsModule : IModule
{
    public string Name => "Notifications";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, NotificationsModelConfiguration>();
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddScoped<NotificationPublisher>();
        services.AddScoped<NotificationService>();
        services.AddScoped<OrderRecipients>();

        services.AddScoped<IOutboxEventHandler, OrderConfirmedHandler>();
        services.AddScoped<IOutboxEventHandler, OrderStatusChangedHandler>();
        services.AddScoped<IOutboxEventHandler, FulfillmentExceptionHandler>();
        services.AddScoped<IOutboxEventHandler, FulfillmentInquiryHandler>();
        services.AddScoped<IOutboxEventHandler, ReconciliationRequiredHandler>();
        services.AddScoped<IOutboxEventHandler, DepositSubmittedHandler>();
        services.AddScoped<IOutboxEventHandler, DepositDecidedHandler>();
        services.AddScoped<IOutboxEventHandler, OnlineDepositCreditedHandler>();
        services.AddScoped<IOutboxEventHandler, WalletIntegrityHandler>();
        services.AddScoped<IOutboxEventHandler, CommercialTermsChangedHandler>();
        services.AddScoped<IOutboxEventHandler, ResellerStatusChangedHandler>();
        services.AddScoped<IOutboxEventHandler, DiscrepancyOpenedHandler>();
        services.AddScoped<IOutboxEventHandler, TransferRequestedHandler>();
        services.AddScoped<IOutboxEventHandler, AdjustmentRequestedHandler>();
        services.AddScoped<IOutboxEventHandler, SecurityAlertHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => NotificationEndpoints.Map(endpoints);
}
