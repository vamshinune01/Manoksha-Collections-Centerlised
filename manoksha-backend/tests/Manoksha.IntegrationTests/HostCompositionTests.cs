using Manoksha.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
