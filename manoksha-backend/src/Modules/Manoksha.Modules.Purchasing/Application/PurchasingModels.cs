namespace Manoksha.Modules.Purchasing.Application;

public sealed record SupplierDto(Guid Id, string Code, string Name, string? ContactName, string? Mobile, string? Email, string? Gstin, string? Address, bool IsActive);

public sealed record SaveSupplierRequest(string Name, string? ContactName, string? Mobile, string? Email, string? Gstin, string? Address, bool IsActive, string Reason);

public sealed record PoLineRequest(Guid SkuId, int OrderedQty, decimal ExpectedUnitCost);

public sealed record CreatePurchaseOrderRequest(Guid SupplierId, Guid ReceivingBranchId, string? SupplierReference, DateOnly? ExpectedDate, string? Notes, IReadOnlyList<PoLineRequest> Lines, string Reason);

public sealed record UpdatePurchaseOrderRequest(Guid SupplierId, Guid ReceivingBranchId, string? SupplierReference, DateOnly? ExpectedDate, string? Notes, string Reason);

/// <summary>Amends an issued PO: change ordered quantity/cost of a line, or add a line (ADR-001 §13).</summary>
public sealed record AmendLineRequest(Guid? LineId, Guid? SkuId, int OrderedQty, decimal ExpectedUnitCost, string Reason);

public sealed record PoReasonRequest(string Reason);

public sealed record PoLineDto(Guid Id, Guid SkuId, string SkuCode, string ProductName, string VariantName, string TrackingMode,
    int OrderedQty, decimal? ExpectedUnitCost, int ReceivedQty, int DamagedQty, int RemainingQty);

public sealed record PurchaseOrderDto(
    Guid Id,
    string Number,
    string Status,
    Guid SupplierId,
    string SupplierName,
    Guid ReceivingBranchId,
    string ReceivingBranchName,
    string? SupplierReference,
    DateOnly? ExpectedDate,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? ClosedAt,
    string? CloseReason,
    decimal? ExpectedTotal,
    IReadOnlyList<PoLineDto> Lines);

public sealed record ReceiptLineRequest(Guid PoLineId, int ReceivedQty, int DamagedQty, decimal UnitCost);

public sealed record CreateGoodsReceiptRequest(string SupplierInvoiceRef, DateTimeOffset? ReceivedAt, string? Notes, IReadOnlyList<ReceiptLineRequest> Lines);

public sealed record GoodsReceiptLineDto(Guid Id, Guid PoLineId, Guid SkuId, string SkuCode, string ProductName, string VariantName,
    int ReceivedQty, int DamagedQty, int AcceptedQty, decimal? UnitCost);

public sealed record ReceivedItemDto(Guid ItemId, Guid BarcodeId, string Barcode, Guid SkuId, string Status);

public sealed record GoodsReceiptDto(
    Guid Id,
    string Number,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    string BranchName,
    string SupplierInvoiceRef,
    DateTimeOffset ReceivedAt,
    Guid ReceivedBy,
    string? Notes,
    decimal? TotalCost,
    IReadOnlyList<GoodsReceiptLineDto> Lines,
    IReadOnlyList<ReceivedItemDto> Items);
