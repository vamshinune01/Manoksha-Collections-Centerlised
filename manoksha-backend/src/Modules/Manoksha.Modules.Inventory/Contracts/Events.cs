using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Inventory.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

/// <summary>Expected and actual stock differ (SourceType TRANSFER, COUNT or ORDER); someone must resolve it.</summary>
public sealed record InventoryDiscrepancyOpened(Guid DiscrepancyId, string Number, string SourceType, string SourceNumber, Guid BranchId, Guid SkuId, int ExpectedQty,
    int ActualQty) : IIntegrationEvent
{
    public static string EventType => "inventory.discrepancy_opened";
}

/// <summary>A transfer waits for approval at the source branch.</summary>
public sealed record TransferRequested(Guid TransferId, string Number, Guid SourceBranchId, Guid DestinationBranchId) : IIntegrationEvent
{
    public static string EventType => "inventory.transfer_requested";
}

/// <summary>A stock adjustment waits for approval.</summary>
public sealed record AdjustmentRequested(Guid AdjustmentId, string Number, Guid BranchId, Guid SkuId, string Kind, int Quantity) : IIntegrationEvent
{
    public static string EventType => "inventory.adjustment_requested";
}
