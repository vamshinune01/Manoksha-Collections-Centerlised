using Manoksha.Application.Security;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Inventory.Application;

internal sealed record CostAllocation(Guid LayerId, long LayerSeq, DateTimeOffset LayerDate, decimal UnitCost, int Quantity);

internal sealed record MovementContext(string MovementType, string ReferenceType, Guid ReferenceId, string? ReferenceNumber, string? Reason);

/// <summary>
/// The only code that changes stock. Every primitive runs inside the caller's transaction and is concurrency-safe:
/// quantity changes are conditional UPDATEs (never read-modify-write), serialized items change only from an expected
/// status, and FIFO layers are row-locked. Every change writes an append-only movement (SPEC §9, §13).
/// Callers must process SKUs in a deterministic order (by SKU id) to avoid deadlocks.
/// </summary>
internal sealed class StockEngine(ManokshaDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task AddQuantityAsync(Guid skuId, Guid branchId, InventoryStatus status, int quantity, MovementContext ctx, InventoryStatus? fromStatus = null, Guid? fromBranchId = null, CancellationToken ct = default)
    {
        Positive(quantity);
        var now = clock.UtcNow;
        var s = status.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory.stock_levels (sku_id, branch_id, status, quantity, updated_at)
            VALUES ({skuId}, {branchId}, {s}, {quantity}, {now})
            ON CONFLICT (sku_id, branch_id, status) DO UPDATE SET quantity = stock_levels.quantity + EXCLUDED.quantity, updated_at = EXCLUDED.updated_at
            """, ct);
        Record(skuId, null, quantity, fromBranchId, branchId, fromStatus, status, ctx);
    }

    /// <summary>Removes units from a status. Fails (nothing changes) when fewer units are available.</summary>
    public async Task RemoveQuantityAsync(Guid skuId, Guid branchId, InventoryStatus status, int quantity, MovementContext ctx, bool recordMovement = true, CancellationToken ct = default)
    {
        Positive(quantity);
        var s = status.ToString();
        var now = clock.UtcNow;
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory.stock_levels SET quantity = quantity - {quantity}, updated_at = {now}
            WHERE sku_id = {skuId} AND branch_id = {branchId} AND status = {s} AND quantity >= {quantity}
            """, ct);
        if (updated == 0)
        {
            var have = await QuantityAsync(skuId, branchId, status, ct);
            var ex = new BusinessRuleException("INSUFFICIENT_STOCK", $"Only {have} unit(s) are {status} at this branch; {quantity} needed.", 409);
            ex.Details["skuId"] = skuId;
            ex.Details["available"] = have;
            throw ex;
        }
        if (recordMovement)
        {
            Record(skuId, null, quantity, branchId, null, status, null, ctx);
        }
    }

    public async Task MoveQuantityAsync(Guid skuId, Guid branchId, InventoryStatus from, InventoryStatus to, int quantity, MovementContext ctx, Guid? toBranchId = null, CancellationToken ct = default)
    {
        await RemoveQuantityAsync(skuId, branchId, from, quantity, ctx, recordMovement: false, ct);
        var destination = toBranchId ?? branchId;
        var now = clock.UtcNow;
        var s = to.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inventory.stock_levels (sku_id, branch_id, status, quantity, updated_at)
            VALUES ({skuId}, {destination}, {s}, {quantity}, {now})
            ON CONFLICT (sku_id, branch_id, status) DO UPDATE SET quantity = stock_levels.quantity + EXCLUDED.quantity, updated_at = EXCLUDED.updated_at
            """, ct);
        Record(skuId, null, quantity, branchId, destination, from, to, ctx);
    }

    public async Task<int> QuantityAsync(Guid skuId, Guid branchId, InventoryStatus status, CancellationToken ct = default) =>
        await db.Set<StockLevel>().Where(l => l.SkuId == skuId && l.BranchId == branchId && l.Status == status).Select(l => l.Quantity).SingleOrDefaultAsync(ct);

    /// <summary>
    /// Changes serialized items from an expected status (and branch). All-or-nothing: if any item is not in the expected
    /// state — e.g. already sold, reserved or scanned twice — nothing changes.
    /// </summary>
    public async Task MoveItemsAsync(IReadOnlyCollection<Guid> itemIds, Guid skuId, Guid branchId, InventoryStatus from, InventoryStatus to, MovementContext ctx, Guid? toBranchId = null, CancellationToken ct = default)
    {
        if (itemIds.Count == 0)
        {
            return;
        }
        var ids = itemIds.Distinct().ToArray();
        if (ids.Length != itemIds.Count)
        {
            throw new BusinessRuleException("ITEM_DUPLICATED", "The same item was listed twice.", 400);
        }
        var destination = toBranchId ?? branchId;
        var now = clock.UtcNow;
        var f = from.ToString();
        var t = to.ToString();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory.inventory_items SET status = {t}, branch_id = {destination}, updated_at = {now}
            WHERE id = ANY({ids}) AND sku_id = {skuId} AND branch_id = {branchId} AND status = {f} AND written_off_at IS NULL
            """, ct);
        if (updated != ids.Length)
        {
            throw new BusinessRuleException("ITEM_NOT_AVAILABLE",
                $"{ids.Length - updated} of the selected item(s) are not {from} at this branch (already moved, sold or not this SKU).", 409);
        }
        foreach (var id in ids)
        {
            Record(skuId, id, 1, branchId, destination, from, to, ctx);
        }
    }

    public async Task WriteOffItemsAsync(IReadOnlyCollection<Guid> itemIds, Guid skuId, Guid branchId, InventoryStatus from, MovementContext ctx, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToArray();
        var now = clock.UtcNow;
        var f = from.ToString();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE inventory.inventory_items SET written_off_at = {now}, updated_at = {now}
            WHERE id = ANY({ids}) AND sku_id = {skuId} AND branch_id = {branchId} AND status = {f} AND written_off_at IS NULL
            """, ct);
        if (updated != ids.Length)
        {
            throw new BusinessRuleException("ITEM_NOT_AVAILABLE", $"Some selected items are not {from} at this branch.", 409);
        }
        foreach (var id in ids)
        {
            Record(skuId, id, 1, branchId, null, from, null, ctx);
        }
    }

    public InventoryItem CreateItem(Guid itemId, Guid skuId, Guid branchId, InventoryStatus status, string barcode, MovementContext ctx)
    {
        var item = new InventoryItem(itemId, skuId, branchId, status, barcode, ctx.ReferenceType, ctx.ReferenceId, clock.UtcNow);
        db.Add(item);
        Record(skuId, itemId, 1, null, branchId, null, status, ctx);
        return item;
    }

    public CostLayer CreateLayer(Guid skuId, Guid branchId, DateTimeOffset layerDate, decimal unitCost, int quantity, string sourceType, Guid sourceId, Guid? originLayerId = null)
    {
        var layer = new CostLayer(skuId, branchId, layerDate, unitCost, quantity, sourceType, sourceId, originLayerId, clock.UtcNow);
        db.Add(layer);
        return layer;
    }

    /// <summary>Consumes the oldest cost layers first (FIFO per SKU per branch). Layers are row-locked.</summary>
    public async Task<IReadOnlyList<CostAllocation>> ConsumeFifoAsync(Guid skuId, Guid branchId, int quantity, string reason, string referenceType, Guid referenceId, CancellationToken ct = default)
    {
        Positive(quantity);
        var layers = await db.Set<CostLayer>()
            .FromSqlInterpolated($"""
                SELECT * FROM inventory.cost_layers
                WHERE sku_id = {skuId} AND branch_id = {branchId} AND remaining_qty > 0
                ORDER BY layer_date, seq
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var needed = quantity;
        var allocations = new List<CostAllocation>();
        foreach (var layer in layers)
        {
            if (needed == 0)
            {
                break;
            }
            var taken = layer.Take(needed);
            needed -= taken;
            allocations.Add(new CostAllocation(layer.Id, layer.Seq, layer.LayerDate, layer.UnitCost, taken));
            db.Add(new CostLayerConsumption(layer.Id, taken, layer.UnitCost, reason, referenceType, referenceId, clock.UtcNow));
        }
        if (needed > 0)
        {
            // Stock and cost layers must always agree; this signals an integrity problem, never a user error.
            throw new BusinessRuleException("COST_LAYERS_INSUFFICIENT", "Cost records do not cover this stock. The operation was not applied; please report it.", 409);
        }
        return allocations;
    }

    /// <summary>Most recent purchase/transfer-in unit cost at the branch, else company-wide; null when never received.</summary>
    public async Task<decimal?> LatestUnitCostAsync(Guid skuId, Guid branchId, CancellationToken ct = default) =>
        await db.Set<CostLayer>().Where(l => l.SkuId == skuId && l.BranchId == branchId).OrderByDescending(l => l.Seq).Select(l => (decimal?)l.UnitCost).FirstOrDefaultAsync(ct)
        ?? await db.Set<CostLayer>().Where(l => l.SkuId == skuId).OrderByDescending(l => l.Seq).Select(l => (decimal?)l.UnitCost).FirstOrDefaultAsync(ct);

    private void Record(Guid skuId, Guid? itemId, int quantity, Guid? fromBranch, Guid? toBranch, InventoryStatus? from, InventoryStatus? to, MovementContext ctx) =>
        db.Add(new InventoryMovement(skuId, itemId, quantity, fromBranch, toBranch, from, to, ctx.MovementType, ctx.ReferenceType, ctx.ReferenceId, ctx.ReferenceNumber,
            currentUser.UserIdOrNull, ctx.Reason, clock.UtcNow));

    private static void Positive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new BusinessRuleException("QUANTITY_INVALID", "Quantity must be greater than zero.", 400);
        }
    }
}

internal static class Numbering
{
    public static async Task<string> NextAsync(ManokshaDbContext db, string sequence, string prefix, CancellationToken ct)
    {
        // Fixed allow-list: sequence names are never built from input.
        FormattableString sql = sequence switch
        {
            "transfer_seq" => $"SELECT nextval('inventory.transfer_seq') AS \"Value\"",
            "discrepancy_seq" => $"SELECT nextval('inventory.discrepancy_seq') AS \"Value\"",
            "count_seq" => $"SELECT nextval('inventory.count_seq') AS \"Value\"",
            "adjustment_seq" => $"SELECT nextval('inventory.adjustment_seq') AS \"Value\"",
            _ => throw new ArgumentOutOfRangeException(nameof(sequence)),
        };
        var value = await db.Database.SqlQuery<long>(sql).SingleAsync(ct);
        return $"{prefix}-{value:D6}";
    }
}
