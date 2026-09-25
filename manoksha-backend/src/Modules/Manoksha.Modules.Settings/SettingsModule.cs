using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Modules.Settings.Application;
using Manoksha.Modules.Settings.Endpoints;
using Manoksha.Modules.Settings.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Settings;

public sealed class SettingsModule : IModule
{
    public string Name => "Settings";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, SettingsModelConfiguration>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsReader>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<IStartupSeeder, SettingsSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SettingsEndpoints.Map(endpoints);
}
