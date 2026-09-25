namespace Manoksha.Modules.Inventory.Contracts;

public sealed record BasketLine(Guid SkuId, int Quantity);

/// <param name="ItemIds">Serialized pieces sold (empty for quantity-tracked SKUs).</param>
/// <param name="CostAmount">FIFO cost consumed for this line (gross-profit basis).</param>
public sealed record AllocatedLine(Guid SkuId, int Quantity, IReadOnlyList<Guid> ItemIds, decimal CostAmount);

public sealed record LineShortfall(Guid SkuId, int Requested, int Available);

public sealed record BasketAllocation(bool Success, IReadOnlyList<AllocatedLine> Lines, IReadOnlyList<LineShortfall> Shortfalls);

/// <summary>
/// All-or-nothing stock allocation of a complete basket at ONE branch (SPEC §11: never split an order). Runs inside the caller's
/// transaction using a savepoint: on any shortfall nothing at that branch changes and the caller can try the next branch.
/// Concurrency-safe: quantities use conditional updates; pieces are claimed with FOR UPDATE SKIP LOCKED, so the same unit can
/// never be sold twice (SPEC §13).
/// </summary>
public interface IStockAllocator
{
    /// <summary>Sells the basket immediately (AVAILABLE → SOLD) and consumes FIFO cost — reseller orders.</summary>
    Task<BasketAllocation> TrySellBasketAsync(Guid branchId, IReadOnlyList<BasketLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default);
}
