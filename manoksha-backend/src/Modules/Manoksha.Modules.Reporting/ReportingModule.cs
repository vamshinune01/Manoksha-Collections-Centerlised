using Manoksha.Application.Modules;
using Manoksha.Modules.Reporting.Application;
using Manoksha.Modules.Reporting.Contracts;
using Manoksha.Modules.Reporting.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Reporting;

/// <summary>Dashboards, reports and the Exception Center: read-only SQL across module schemas (design §3). No tables of its own.</summary>
public sealed class ReportingModule : IModule
{
    public string Name => "Reporting";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddScoped<ReportingService>();
        services.AddScoped<IReportingQueries>(sp => sp.GetRequiredService<ReportingService>());
        services.AddScoped<ExceptionCenterService>();
        services.AddScoped<ResellerDashboardService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ReportingEndpoints.Map(endpoints);
}
