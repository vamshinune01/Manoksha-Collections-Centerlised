using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Modules.Payments.Application;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Payments.Endpoints;
using Manoksha.Modules.Payments.Persistence;
using Manoksha.Modules.Payments.Simulator;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Payments;

/// <summary>
/// Payments (SPEC §14, §17.1, §36): attempts, webhook inbox, provider polling, late-success recheck and reconciliation cases.
/// The provider is chosen by "Integrations:Payments:Provider". Only the development simulator exists until the Owner selects a
/// UPI gateway; it is refused in Production so a misconfiguration can never fake payments.
/// </summary>
public sealed class PaymentsModule : IModule
{
    private bool _simulatorEnabled;

    public string Name => "Payments";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, PaymentsModelConfiguration>();
        services.AddScoped<PaymentProcessor>();
        services.AddScoped<PaymentService>();
        services.AddScoped<IPayments>(sp => sp.GetRequiredService<PaymentService>());
        services.AddScoped<PaymentWebhookService>();
        services.AddScoped<PaymentPoller>();
        services.AddScoped<PaymentQueryService>();

        var provider = configuration["Integrations:Payments:Provider"] ?? "Simulator";
        switch (provider)
        {
            case "Simulator":
                if (environment.IsProduction())
                {
                    throw new InvalidOperationException("Integrations:Payments:Provider=Simulator is a development-only adapter and cannot be used in Production.");
                }
                _simulatorEnabled = true;
                services.Configure<SimulatorOptions>(configuration.GetSection("Integrations:Payments:Simulator"));
                services.AddSingleton<SimulatorKey>();
                services.AddScoped<IPaymentGateway, SimulatorPaymentGateway>();
                services.AddScoped<SimulatorService>();
                break;
            default:
                throw new InvalidOperationException($"Unknown payment provider '{provider}'.");
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PaymentEndpoints.Map(endpoints, _simulatorEnabled);
}

/// <summary>Background operations the Worker host runs for the Payments module.</summary>
public static class PaymentJobs
{
    /// <returns>Number of events/attempts handled.</returns>
    public static Task<int> PollAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        scopedServices.GetRequiredService<PaymentPoller>().RunOnceAsync(ct);
}
