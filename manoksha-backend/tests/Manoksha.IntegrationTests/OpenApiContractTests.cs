using System.Text.Json.Nodes;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>
/// The committed OpenAPI snapshot (contracts/openapi/manoksha-v1.json) is what the web and Flutter clients are
/// generated from. It must match the running API. Set UPDATE_OPENAPI_CONTRACT=1 to regenerate it intentionally.
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenApiContractTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Committed_contract_matches_the_api()
    {
        var live = JsonNode.Parse(await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json"))!;
        var path = Path.Combine(FindRepoRoot(), "contracts", "openapi", "manoksha-v1.json");
        var liveText = live.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n";

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_CONTRACT") == "1")
        {
            await File.WriteAllTextAsync(path, liveText);
            return;
        }

        File.Exists(path).Should().BeTrue($"{path} is missing; run tests with UPDATE_OPENAPI_CONTRACT=1");
        var committed = await File.ReadAllTextAsync(path);
        committed.Should().Be(liveText, "the API changed; regenerate the contract with UPDATE_OPENAPI_CONTRACT=1 and regenerate clients");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "contracts")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root with /contracts not found.");
    }
}
