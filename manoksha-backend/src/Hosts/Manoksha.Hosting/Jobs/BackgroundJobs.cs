using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.Hosting.Jobs;

/// <summary>
/// The background jobs (design §16). The Worker host always runs them. A low-cost deployment can instead run them inside the API
/// (<c>Jobs:InProcess=true</c>): they then run while the API instance is alive. Every job is a singleton across instances
/// (advisory locks), and payment/reservation correctness never depends on job timing (validity is time-based).
/// </summary>
public static class BackgroundJobs
{
    public static IServiceCollection AddManokshaBackgroundJobs(this IServiceCollection services)
    {
        services.AddHostedService<OutboxDispatcher>();
        services.AddHostedService<HousekeepingJob>();
        services.AddHostedService<WalletIntegrityJob>();
        services.AddHostedService<ReservationSweeperJob>();
        services.AddHostedService<PaymentPollerJob>();
        services.AddHostedService<EmailSenderJob>();
        services.AddHostedService<NotificationDailyJob>();
        return services;
    }
}
