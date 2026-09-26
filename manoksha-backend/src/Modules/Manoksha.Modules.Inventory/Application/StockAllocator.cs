using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Inventory.Application;

internal sealed class StockAllocator(ManokshaDbContext db, StockEngine engine, ICatalogLookup catalog) : IStockAllocator, IStockAvailability
{
    public Task<BasketAllocation> TrySellBasketAsync(Guid branchId, IReadOnlyList<BasketLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default) =>
        TryAllocateAsync(branchId, lines, InventoryStatus.Sold, referenceType, referenceId, referenceNumber, cancellationToken);

    public Task<BasketAllocation> TryReserveBasketAsync(Guid branchId, IReadOnlyList<BasketLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default) =>
        TryAllocateAsync(branchId, lines, InventoryStatus.Reserved, referenceType, referenceId, referenceNumber, cancellationToken);

    public async Task<IReadOnlyList<AllocatedLine>> CommitReservedAsync(Guid branchId, IReadOnlyList<ReservedLine> lines, string referenceType, Guid referenceId,
        string referenceNumber, CancellationToken cancellationToken = default)
    {
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).ToList(), cancellationToken);
        var ctx = new MovementContext("SALE", referenceType, referenceId, referenceNumber, null);
        var result = new List<AllocatedLine>();
        foreach (var line in lines.OrderBy(l => l.SkuId))
        {
            if (skus[line.SkuId].TrackingMode == "Serialized")
            {
                await engine.MoveItemsAsync([.. line.ItemIds], line.SkuId, branchId, InventoryStatus.Reserved, InventoryStatus.Sold, ctx, ct: cancellationToken);
            }
            else
            {
                await engine.RemoveQuantityAsync(line.SkuId, branchId, InventoryStatus.Reserved, line.Quantity, ctx, recordedToStatus: InventoryStatus.Sold, ct: cancellationToken);
            }
            var costs = await engine.ConsumeFifoAsync(line.SkuId, branchId, line.Quantity, "SALE", referenceType, referenceId, cancellationToken);
            result.Add(new AllocatedLine(line.SkuId, line.Quantity, line.ItemIds, Money.Round(costs.Sum(c => c.Quantity * c.UnitCost))));
        }
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task ReleaseReservedAsync(Guid branchId, IReadOnlyList<ReservedLine> lines, string referenceType, Guid referenceId, string referenceNumber, string reason,
        CancellationToken cancellationToken = default)
    {
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).ToList(), cancellationToken);
        var ctx = new MovementContext("RELEASE", referenceType, referenceId, referenceNumber, reason);
        foreach (var line in lines.OrderBy(l => l.SkuId))
        {
            if (skus[line.SkuId].TrackingMode == "Serialized")
            {
                await engine.MoveItemsAsync([.. line.ItemIds], line.SkuId, branchId, InventoryStatus.Reserved, InventoryStatus.Available, ctx, ct: cancellationToken);
            }
            else
            {
                await engine.MoveQuantityAsync(line.SkuId, branchId, InventoryStatus.Reserved, InventoryStatus.Available, line.Quantity, ctx, ct: cancellationToken);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> AvailableQuantitiesAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default)
    {
        var skus = await catalog.FindSkusAsync(skuIds, cancellationToken);
        var serialized = skus.Values.Where(s => s.TrackingMode == "Serialized").Select(s => s.SkuId).ToList();
        var quantity = skus.Values.Where(s => s.TrackingMode != "Serialized").Select(s => s.SkuId).ToList();
        var result = new Dictionary<Guid, int>();
        foreach (var row in await db.Set<StockLevel>().AsNoTracking()
                     .Where(l => quantity.Contains(l.SkuId) && l.Status == InventoryStatus.Available)
                     .GroupBy(l => l.SkuId).Select(g => new { g.Key, Qty = g.Sum(l => l.Quantity) }).ToListAsync(cancellationToken))
        {
            result[row.Key] = row.Qty;
        }
        foreach (var row in await db.Set<InventoryItem>().AsNoTracking()
                     .Where(i => serialized.Contains(i.SkuId) && i.Status == InventoryStatus.Available && i.WrittenOffAt == null)
                     .GroupBy(i => i.SkuId).Select(g => new { g.Key, Qty = g.Count() }).ToListAsync(cancellationToken))
        {
            result[row.Key] = row.Qty;
        }
        return result;
    }

    private async Task<BasketAllocation> TryAllocateAsync(Guid branchId, IReadOnlyList<BasketLine> lines, InventoryStatus target, string referenceType, Guid referenceId,
        string referenceNumber, CancellationToken cancellationToken)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Stock allocation must run inside a transaction.");
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).ToList(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); // flush pending work so the savepoint boundary is clean
        var savepoint = $"alloc_{Guid.NewGuid():N}";
        await tx.CreateSavepointAsync(savepoint, cancellationToken);
        var trackedBefore = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);

        var ctx = new MovementContext(target == InventoryStatus.Sold ? "SALE" : "RESERVE", referenceType, referenceId, referenceNumber, null);
        var shortfalls = new List<LineShortfall>();
        var taken = new List<(BasketLine Line, List<Guid> Items)>();
        foreach (var line in lines.OrderBy(l => l.SkuId))
        {
            if (!skus.TryGetValue(line.SkuId, out var sku))
            {
                throw new NotFoundException("SKU_NOT_FOUND", "A requested item does not exist.");
            }
            if (sku.TrackingMode == "Serialized")
            {
                var items = await engine.ClaimAvailableItemsAsync(line.SkuId, branchId, line.Quantity, cancellationToken);
                if (items.Count < line.Quantity)
                {
                    shortfalls.Add(new LineShortfall(line.SkuId, line.Quantity, items.Count));
                    continue;
                }
                await engine.MoveItemsAsync(items, line.SkuId, branchId, InventoryStatus.Available, target, ctx, ct: cancellationToken);
                taken.Add((line, items));
            }
            else
            {
                try
                {
                    if (target == InventoryStatus.Sold)
                    {
                        await engine.RemoveQuantityAsync(line.SkuId, branchId, InventoryStatus.Available, line.Quantity, ctx, recordedToStatus: InventoryStatus.Sold, ct: cancellationToken);
                    }
                    else
                    {
                        await engine.MoveQuantityAsync(line.SkuId, branchId, InventoryStatus.Available, target, line.Quantity, ctx, ct: cancellationToken);
                    }
                    taken.Add((line, []));
                }
                catch (BusinessRuleException ex) when (ex.Code == "INSUFFICIENT_STOCK")
                {
                    shortfalls.Add(new LineShortfall(line.SkuId, line.Quantity, (int)(ex.Details["available"] ?? 0)));
                }
            }
        }

        if (shortfalls.Count > 0)
        {
            await tx.RollbackToSavepointAsync(savepoint, cancellationToken);
            foreach (var entry in db.ChangeTracker.Entries().Where(e => !trackedBefore.Contains(e.Entity)).ToList())
            {
                entry.State = EntityState.Detached;
            }
            return new BasketAllocation(false, [], shortfalls);
        }

        var allocated = new List<AllocatedLine>();
        foreach (var (line, items) in taken)
        {
            var cost = 0m;
            if (target == InventoryStatus.Sold)
            {
                var costs = await engine.ConsumeFifoAsync(line.SkuId, branchId, line.Quantity, "SALE", referenceType, referenceId, cancellationToken);
                cost = Money.Round(costs.Sum(c => c.Quantity * c.UnitCost));
            }
            allocated.Add(new AllocatedLine(line.SkuId, line.Quantity, items, cost));
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.ReleaseSavepointAsync(savepoint, cancellationToken);
        return new BasketAllocation(true, allocated, []);
    }
}
