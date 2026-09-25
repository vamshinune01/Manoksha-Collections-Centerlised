namespace Manoksha.Modules.Inventory.Contracts;

/// <param name="AcceptedQty">Good units → AVAILABLE.</param>
/// <param name="DamagedQty">Damaged units → DAMAGED (still owned; part of the cost layer).</param>
/// <param name="UnitCost">Actual purchase cost per unit (INR).</param>
public sealed record ReceiptLinePosting(Guid SourceLineId, Guid SkuId, int AcceptedQty, int DamagedQty, decimal UnitCost);

public sealed record ReceiptPosting(Guid BranchId, Guid GoodsReceiptId, string GoodsReceiptNumber, DateTimeOffset ReceivedAt, IReadOnlyList<ReceiptLinePosting> Lines);

public sealed record ReceivedItem(Guid SourceLineId, Guid ItemId, Guid BarcodeId, string Barcode, string Status);

/// <summary>Creates stock from a verified goods receipt, inside the caller's transaction (Purchasing → Inventory).</summary>
public interface IStockReceiver
{
    Task<IReadOnlyList<ReceivedItem>> ReceiveAsync(ReceiptPosting posting, CancellationToken cancellationToken = default);
}
