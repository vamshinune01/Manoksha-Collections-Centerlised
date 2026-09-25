using Manoksha.Application.Abstractions;
using Manoksha.Persistence;
using Manoksha.Persistence.Outbox;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Worker.Jobs;

/// <summary>
/// Dispatches committed outbox events to their handlers. Rows are claimed with FOR UPDATE SKIP LOCKED, so several
/// worker instances never process the same message concurrently. Handler failures are retried with backoff and
/// never affect the business transaction that produced the event (SPEC §29).
/// </summary>
internal sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int MaxAttempts = 10;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox dispatch batch failed");
                processed = 0;
            }
            if (processed < BatchSize)
            {
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ManokshaDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var handlers = scope.ServiceProvider.GetServices<IOutboxEventHandler>().ToLookup(h => h.EventType);
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var messages = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM platform.outbox_messages
                WHERE processed_at IS NULL AND failed = false AND next_attempt_at <= {now}
                ORDER BY occurred_at
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            try
            {
                foreach (var handler in handlers[message.Type])
                {
                    await handler.HandleAsync(message.Payload, ct);
                }
                message.MarkProcessed(clock.UtcNow);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox message {Id} ({Type}) failed on attempt {Attempt}", message.Id, message.Type, message.Attempts + 1);
                message.MarkAttemptFailed(ex.Message, clock.UtcNow, MaxAttempts);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return messages.Count;
    }
}
