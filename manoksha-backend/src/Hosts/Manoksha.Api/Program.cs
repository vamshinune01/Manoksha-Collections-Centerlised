using System.Text.Json.Serialization;
using Manoksha.Api.Infrastructure;
using Manoksha.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddManokshaCore(builder.Configuration, builder.Environment);
builder.Services.AddManokshaApiModules(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddManokshaApi(builder.Configuration);

var app = builder.Build();

if (args.Length > 0 && !args[0].StartsWith('-'))
{
    // Operational commands (Cloud Run Job / local CLI): migrate, bootstrap-owner, seed-dev.
    Environment.ExitCode = await CliCommands.RunAsync(app, args);
    return;
}

await app.Services.InitialiseDatabaseAsync(applyMigrations: app.Configuration.GetValue<bool>("Database:MigrateOnStartup"));

app.UseManokshaApi();
foreach (var module in ModuleCatalog.All)
{
    module.MapEndpoints(app);
}

await app.RunAsync();

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
