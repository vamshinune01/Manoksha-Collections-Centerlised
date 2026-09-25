using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Inventory.Application;

/// <summary>
/// Posts a verified goods receipt (ADR-001 §4): accepted units become AVAILABLE, damaged units DAMAGED, one FIFO layer per line
/// at the actual unit cost. Serialized SKUs get one item + unique barcode per piece.
/// </summary>
internal sealed class StockReceiver(ManokshaDbContext db, StockEngine engine, InventoryAccess access, IItemBarcodeIssuer barcodes) : IStockReceiver
{
    public async Task<IReadOnlyList<ReceivedItem>> ReceiveAsync(ReceiptPosting posting, CancellationToken cancellationToken = default)
    {
        var skus = await access.SkusAsync(posting.Lines.Select(l => l.SkuId).Distinct().ToList(), cancellationToken);
        var items = new List<ReceivedItem>();
        foreach (var line in posting.Lines.OrderBy(l => l.SkuId))
        {
            var total = line.AcceptedQty + line.DamagedQty;
            if (line.AcceptedQty < 0 || line.DamagedQty < 0 || total == 0)
            {
                throw new BusinessRuleException("QUANTITY_INVALID", "Received quantities must be positive.", 400);
            }
            var ctx = new MovementContext("GOODS_RECEIPT", "GoodsReceipt", posting.GoodsReceiptId, posting.GoodsReceiptNumber, null);
            engine.CreateLayer(line.SkuId, posting.BranchId, posting.ReceivedAt, line.UnitCost, total, "GOODS_RECEIPT", line.SourceLineId);

            if (InventoryAccess.IsSerialized(skus[line.SkuId]))
            {
                for (var i = 0; i < total; i++)
                {
                    var status = i < line.AcceptedQty ? InventoryStatus.Available : InventoryStatus.Damaged;
                    var itemId = Uuid7.NewGuid();
                    var issued = await barcodes.IssueForItemAsync(line.SkuId, itemId, cancellationToken);
                    engine.CreateItem(itemId, line.SkuId, posting.BranchId, status, issued.Code, ctx);
                    items.Add(new ReceivedItem(line.SourceLineId, itemId, issued.BarcodeId, issued.Code, status.ToString()));
                }
            }
            else
            {
                if (line.AcceptedQty > 0)
                {
                    await engine.AddQuantityAsync(line.SkuId, posting.BranchId, InventoryStatus.Available, line.AcceptedQty, ctx, ct: cancellationToken);
                }
                if (line.DamagedQty > 0)
                {
                    await engine.AddQuantityAsync(line.SkuId, posting.BranchId, InventoryStatus.Damaged, line.DamagedQty, ctx, ct: cancellationToken);
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return items;
    }
}
