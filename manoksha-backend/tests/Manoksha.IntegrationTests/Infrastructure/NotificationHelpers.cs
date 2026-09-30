using System.Collections.Concurrent;
using Manoksha.Application.Abstractions;
using Manoksha.Hosting.Jobs;
using Manoksha.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Manoksha.IntegrationTests.Infrastructure;

/// <summary>Test email provider: records every message; addresses in <see cref="FailFor"/> fail like a provider outage.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public ConcurrentDictionary<string, bool> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailFor.ContainsKey(message.To))
        {
            throw new InvalidOperationException("Simulated email provider failure");
        }
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) => Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();
}

public static class NotificationHelpers
{
    /// <summary>Runs the outbox dispatcher (what the worker does continuously) until nothing is due.</summary>
    public static async Task DispatchOutboxAsync(this ManokshaApiFactory factory)
    {
        var dispatcher = new OutboxDispatcher(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxDispatcher>.Instance);
        for (var i = 0; i < 200 && await dispatcher.DispatchBatchAsync(CancellationToken.None) > 0; i++)
        {
        }
    }

    public static async Task<int> SendEmailsAsync(this ManokshaApiFactory factory)
    {
        var total = 0;
        for (var i = 0; i < 100; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sent = await NotificationJobs.SendDueEmailsAsync(scope.ServiceProvider, CancellationToken.None);
            total += sent;
            if (sent == 0)
            {
                break;
            }
        }
        return total;
    }

    public static async Task RunDailyNotificationsAsync(this ManokshaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await NotificationJobs.RunDailyAsync(scope.ServiceProvider, CancellationToken.None);
    }
}
