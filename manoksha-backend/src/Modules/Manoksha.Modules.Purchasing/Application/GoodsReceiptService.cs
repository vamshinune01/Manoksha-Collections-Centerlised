using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Purchasing.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Purchasing.Application;

/// <summary>
/// Goods receipt at the PO's receiving branch by an authorized Inventory Employee / Branch Manager (ADR-001 §4).
/// Records ordered vs received vs damaged and the actual cost, then creates inventory in the same transaction. Idempotent
/// by Idempotency-Key so a double submit cannot create stock twice.
/// </summary>
internal sealed class GoodsReceiptService(
    ManokshaDbContext db,
    IIdempotencyService idempotency,
    IPermissionService permissions,
    PurchaseOrderService orders,
    IStockReceiver stock,
    IBranchDirectory branches,
    ICatalogLookup catalog,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public Task<GoodsReceiptDto> ReceiveAsync(Guid purchaseOrderId, CreateGoodsReceiptRequest r, string idempotencyKey, CancellationToken ct) =>
        idempotency.ExecuteAsync($"purchasing.goods_receipt:{purchaseOrderId}", idempotencyKey, r, innerCt => ReceiveCoreAsync(purchaseOrderId, r, innerCt), ct);

    private async Task<GoodsReceiptDto> ReceiveCoreAsync(Guid purchaseOrderId, CreateGoodsReceiptRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.SupplierInvoiceRef))
        {
            throw new BusinessRuleException("INVOICE_REF_REQUIRED", "Enter the supplier invoice / delivery reference.", 400);
        }
        var requested = r.Lines ?? [];
        if (requested.Count == 0 || requested.Select(l => l.PoLineId).Distinct().Count() != requested.Count)
        {
            throw new BusinessRuleException("GRN_LINES_INVALID", "Add at least one received line, each PO line only once.", 400);
        }

        // Lock the PO so concurrent receipts against the same PO serialize (prevents over-receipt races).
        var po = await db.Set<PurchaseOrder>()
            .FromSqlInterpolated($"SELECT *, xmin FROM purchasing.purchase_orders WHERE id = {purchaseOrderId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("PO_NOT_FOUND", "Purchase order not found.");
        await permissions.EnsurePermissionForBranchAsync(P.Purchasing.GoodsReceiptRecord, po.ReceivingBranchId, ct);
        if (!po.IsReceivable)
        {
            throw new BusinessRuleException("PO_STATUS_INVALID", $"Goods cannot be received against a {po.Status} purchase order.");
        }
        var branch = await branches.FindAsync(po.ReceivingBranchId, ct);
        if (branch is not { IsActive: true })
        {
            throw new BusinessRuleException("BRANCH_INACTIVE", "The receiving branch is inactive.");
        }

        var now = clock.UtcNow;
        var receivedAt = r.ReceivedAt ?? now;
        if (receivedAt > now.AddMinutes(5))
        {
            throw new BusinessRuleException("RECEIVED_AT_INVALID", "Receipt time cannot be in the future.", 400);
        }

        var lines = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po.Id).ToDictionaryAsync(l => l.Id, ct);
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('purchasing.grn_seq') AS \"Value\"").SingleAsync(ct);
        var grn = new GoodsReceipt($"GRN-{seq:D6}", po.Id, po.SupplierId, po.ReceivingBranchId, r.SupplierInvoiceRef.Trim(), receivedAt, r.Notes?.Trim(), currentUser.UserId, now);
        db.Add(grn);

        var postings = new List<ReceiptLinePosting>();
        var grnLines = new List<GoodsReceiptLine>();
        foreach (var req in requested)
        {
            var poLine = lines.GetValueOrDefault(req.PoLineId) ?? throw new NotFoundException("PO_LINE_NOT_FOUND", "A received line does not belong to this purchase order.");
            var line = new GoodsReceiptLine(grn.Id, poLine.Id, poLine.SkuId, req.ReceivedQty, req.DamagedQty, req.UnitCost);
            poLine.Receive(req.ReceivedQty, req.DamagedQty);
            db.Add(line);
            grnLines.Add(line);
            postings.Add(new ReceiptLinePosting(line.Id, line.SkuId, line.AcceptedQty, line.DamagedQty, line.UnitCost));
        }
        await db.SaveChangesAsync(ct);

        var items = await stock.ReceiveAsync(new ReceiptPosting(po.ReceivingBranchId, grn.Id, grn.Number, receivedAt, postings), ct);
        await orders.RefreshStatusAsync(po, ct);

        await audit.RecordAsync(new AuditRecord("purchasing.goods_receipt.recorded", "GoodsReceipt", grn.Id.ToString(),
            After: new
            {
                grn.Number,
                po = po.Number,
                grn.SupplierInvoiceRef,
                lines = grnLines.Select(l => new { l.SkuId, l.ReceivedQty, l.DamagedQty, l.AcceptedQty, l.UnitCost }),
                poStatus = po.Status.ToString(),
            }, BranchId: po.ReceivingBranchId), ct);
        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(grn, grnLines, items.Select(i => new ReceivedItemDto(i.ItemId, i.BarcodeId, i.Barcode,
            grnLines.Single(l => l.Id == i.SourceLineId).SkuId, i.Status)).ToList(), await permissions.HasPermissionAsync(P.Purchasing.View, ct), ct);
    }

    public async Task<IReadOnlyList<GoodsReceiptDto>> ListAsync(Guid? purchaseOrderId, Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var showCost = access.Has(P.Purchasing.View);
        var q = db.Set<GoodsReceipt>().AsNoTracking();
        if (!showCost)
        {
            var allowed = access.BranchesWith(P.Purchasing.GoodsReceiptRecord);
            if (allowed is not null)
            {
                q = q.Where(g => allowed.Contains(g.BranchId));
            }
        }
        if (purchaseOrderId is { } poId)
        {
            q = q.Where(g => g.PurchaseOrderId == poId);
        }
        if (branchId is { } b)
        {
            q = q.Where(g => g.BranchId == b);
        }
        var receipts = await q.OrderByDescending(g => g.ReceivedAt).Take(200).ToListAsync(ct);
        var ids = receipts.Select(g => g.Id).ToList();
        var lines = await db.Set<GoodsReceiptLine>().AsNoTracking().Where(l => ids.Contains(l.GoodsReceiptId)).ToListAsync(ct);
        var result = new List<GoodsReceiptDto>();
        foreach (var g in receipts)
        {
            result.Add(await ToDtoAsync(g, lines.Where(l => l.GoodsReceiptId == g.Id).ToList(), [], showCost, ct));
        }
        return result;
    }

    private async Task<GoodsReceiptDto> ToDtoAsync(GoodsReceipt g, IReadOnlyList<GoodsReceiptLine> lines, IReadOnlyList<ReceivedItemDto> items, bool showCost, CancellationToken ct)
    {
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).Distinct().ToList(), ct);
        var po = await db.Set<PurchaseOrder>().AsNoTracking().Where(x => x.Id == g.PurchaseOrderId).Select(x => x.Number).SingleAsync(ct);
        var supplier = await db.Set<Supplier>().AsNoTracking().Where(s => s.Id == g.SupplierId).Select(s => s.Name).SingleAsync(ct);
        var branch = await branches.FindAsync(g.BranchId, ct);
        return new GoodsReceiptDto(g.Id, g.Number, g.PurchaseOrderId, po, g.SupplierId, supplier, g.BranchId, branch?.Name ?? "?", g.SupplierInvoiceRef, g.ReceivedAt,
            g.ReceivedBy, g.Notes, showCost ? Money.Round(lines.Sum(l => l.ReceivedQty * l.UnitCost)) : null,
            lines.Select(l => new GoodsReceiptLineDto(l.Id, l.PurchaseOrderLineId, l.SkuId, skus[l.SkuId].SkuCode, skus[l.SkuId].ProductName, skus[l.SkuId].VariantName,
                l.ReceivedQty, l.DamagedQty, l.AcceptedQty, showCost ? l.UnitCost : null)).ToList(),
            items);
    }
}
