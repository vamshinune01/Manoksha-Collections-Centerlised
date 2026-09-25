using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Inventory.Application;

internal sealed class StockAllocator(ManokshaDbContext db, StockEngine engine, ICatalogLookup catalog) : IStockAllocator
{
    public async Task<BasketAllocation> TrySellBasketAsync(Guid branchId, IReadOnlyList<BasketLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Stock allocation must run inside a transaction.");
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).ToList(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); // flush pending work so the savepoint boundary is clean
        var savepoint = $"alloc_{Guid.NewGuid():N}";
        await tx.CreateSavepointAsync(savepoint, cancellationToken);
        var trackedBefore = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);

        var ctx = new MovementContext("SALE", referenceType, referenceId, referenceNumber, null);
        var shortfalls = new List<LineShortfall>();
        var sold = new List<(BasketLine Line, List<Guid> Items)>();
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
                await engine.MoveItemsAsync(items, line.SkuId, branchId, InventoryStatus.Available, InventoryStatus.Sold, ctx, ct: cancellationToken);
                sold.Add((line, items));
            }
            else
            {
                try
                {
                    await engine.RemoveQuantityAsync(line.SkuId, branchId, InventoryStatus.Available, line.Quantity, ctx, recordedToStatus: InventoryStatus.Sold, ct: cancellationToken);
                    sold.Add((line, []));
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
        foreach (var (line, items) in sold)
        {
            var costs = await engine.ConsumeFifoAsync(line.SkuId, branchId, line.Quantity, "SALE", referenceType, referenceId, cancellationToken);
            allocated.Add(new AllocatedLine(line.SkuId, line.Quantity, items, Money.Round(costs.Sum(c => c.Quantity * c.UnitCost))));
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.ReleaseSavepointAsync(savepoint, cancellationToken);
        return new BasketAllocation(true, allocated, []);
    }
}
