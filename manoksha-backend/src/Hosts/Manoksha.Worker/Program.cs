using Manoksha.Hosting;
using Manoksha.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddManokshaCore(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<HousekeepingJob>();
builder.Services.AddHostedService<WalletIntegrityJob>();
builder.Services.AddHostedService<ReservationSweeperJob>();
builder.Services.AddHostedService<PaymentPollerJob>();

var host = builder.Build();
await host.RunAsync();
