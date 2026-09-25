namespace Manoksha.Modules.Inventory.Application;

public sealed record StockRowDto(
    Guid BranchId,
    string BranchName,
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string VariantName,
    string TrackingMode,
    IReadOnlyDictionary<string, int> ByStatus,
    int Available,
    int OnHand);

public sealed record ItemDto(Guid Id, Guid SkuId, Guid BranchId, string BranchName, string Status, string Barcode, DateTimeOffset ReceivedAt, bool WrittenOff);

public sealed record MovementDto(
    long Seq,
    DateTimeOffset OccurredAt,
    Guid SkuId,
    Guid? ItemId,
    int Quantity,
    Guid? FromBranchId,
    Guid? ToBranchId,
    string? FromStatus,
    string? ToStatus,
    string MovementType,
    string ReferenceType,
    string? ReferenceNumber,
    Guid? ActorUserId,
    string? Reason);

public sealed record PosScanDto(
    string Barcode,
    string BarcodeKind,
    Guid? InventoryItemId,
    string? ItemStatus,
    Guid? ItemBranchId,
    string? ItemBranchName,
    Guid SkuId,
    string SkuCode,
    Guid VariantId,
    string VariantName,
    IReadOnlyList<ScanAttributeDto> Attributes,
    Guid ProductId,
    string ProductName,
    string ProductStatus,
    string TrackingMode,
    bool AvailableForRetail,
    bool AvailableForReseller,
    decimal? RetailPrice,
    IReadOnlyList<BranchAvailabilityDto> Availability);

public sealed record ScanAttributeDto(string AttributeName, string Value);

public sealed record BranchAvailabilityDto(Guid BranchId, string BranchName, int Available);

// Transfers
public sealed record TransferLineRequest(Guid SkuId, int Quantity);

public sealed record CreateTransferRequest(Guid SourceBranchId, Guid DestinationBranchId, IReadOnlyList<TransferLineRequest> Lines, string Reason);

public sealed record DecisionRequest(string? Note);

public sealed record InventoryReasonRequest(string Reason);

public sealed record LineQuantityRequest(Guid LineId, int? Quantity, IReadOnlyList<Guid>? ItemIds);

public sealed record LinesRequest(IReadOnlyList<LineQuantityRequest> Lines);

public sealed record TransferLineDto(Guid Id, Guid SkuId, string SkuCode, string ProductName, string VariantName, bool Serialized,
    int RequestedQty, int PreparedQty, int DispatchedQty, int ReceivedQty, int ResolvedQty, int OutstandingQty, IReadOnlyList<TransferItemDto> Items);

public sealed record TransferItemDto(Guid ItemId, string Barcode, string? Outcome);

public sealed record TransferDto(
    Guid Id,
    string Number,
    string Status,
    Guid SourceBranchId,
    string SourceBranchName,
    Guid DestinationBranchId,
    string DestinationBranchName,
    string Reason,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    bool OwnerSelfAuthorized,
    string? DecisionNote,
    DateTimeOffset? PreparedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<TransferLineDto> Lines);

// Discrepancies
public sealed record DiscrepancyDto(
    Guid Id,
    string Number,
    string SourceType,
    Guid SourceId,
    string SourceNumber,
    Guid BranchId,
    string BranchName,
    Guid SkuId,
    string SkuCode,
    string ProductName,
    int ExpectedQty,
    int ActualQty,
    int Variance,
    int OutstandingQty,
    IReadOnlyList<Guid> MissingItemIds,
    string Status,
    string? Resolution,
    string? ResolutionNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

/// <param name="Action">RECEIVED_LATE, RETURNED_TO_SOURCE or WRITTEN_OFF (transfer); DISMISSED (count).</param>
public sealed record ResolveDiscrepancyRequest(string Action, int? Quantity, IReadOnlyList<Guid>? ItemIds, string Notes);

// Counts
public sealed record CreateCountRequest(Guid BranchId, IReadOnlyList<Guid>? SkuIds, string? Notes);

public sealed record CountLineRequest(Guid SkuId, int CountedQty);

public sealed record RecordCountsRequest(IReadOnlyList<CountLineRequest> Lines);

public sealed record CountLineDto(Guid SkuId, string SkuCode, string ProductName, string VariantName, int? CountedQty, int? SystemQty, int? Variance);

public sealed record CountDto(Guid Id, string Number, Guid BranchId, string BranchName, string Status, string? Notes, Guid CreatedBy, DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt, IReadOnlyList<CountLineDto> Lines, IReadOnlyList<Guid> DiscrepancyIds);

// Adjustments
public sealed record CreateAdjustmentRequest(
    Guid BranchId,
    Guid SkuId,
    string Kind,
    string? FromStatus,
    string? ToStatus,
    int? Quantity,
    IReadOnlyList<Guid>? ItemIds,
    string ReasonCode,
    string Notes,
    Guid? DiscrepancyId);

public sealed record ApproveAdjustmentRequest(string? Note, decimal? UnitCost);

public sealed record AdjustmentDto(
    Guid Id,
    string Number,
    Guid BranchId,
    string BranchName,
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string Kind,
    string? FromStatus,
    string? ToStatus,
    int Quantity,
    IReadOnlyList<Guid> ItemIds,
    string ReasonCode,
    string Notes,
    Guid? DiscrepancyId,
    string Status,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    decimal? UnitCost,
    decimal? ValueAtCost,
    decimal? EstimatedValue,
    bool? RequiresOwner);
