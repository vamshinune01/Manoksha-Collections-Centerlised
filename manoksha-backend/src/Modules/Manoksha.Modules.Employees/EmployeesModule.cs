using Manoksha.Application.Modules;
using Manoksha.Modules.Employees.Application;
using Manoksha.Modules.Employees.Endpoints;
using Manoksha.Modules.Employees.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Employees;

public sealed class EmployeesModule : IModule
{
    public string Name => "Employees";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, EmployeesModelConfiguration>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<IDevelopmentSeeder, EmployeesDevelopmentSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => EmployeeEndpoints.Map(endpoints);
}
