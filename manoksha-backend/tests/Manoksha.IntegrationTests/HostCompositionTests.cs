using Manoksha.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Manoksha.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace Manoksha.IntegrationTests;

/// <summary>
/// The Worker is a non-web host. Every module's core registrations must be constructible without ASP.NET Core
/// routing/authorization services (regression: web-only services once broke Worker startup).
/// </summary>
public class HostCompositionTests
{
    [Fact]
    public void Worker_style_host_validates_all_core_registrations()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Manoksha"] = "Host=localhost;Database=unused",
        });
        builder.Services.AddManokshaCore(builder.Configuration, builder.Environment);

        var act = () => builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose();

        act.Should().NotThrow();
    }
}

/// <summary>Every scoped service must resolve without recursion (factory registrations hide DI cycles from ValidateOnBuild).</summary>
[Collection(ApiCollection.Name)]
public class ServiceResolutionTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task All_module_services_resolve()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var types = typeof(Manoksha.Hosting.ModuleCatalog).Assembly.GetReferencedAssemblies()
            .Select(System.Reflection.Assembly.Load)
            .Where(a => a.GetName().Name!.StartsWith("Manoksha.Modules.", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace?.EndsWith(".Contracts", StringComparison.Ordinal) == true && t.IsInterface)
            .ToList();
        types.Should().NotBeEmpty();
        foreach (var type in types)
        {
            var act = () => sp.GetServices(type).ToList();
            act.Should().NotThrow($"{type.Name} must resolve");
        }
    }
}
