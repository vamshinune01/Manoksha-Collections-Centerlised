using Manoksha.Application.Abstractions;
using Manoksha.Integrations.Email;
using Manoksha.Integrations.Sms;
using Manoksha.Integrations.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Integrations;

/// <summary>
/// Selects provider adapters from configuration ("Integrations:{Sms|Email|Storage}:Provider"). Development
/// fakes are refused in Production so a misconfiguration can never silently skip real delivery.
/// Production adapters (SMS/DLT provider, email provider, GCS, UPI gateway) are added when selected (ADR-001).
/// </summary>
public static class IntegrationsServiceCollectionExtensions
{
    public static IServiceCollection AddManokshaIntegrations(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("Integrations");

        var sms = section["Sms:Provider"] ?? "Fake";
        switch (sms)
        {
            case "Fake":
                EnsureNotProduction(environment, "Integrations:Sms:Provider=Fake");
                services.Configure<FakeSmsOptions>(section.GetSection("Sms:Fake"));
                services.AddSingleton<ISmsSender, FakeSmsSender>();
                break;
            default:
                throw new InvalidOperationException($"Unknown SMS provider '{sms}'.");
        }

        var email = section["Email:Provider"] ?? "Logging";
        switch (email)
        {
            case "Logging":
                EnsureNotProduction(environment, "Integrations:Email:Provider=Logging");
                services.AddSingleton<IEmailSender, LoggingEmailSender>();
                break;
            case "Smtp":
                services.Configure<SmtpEmailOptions>(section.GetSection("Email:Smtp"));
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
                break;
            default:
                throw new InvalidOperationException($"Unknown email provider '{email}'.");
        }

        var storage = section["Storage:Provider"] ?? "Local";
        switch (storage)
        {
            case "Local":
                EnsureNotProduction(environment, "Integrations:Storage:Provider=Local");
                services.Configure<LocalFileStorageOptions>(section.GetSection("Storage:Local"));
                services.AddSingleton<IFileStorage, LocalFileStorage>();
                break;
            default:
                throw new InvalidOperationException($"Unknown storage provider '{storage}'.");
        }

        return services;
    }

    private static void EnsureNotProduction(IHostEnvironment environment, string setting)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException($"{setting} is a development-only adapter and cannot be used in Production.");
        }
    }
}
