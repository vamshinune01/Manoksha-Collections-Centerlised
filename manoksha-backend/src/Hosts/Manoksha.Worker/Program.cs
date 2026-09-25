using Manoksha.Hosting;
using Manoksha.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddManokshaCore(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<HousekeepingJob>();
builder.Services.AddHostedService<WalletIntegrityJob>();

var host = builder.Build();
await host.RunAsync();
