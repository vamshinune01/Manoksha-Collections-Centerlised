using Manoksha.Persistence;
using Manoksha.Persistence.Platform;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Worker.Jobs;

/// <summary>
/// Removes expired technical records (OTP challenges, idempotency keys, expired refresh tokens, old processed
/// outbox rows). Never touches business history. Singleton across instances via an advisory lock.
/// </summary>
internal sealed class HousekeepingJob(IServiceScopeFactory scopeFactory, ILogger<HousekeepingJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Housekeeping failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ManokshaDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await AdvisoryLocks.TryAcquireTransactionLockAsync(db, AdvisoryLocks.Housekeeping, ct))
        {
            return;
        }
        var otp = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.otp_challenges WHERE created_at < {now.AddDays(-1)}", ct);
        var idem = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM platform.idempotency_records WHERE expires_at < {now}", ct);
        var tokens = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.refresh_tokens WHERE expires_at < {now.AddDays(-30)}", ct);
        var outbox = await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM platform.outbox_messages WHERE processed_at < {now.AddDays(-30)}", ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Housekeeping removed {Otp} OTP challenges, {Idem} idempotency records, {Tokens} refresh tokens, {Outbox} outbox rows", otp, idem, tokens, outbox);
    }
}
