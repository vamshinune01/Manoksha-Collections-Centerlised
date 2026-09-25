using Manoksha.Modules.Wallet;
using Manoksha.Persistence;
using Manoksha.Persistence.Platform;

namespace Manoksha.Worker.Jobs;

/// <summary>Nightly (configurable) wallet ledger integrity check. Singleton across worker instances via an advisory lock.</summary>
internal sealed class WalletIntegrityJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<WalletIntegrityJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(configuration.GetValue("Jobs:WalletIntegrityIntervalHours", 24.0));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ManokshaDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);
                if (await AdvisoryLocks.TryAcquireTransactionLockAsync(db, AdvisoryLocks.WalletIntegrity, stoppingToken))
                {
                    var issues = await WalletJobs.RunIntegrityCheckAsync(scope.ServiceProvider, stoppingToken);
                    logger.LogInformation("Wallet integrity check completed with {Issues} issue(s)", issues);
                }
                await tx.CommitAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Wallet integrity check failed to run");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
