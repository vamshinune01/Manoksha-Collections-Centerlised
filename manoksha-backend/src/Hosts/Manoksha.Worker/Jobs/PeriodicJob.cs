using Manoksha.Persistence;
using Manoksha.Persistence.Platform;

namespace Manoksha.Worker.Jobs;

/// <summary>
/// Runs a module job on a fixed cadence, as a singleton across worker instances (session advisory lock). The job itself runs
/// its own short transactions, so one failing item never blocks the rest.
/// </summary>
internal abstract class PeriodicJob(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    protected abstract long LockKey { get; }

    protected abstract string Name { get; }

    protected abstract Task<int> RunAsync(IServiceProvider scopedServices, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ManokshaDbContext>();
                if (await AdvisoryLocks.TryAcquireSessionLockAsync(db, LockKey, stoppingToken))
                {
                    try
                    {
                        var handled = await RunAsync(scope.ServiceProvider, stoppingToken);
                        if (handled > 0)
                        {
                            logger.LogInformation("{Job} handled {Count} item(s)", Name, handled);
                        }
                    }
                    finally
                    {
                        await AdvisoryLocks.ReleaseSessionLockAsync(db, LockKey);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "{Job} failed to run", Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Releases expired online-order reservations (SPEC §13; design §16: ~15 s).</summary>
internal sealed class ReservationSweeperJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ReservationSweeperJob> logger)
    : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(configuration.GetValue("Jobs:ReservationSweeperSeconds", 15.0));

    protected override long LockKey => AdvisoryLocks.ReservationSweeper;

    protected override string Name => "Reservation sweeper";

    protected override Task<int> RunAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        Manoksha.Modules.Orders.OrderJobs.ReleaseExpiredReservationsAsync(scopedServices, ct);
}

/// <summary>Polls the payment provider for missed webhooks and late successes (design §13, §16: 30–60 s).</summary>
internal sealed class PaymentPollerJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PaymentPollerJob> logger)
    : PeriodicJob(scopeFactory, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(configuration.GetValue("Jobs:PaymentPollerSeconds", 30.0));

    protected override long LockKey => AdvisoryLocks.PaymentPoller;

    protected override string Name => "Payment poller";

    protected override Task<int> RunAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        Manoksha.Modules.Payments.PaymentJobs.PollAsync(scopedServices, ct);
}
