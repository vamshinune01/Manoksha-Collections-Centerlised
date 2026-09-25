using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.SharedKernel;

namespace Manoksha.Persistence.Outbox;

internal sealed class TransactionalOutbox(ManokshaDbContext db, IClock clock) : IOutbox
{
    public void Enqueue<TEvent>(TEvent @event) where TEvent : IIntegrationEvent
    {
        var payload = JsonSerializer.Serialize(@event, JsonDefaults.Options);
        db.OutboxMessages.Add(new OutboxMessage(TEvent.EventType, payload, clock.UtcNow));
    }
}
