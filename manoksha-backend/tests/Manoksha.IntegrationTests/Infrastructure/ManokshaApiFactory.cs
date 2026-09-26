using Manoksha.Application.Modules;
using Manoksha.Modules.Branches.Application;
using Manoksha.Modules.Identity;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Manoksha.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API against a real, freshly migrated PostgreSQL database. Uses MANOKSHA_TEST_POSTGRES (a server
/// connection string) when set — e.g. in CI with a service container — otherwise starts a Testcontainers Postgres 16.
/// </summary>
public sealed class ManokshaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OwnerEmail = "owner@test.manoksha";
    public const string OwnerPassword = "Owner-Test-Passw0rd";
    public const string FailingSmsNumber = "+919000000001";
    public const string MfaTestRole = "MFA_REQUIRED_TEST";

    private PostgreSqlContainer? _container;
    private string _connectionString = default!;
    private string? _externalServer;
    private string? _externalDatabase;

    public const string SimulatorWebhookSecret = "integration-test-simulator-secret";

    public TestClock Clock { get; } = new();

    public Guid OwnerUserId { get; private set; }

    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("MANOKSHA_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var database = $"manoksha_it_{Guid.NewGuid():N}"[..30];
            _externalServer = external;
            _externalDatabase = database;
            await using (var admin = new NpgsqlConnection(external))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
                await cmd.ExecuteNonQueryAsync();
            }
            _connectionString = new NpgsqlConnectionStringBuilder(external) { Database = database }.ConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        // Building the host runs migrations + seeders.
        _ = Services;
        OwnerUserId = await IdentityCommands.BootstrapOwnerAsync(Services, OwnerEmail, "Test Owner", OwnerPassword, CancellationToken.None);

        // The three V1 branches with the well-known development ids (Karimnagar P1, Hyderabad P2, Mulugu P3).
        await using var scope = Services.CreateAsyncScope();
        var branches = scope.ServiceProvider.GetRequiredService<BranchService>();
        foreach (var (id, code, name) in new[]
        {
            (DevelopmentSeedData.BranchKarimnagar, "KNR", "Karimnagar"),
            (DevelopmentSeedData.BranchHyderabad, "HYD", "Hyderabad"),
            (DevelopmentSeedData.BranchMulugu, "MLG", "Mulugu"),
        })
        {
            await branches.CreateAsync(id, new CreateBranchRequest(code, name, null, "test setup"), CancellationToken.None);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Manoksha", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "100000");
        builder.UseSetting("RateLimiting:OtpPermitPerMinute", "100000");
        builder.UseSetting("Auth:MfaRequiredRoles:0", MfaTestRole);
        builder.UseSetting("Integrations:Sms:Provider", "Fake");
        builder.UseSetting("Integrations:Sms:Fake:FailNumbers:0", FailingSmsNumber);
        builder.UseSetting("Integrations:Email:Provider", "Logging");
        builder.UseSetting("Integrations:Storage:Provider", "Local");
        builder.UseSetting("Integrations:Storage:Local:RootPath", Path.Combine(Path.GetTempPath(), "manoksha-it-files"));
        builder.UseSetting("Integrations:Payments:Provider", "Simulator");
        builder.UseSetting("Integrations:Payments:Simulator:WebhookSecret", SimulatorWebhookSecret);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);
        });
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        if (_externalServer is not null)
        {
            await using var admin = new NpgsqlConnection(_externalServer);
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_externalDatabase}\" WITH (FORCE)", admin);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ManokshaApiFactory>
{
    public const string Name = "api";
}
