using Manoksha.Application.Abstractions;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Inventory.Application;

/// <summary>
/// Stock of a confirmed order after a cancellation or reroute. Every change writes movement history; FIFO cost layers are
/// restored for units that are physically back (AVAILABLE or DAMAGED) with their original cost and layer date, so company cost
/// is unchanged; missing units stay consumed (a loss) and open an ORDER discrepancy for investigation (SPEC §9, §22).
/// </summary>
internal sealed class OrderStockService(ManokshaDbContext db, StockEngine engine, ICatalogLookup catalog, IAuditWriter audit, IClock clock) : IOrderStock
{
    public const string ReturnLayerSource = "ORDER_RETURN";

    public async Task<StockReturnResult> ReturnSoldStockAsync(Guid branchId, IReadOnlyList<SoldStockReturn> lines, string referenceType, Guid referenceId,
        string referenceNumber, string reason, CancellationToken cancellationToken = default)
    {
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).ToList(), cancellationToken);
        var ctx = new MovementContext("ORDER_RETURN", referenceType, referenceId, referenceNumber, reason);
        var discrepancies = new List<string>();
        var result = new List<ReturnedLine>();
        foreach (var line in lines.OrderBy(l => l.SkuId))
        {
            var serialized = skus[line.SkuId].TrackingMode == "Serialized";
            int good;
            if (serialized)
            {
                var all = line.ItemIds.Distinct().ToList();
                var damaged = line.DamagedItemIds.Distinct().ToList();
                var missing = line.MissingItemIds.Distinct().ToList();
                if (damaged.Concat(missing).Any(i => !all.Contains(i)) || damaged.Intersect(missing).Any())
                {
                    throw new BusinessRuleException("ORDER_ITEMS_INVALID", "Damaged or missing pieces must be pieces of this order, each listed once.", 400);
                }
                var back = all.Except(damaged).Except(missing).ToList();
                await engine.MoveItemsAsync(back, line.SkuId, branchId, InventoryStatus.Sold, InventoryStatus.Available, ctx, ct: cancellationToken);
                await engine.MoveItemsAsync(damaged, line.SkuId, branchId, InventoryStatus.Sold, InventoryStatus.Damaged, ctx, ct: cancellationToken);
                if (missing.Count > 0)
                {
                    await engine.MoveItemsAsync(missing, line.SkuId, branchId, InventoryStatus.Sold, InventoryStatus.Lost, ctx, ct: cancellationToken);
                    await engine.WriteOffItemsAsync(missing, line.SkuId, branchId, InventoryStatus.Lost, ctx, cancellationToken);
                }
                good = back.Count;
                result.Add(await FinishAsync(branchId, line.SkuId, good, damaged.Count, missing.Count, [.. missing], referenceType, referenceId, referenceNumber, discrepancies,
                    cancellationToken));
            }
            else
            {
                if (line.DamagedQty < 0 || line.MissingQty < 0 || line.DamagedQty + line.MissingQty > line.Quantity)
                {
                    throw new BusinessRuleException("ORDER_QTY_INVALID", "Damaged plus missing units cannot exceed the ordered quantity.", 400);
                }
                good = line.Quantity - line.DamagedQty - line.MissingQty;
                if (good > 0)
                {
                    await engine.AddQuantityAsync(line.SkuId, branchId, InventoryStatus.Available, good, ctx, fromStatus: InventoryStatus.Sold, ct: cancellationToken);
                }
                if (line.DamagedQty > 0)
                {
                    await engine.AddQuantityAsync(line.SkuId, branchId, InventoryStatus.Damaged, line.DamagedQty, ctx, fromStatus: InventoryStatus.Sold, ct: cancellationToken);
                }
                if (line.MissingQty > 0)
                {
                    engine.RecordMovement(line.SkuId, null, line.MissingQty, branchId, null, InventoryStatus.Sold, InventoryStatus.Lost, ctx);
                }
                result.Add(await FinishAsync(branchId, line.SkuId, good, line.DamagedQty, line.MissingQty, [], referenceType, referenceId, referenceNumber, discrepancies,
                    cancellationToken));
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return new StockReturnResult(discrepancies, result);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> AvailableAtBranchAsync(Guid branchId, IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default)
    {
        var ids = skuIds.Distinct().ToList();
        var result = ids.ToDictionary(id => id, _ => 0);
        foreach (var row in await db.Set<StockLevel>().AsNoTracking()
                     .Where(l => ids.Contains(l.SkuId) && l.BranchId == branchId && l.Status == InventoryStatus.Available)
                     .Select(l => new { l.SkuId, l.Quantity }).ToListAsync(cancellationToken))
        {
            result[row.SkuId] += row.Quantity;
        }
        foreach (var row in await db.Set<InventoryItem>().AsNoTracking()
                     .Where(i => ids.Contains(i.SkuId) && i.BranchId == branchId && i.Status == InventoryStatus.Available && i.WrittenOffAt == null)
                     .GroupBy(i => i.SkuId).Select(g => new { g.Key, Qty = g.Count() }).ToListAsync(cancellationToken))
        {
            result[row.Key] += row.Qty;
        }
        return result;
    }

    private async Task<ReturnedLine> FinishAsync(Guid branchId, Guid skuId, int good, int damaged, int missing, Guid[] missingItems, string referenceType, Guid referenceId,
        string referenceNumber, List<string> discrepancies, CancellationToken ct)
    {
        var restored = await RestoreLayersAsync(branchId, skuId, good + damaged, referenceType, referenceId, ct);
        if (missing > 0)
        {
            var d = new InventoryDiscrepancy(await Numbering.NextAsync(db, "discrepancy_seq", "DSC", ct), "ORDER", referenceId, referenceNumber, branchId, skuId,
                missing, 0, missingItems, clock.UtcNow);
            db.Add(d);
            discrepancies.Add(d.Number);
            await audit.RecordAsync(new AuditRecord("inventory.discrepancy.opened", "Discrepancy", d.Id.ToString(),
                After: new { d.Number, source = "ORDER", referenceNumber, skuId, missing }, BranchId: branchId), ct);
        }
        return new ReturnedLine(skuId, good, damaged, missing, restored);
    }

    /// <summary>Recreates the consumed cost layers (newest consumption first) for units physically back at the branch.</summary>
    private async Task<decimal> RestoreLayersAsync(Guid branchId, Guid skuId, int quantity, string referenceType, Guid referenceId, CancellationToken ct)
    {
        if (quantity <= 0)
        {
            return 0m;
        }
        var consumed = await (from c in db.Set<CostLayerConsumption>()
                              join l in db.Set<CostLayer>() on c.LayerId equals l.Id
                              where c.ReferenceType == referenceType && c.ReferenceId == referenceId && l.SkuId == skuId && l.BranchId == branchId
                              orderby c.OccurredAt descending
                              select new { Layer = l, c.Quantity }).ToListAsync(ct);
        var alreadyRestored = await db.Set<CostLayer>()
            .Where(l => l.SourceType == ReturnLayerSource && l.SourceId == referenceId && l.SkuId == skuId && l.BranchId == branchId && l.OriginLayerId != null)
            .GroupBy(l => l.OriginLayerId!.Value).Select(g => new { g.Key, Qty = g.Sum(x => x.OriginalQty) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var remaining = quantity;
        var cost = 0m;
        foreach (var group in consumed.GroupBy(c => c.Layer.Id))
        {
            if (remaining == 0)
            {
                break;
            }
            var layer = group.First().Layer;
            var open = group.Sum(g => g.Quantity) - alreadyRestored.GetValueOrDefault(layer.Id);
            var take = Math.Min(open, remaining);
            if (take <= 0)
            {
                continue;
            }
            engine.CreateLayer(skuId, branchId, layer.LayerDate, layer.UnitCost, take, ReturnLayerSource, referenceId, layer.Id);
            remaining -= take;
            cost += take * layer.UnitCost;
        }
        if (remaining > 0)
        {
            throw new BusinessRuleException("COST_LAYERS_INSUFFICIENT", "The sale's cost records do not cover the returned stock. Nothing was changed; please report it.", 409);
        }
        return Money.Round(cost);
    }
}
