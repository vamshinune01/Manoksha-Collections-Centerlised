namespace Manoksha.Application.Abstractions;

/// <summary>A business event published through the transactional outbox.</summary>
public interface IIntegrationEvent
{
    /// <summary>Stable event type name, e.g. "identity.internal_user_created".</summary>
    static abstract string EventType { get; }
}

/// <summary>Adds an event to the current unit of work; it is dispatched by the Worker after commit.</summary>
public interface IOutbox
{
    void Enqueue<TEvent>(TEvent @event) where TEvent : IIntegrationEvent;
}

/// <summary>Handles one outbox event type in the Worker. Handlers must be idempotent.</summary>
public interface IOutboxEventHandler
{
    string EventType { get; }

    Task HandleAsync(string payloadJson, CancellationToken cancellationToken);
}
