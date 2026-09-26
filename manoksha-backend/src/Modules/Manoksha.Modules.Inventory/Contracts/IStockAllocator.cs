namespace Manoksha.Modules.Inventory.Contracts;

public sealed record BasketLine(Guid SkuId, int Quantity);

/// <param name="ItemIds">Serialized pieces sold (empty for quantity-tracked SKUs).</param>
/// <param name="CostAmount">FIFO cost consumed for this line (gross-profit basis); 0 for a reservation (cost is consumed at sale).</param>
public sealed record AllocatedLine(Guid SkuId, int Quantity, IReadOnlyList<Guid> ItemIds, decimal CostAmount);

public sealed record LineShortfall(Guid SkuId, int Requested, int Available);

public sealed record BasketAllocation(bool Success, IReadOnlyList<AllocatedLine> Lines, IReadOnlyList<LineShortfall> Shortfalls);

/// <summary>A line held in RESERVED status (the pieces for serialized SKUs).</summary>
public sealed record ReservedLine(Guid SkuId, int Quantity, IReadOnlyList<Guid> ItemIds);

/// <summary>
/// All-or-nothing stock allocation of a complete basket at ONE branch (SPEC §11: never split an order). Runs inside the caller's
/// transaction using a savepoint: on any shortfall nothing at that branch changes and the caller can try the next branch.
/// Concurrency-safe: quantities use conditional updates; pieces are claimed with FOR UPDATE SKIP LOCKED, so the same unit can
/// never be reserved or sold twice (SPEC §13).
/// </summary>
public interface IStockAllocator
{
    /// <summary>Sells the basket immediately (AVAILABLE → SOLD) and consumes FIFO cost — reseller orders and late-payment recovery.</summary>
    Task<BasketAllocation> TrySellBasketAsync(Guid branchId, IReadOnlyList<BasketLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Holds the basket for an online payment (AVAILABLE → RESERVED). No cost is consumed yet.</summary>
    Task<BasketAllocation> TryReserveBasketAsync(Guid branchId, IReadOnlyList<BasketLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Finalises a reservation (RESERVED → SOLD) and consumes FIFO cost. Throws if the reserved stock is not intact.</summary>
    Task<IReadOnlyList<AllocatedLine>> CommitReservedAsync(Guid branchId, IReadOnlyList<ReservedLine> lines, string referenceType, Guid referenceId, string referenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Returns reserved stock to AVAILABLE (payment failed, reservation expired).</summary>
    Task ReleaseReservedAsync(Guid branchId, IReadOnlyList<ReservedLine> lines, string referenceType, Guid referenceId, string referenceNumber, string reason,
        CancellationToken cancellationToken = default);
}

/// <summary>Storefront stock hint: AVAILABLE units per SKU across all branches (not a promise — checkout decides per branch).</summary>
public interface IStockAvailability
{
    Task<IReadOnlyDictionary<Guid, int>> AvailableQuantitiesAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default);
}
