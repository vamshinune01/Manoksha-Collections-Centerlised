using Manoksha.Application.Modules;
using Manoksha.Modules.Branches.Application;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Branches.Endpoints;
using Manoksha.Modules.Branches.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Branches;

public sealed class BranchesModule : IModule
{
    public string Name => "Branches";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, BranchesModelConfiguration>();
        services.AddScoped<BranchService>();
        services.AddScoped<IBranchDirectory>(sp => sp.GetRequiredService<BranchService>());
        services.AddScoped<IFulfillmentPriorityProvider>(sp => sp.GetRequiredService<BranchService>());
        services.AddScoped<IDevelopmentSeeder, BranchesDevelopmentSeeder>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => BranchEndpoints.Map(endpoints);
}
