using Manoksha.Hosting;
using Manoksha.Modules.Identity;

namespace Manoksha.Api.Infrastructure;

/// <summary>
/// Operational commands. Passwords are read from environment variables (never command-line arguments, which leak
/// into process lists and shell history).
/// </summary>
internal static class CliCommands
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Manoksha.Cli");
        try
        {
            switch (args[0])
            {
                case "migrate":
                    await app.Services.InitialiseDatabaseAsync(applyMigrations: true);
                    logger.LogInformation("Database migrated and seeded.");
                    return 0;

                case "bootstrap-owner":
                {
                    var email = Option(args, "--email") ?? throw new ArgumentException("--email is required");
                    var name = Option(args, "--name") ?? "Owner";
                    var password = Environment.GetEnvironmentVariable("MANOKSHA_BOOTSTRAP_OWNER_PASSWORD")
                        ?? throw new ArgumentException("Set MANOKSHA_BOOTSTRAP_OWNER_PASSWORD.");
                    await app.Services.InitialiseDatabaseAsync(applyMigrations: false);
                    var id = await IdentityCommands.BootstrapOwnerAsync(app.Services, email, name, password, CancellationToken.None);
                    logger.LogInformation("Owner {Email} created with id {Id}.", email, id);
                    return 0;
                }

                case "seed-dev":
                {
                    if (!app.Environment.IsDevelopment())
                    {
                        throw new InvalidOperationException("seed-dev is only allowed in the Development environment.");
                    }
                    var password = Environment.GetEnvironmentVariable("MANOKSHA_DEV_SEED_PASSWORD")
                        ?? throw new ArgumentException("Set MANOKSHA_DEV_SEED_PASSWORD.");
                    await app.Services.InitialiseDatabaseAsync(applyMigrations: true);
                    await app.Services.SeedDevelopmentDataAsync(password, CancellationToken.None);
                    logger.LogInformation("Development users seeded.");
                    return 0;
                }

                default:
                    logger.LogError("Unknown command '{Command}'. Commands: migrate | bootstrap-owner --email <e> --name <n> | seed-dev", args[0]);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command '{Command}' failed.", args[0]);
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
