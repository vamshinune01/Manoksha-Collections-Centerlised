using Microsoft.Extensions.Configuration;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Guards environment layering. .NET merges configuration arrays by index, so an empty array in an override file
/// cannot remove values from the base file — these tests catch that class of mistake.
/// </summary>
public class EnvironmentConfigurationTests
{
    [Theory]
    [InlineData("Development", new string[0])]
    [InlineData("Staging", new[] { "OWNER" })]
    [InlineData("Production", new[] { "OWNER" })]
    public void Owner_mfa_is_required_outside_development(string environment, string[] expected)
    {
        var config = Load(environment);
        (config.GetSection("Auth:MfaRequiredRoles").Get<string[]>() ?? []).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Production_does_not_default_to_development_adapters()
    {
        var config = Load("Production");
        config["Integrations:Sms:Provider"].Should().NotBe("Fake");
        config["Integrations:Email:Provider"].Should().NotBe("Logging");
        config["Integrations:Storage:Provider"].Should().NotBe("Local");
        config["Database:MigrateOnStartup"].Should().Be("False", "production migrations run as a separate job");
    }

    private static IConfigurationRoot Load(string environment)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Hosts", "Manoksha.Api")))
        {
            dir = dir.Parent;
        }
        var api = Path.Combine(dir!.FullName, "src", "Hosts", "Manoksha.Api");
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(api, "appsettings.json"))
            .AddJsonFile(Path.Combine(api, $"appsettings.{environment}.json"), optional: true)
            .Build();
    }
}
