using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Branches.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record BranchCreated(Guid BranchId, string Code) : IIntegrationEvent
{
    public static string EventType => "branches.branch_created";
}

public sealed record FulfillmentPriorityChanged(int Version) : IIntegrationEvent
{
    public static string EventType => "branches.fulfillment_priority_changed";
}
