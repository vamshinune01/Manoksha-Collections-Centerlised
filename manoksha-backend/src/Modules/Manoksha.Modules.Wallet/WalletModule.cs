using Manoksha.Application.Modules;
using Manoksha.Modules.Wallet.Application;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Modules.Wallet.Persistence;
using Manoksha.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Manoksha.Modules.Wallet;

public sealed class WalletModule : IModule
{
    public string Name => "Wallet";

    public void AddServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IModuleModelConfiguration, WalletModelConfiguration>();
        services.AddScoped<IWallets, WalletService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Wallet ledger, deposits and endpoints arrive in Phase 5.
    }
}
