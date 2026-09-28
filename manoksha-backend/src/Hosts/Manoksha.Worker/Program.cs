using Manoksha.Hosting;
using Manoksha.Hosting.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddManokshaCore(builder.Configuration, builder.Environment);
builder.Services.AddManokshaBackgroundJobs();

var host = builder.Build();
await host.RunAsync();
