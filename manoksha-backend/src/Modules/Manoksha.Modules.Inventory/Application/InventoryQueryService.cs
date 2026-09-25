using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Inventory.Application;

internal sealed class InventoryQueryService(ManokshaDbContext db, InventoryAccess access, ICatalogBarcodes barcodes)
{
    private static readonly InventoryStatus[] Unsold =
    [
        InventoryStatus.Received, InventoryStatus.Available, InventoryStatus.Reserved, InventoryStatus.TransferPending, InventoryStatus.InTransit,
        InventoryStatus.Damaged, InventoryStatus.Repair, InventoryStatus.Lost, InventoryStatus.Blocked,
    ];

    /// <summary>Stock per branch and SKU by status (quantity rows + serialized pieces), scoped to the caller's branches.</summary>
    public async Task<IReadOnlyList<StockRowDto>> StockAsync(Guid? branchId, Guid? skuId, CancellationToken ct)
    {
        var visible = await access.VisibleBranchesAsync(P.Inventory.View, ct);
        var levels = db.Set<StockLevel>().AsNoTracking().Where(l => l.Quantity > 0);
        var items = db.Set<InventoryItem>().AsNoTracking().Where(i => i.WrittenOffAt == null && Unsold.Contains(i.Status));
        if (visible is not null)
        {
            levels = levels.Where(l => visible.Contains(l.BranchId));
            items = items.Where(i => visible.Contains(i.BranchId));
        }
        if (branchId is { } b)
        {
            levels = levels.Where(l => l.BranchId == b);
            items = items.Where(i => i.BranchId == b);
        }
        if (skuId is { } s)
        {
            levels = levels.Where(l => l.SkuId == s);
            items = items.Where(i => i.SkuId == s);
        }
        var rows = (await levels.Select(l => new { l.BranchId, l.SkuId, l.Status, l.Quantity }).ToListAsync(ct))
            .Concat(await items.GroupBy(i => new { i.BranchId, i.SkuId, i.Status }).Select(g => new { g.Key.BranchId, g.Key.SkuId, g.Key.Status, Quantity = g.Count() }).ToListAsync(ct))
            .ToList();
        if (rows.Count == 0)
        {
            return [];
        }
        var skus = await access.SkusAsync(rows.Select(r => r.SkuId).Distinct().ToList(), ct);
        var names = await access.BranchNamesAsync(ct);
        return rows.GroupBy(r => new { r.BranchId, r.SkuId })
            .Select(g =>
            {
                var byStatus = g.GroupBy(x => x.Status.ToString()).ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
                var sku = skus[g.Key.SkuId];
                var onHand = g.Where(x => x.Status != InventoryStatus.InTransit && x.Status != InventoryStatus.Lost).Sum(x => x.Quantity);
                return new StockRowDto(g.Key.BranchId, names.GetValueOrDefault(g.Key.BranchId, "?"), sku.SkuId, sku.SkuCode, sku.ProductName, sku.VariantName, sku.TrackingMode,
                    byStatus, byStatus.GetValueOrDefault(nameof(InventoryStatus.Available)), onHand);
            })
            .OrderBy(r => r.BranchName).ThenBy(r => r.SkuCode)
            .ToList();
    }

    public async Task<IReadOnlyList<ItemDto>> ItemsAsync(Guid skuId, Guid? branchId, string? status, CancellationToken ct)
    {
        var visible = await access.VisibleBranchesAsync(P.Inventory.View, ct);
        var q = db.Set<InventoryItem>().AsNoTracking().Where(i => i.SkuId == skuId);
        if (visible is not null)
        {
            q = q.Where(i => visible.Contains(i.BranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(i => i.BranchId == b);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InventoryStatus>(status, true, out var st))
        {
            q = q.Where(i => i.Status == st);
        }
        var names = await access.BranchNamesAsync(ct);
        return (await q.OrderBy(i => i.ReceivedAt).Take(1000).ToListAsync(ct))
            .Select(i => new ItemDto(i.Id, i.SkuId, i.BranchId, names.GetValueOrDefault(i.BranchId, "?"), i.Status.ToString(), i.Barcode, i.ReceivedAt, i.WrittenOffAt is not null))
            .ToList();
    }

    public async Task<IReadOnlyList<MovementDto>> MovementsAsync(Guid? skuId, Guid? itemId, Guid? branchId, string? referenceNumber, long? beforeSeq, CancellationToken ct)
    {
        var visible = await access.VisibleBranchesAsync(P.Inventory.View, ct);
        var q = db.Set<InventoryMovement>().AsNoTracking();
        if (visible is not null)
        {
            q = q.Where(m => (m.FromBranchId != null && visible.Contains(m.FromBranchId.Value)) || (m.ToBranchId != null && visible.Contains(m.ToBranchId.Value)));
        }
        if (skuId is { } s)
        {
            q = q.Where(m => m.SkuId == s);
        }
        if (itemId is { } i)
        {
            q = q.Where(m => m.ItemId == i);
        }
        if (branchId is { } b)
        {
            q = q.Where(m => m.FromBranchId == b || m.ToBranchId == b);
        }
        if (!string.IsNullOrWhiteSpace(referenceNumber))
        {
            q = q.Where(m => m.ReferenceNumber == referenceNumber);
        }
        if (beforeSeq is { } before)
        {
            q = q.Where(m => m.Seq < before);
        }
        return await q.OrderByDescending(m => m.Seq).Take(200)
            .Select(m => new MovementDto(m.Seq, m.OccurredAt, m.SkuId, m.ItemId, m.Quantity, m.FromBranchId, m.ToBranchId,
                m.FromStatus == null ? null : m.FromStatus.ToString(), m.ToStatus == null ? null : m.ToStatus.ToString(),
                m.MovementType, m.ReferenceType, m.ReferenceNumber, m.ActorUserId, m.Reason))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Scanning returns product/variant, the piece's current location and status, and availability per branch the user can see
    /// (SPEC §7). Applicable price is added with pricing (Phase 4).
    /// </summary>
    public async Task<PosScanDto> ScanAsync(string code, CancellationToken ct)
    {
        var found = await barcodes.FindAsync(code, ct) ?? throw new NotFoundException("BARCODE_NOT_FOUND", "No product is registered for this barcode.");
        if (!found.IsActive)
        {
            throw new BusinessRuleException("BARCODE_RETIRED", "This barcode has been retired. Use the item's current label.", 410);
        }
        var names = await access.BranchNamesAsync(ct);
        InventoryItem? item = null;
        if (found.InventoryItemId is { } itemId)
        {
            item = await db.Set<InventoryItem>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId, ct);
        }

        var visible = await access.VisibleBranchesAsync(P.Inventory.View, ct);
        var availability = (await StockAsync(null, found.Sku.SkuId, ct))
            .Where(r => visible is null || visible.Contains(r.BranchId))
            .Select(r => new BranchAvailabilityDto(r.BranchId, r.BranchName, r.Available))
            .ToList();

        var sku = found.Sku;
        return new PosScanDto(found.Code, found.Kind, item?.Id, item?.WrittenOffAt is null ? item?.Status.ToString() : "WrittenOff", item?.BranchId,
            item is null ? null : names.GetValueOrDefault(item.BranchId), sku.SkuId, sku.SkuCode, sku.VariantId, sku.VariantName,
            found.Attributes.Select(a => new ScanAttributeDto(a.Attribute, a.Value)).ToList(), sku.ProductId, sku.ProductName, sku.ProductStatus, sku.TrackingMode,
            sku.AvailableForRetail, sku.AvailableForReseller, availability);
    }
}
