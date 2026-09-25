using Manoksha.SharedKernel;

namespace Manoksha.Modules.Inventory.Domain;

/// <summary>Inventory unit statuses (SPEC §9).</summary>
internal enum InventoryStatus
{
    Received = 1,
    Available = 2,
    Reserved = 3,
    Sold = 4,
    Delivered = 5,
    TransferPending = 6,
    InTransit = 7,
    Damaged = 8,
    Returned = 9,
    Repair = 10,
    Lost = 11,
    Blocked = 12,
}

/// <summary>Quantity-tracked stock: one row per (SKU, branch, status).</summary>
internal sealed class StockLevel
{
    private StockLevel()
    {
    }

    public Guid SkuId { get; private set; }

    public Guid BranchId { get; private set; }

    public InventoryStatus Status { get; private set; }

    public int Quantity { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

/// <summary>One physical piece of a serialized SKU, with its own permanent barcode.</summary>
internal sealed class InventoryItem : Entity
{
    private InventoryItem()
    {
    }

    public InventoryItem(Guid id, Guid skuId, Guid branchId, InventoryStatus status, string barcode, string sourceType, Guid sourceId, DateTimeOffset now)
        : base(id)
    {
        SkuId = skuId;
        BranchId = branchId;
        Status = status;
        Barcode = barcode;
        SourceType = sourceType;
        SourceId = sourceId;
        ReceivedAt = now;
        UpdatedAt = now;
    }

    public Guid SkuId { get; private set; }

    public Guid BranchId { get; private set; }

    public InventoryStatus Status { get; private set; }

    public string Barcode { get; private set; } = default!;

    public string SourceType { get; private set; } = default!;

    public Guid SourceId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Set when a damaged/lost piece is written off; the record is kept for history.</summary>
    public DateTimeOffset? WrittenOffAt { get; private set; }
}

/// <summary>
/// FIFO cost layer per SKU per branch (ADR-001 §3). Ordered by <see cref="LayerDate"/> (original receipt date — carried
/// across transfers) then <see cref="Seq"/>.
/// </summary>
internal sealed class CostLayer : Entity
{
    private CostLayer()
    {
    }

    public CostLayer(Guid skuId, Guid branchId, DateTimeOffset layerDate, decimal unitCost, int quantity, string sourceType, Guid sourceId, Guid? originLayerId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (unitCost < 0 || !Money.HasValidScale(unitCost))
        {
            throw new BusinessRuleException("UNIT_COST_INVALID", "Unit cost must be a non-negative amount with at most 2 decimals.", 400);
        }
        SkuId = skuId;
        BranchId = branchId;
        LayerDate = layerDate;
        UnitCost = unitCost;
        OriginalQty = quantity;
        RemainingQty = quantity;
        SourceType = sourceType;
        SourceId = sourceId;
        OriginLayerId = originLayerId;
        CreatedAt = now;
    }

    public long Seq { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid BranchId { get; private set; }

    public DateTimeOffset LayerDate { get; private set; }

    public decimal UnitCost { get; private set; }

    public int OriginalQty { get; private set; }

    public int RemainingQty { get; private set; }

    public string SourceType { get; private set; } = default!;

    public Guid SourceId { get; private set; }

    /// <summary>For layers created by a transfer: the source-branch layer whose cost basis was carried over.</summary>
    public Guid? OriginLayerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int Take(int wanted)
    {
        var taken = Math.Min(wanted, RemainingQty);
        RemainingQty -= taken;
        return taken;
    }
}

/// <summary>Append-only record of FIFO cost consumed (basis of gross profit and write-off cost).</summary>
internal sealed class CostLayerConsumption : Entity
{
    private CostLayerConsumption()
    {
    }

    public CostLayerConsumption(Guid layerId, int quantity, decimal unitCost, string reason, string referenceType, Guid referenceId, DateTimeOffset now)
    {
        LayerId = layerId;
        Quantity = quantity;
        UnitCost = unitCost;
        Reason = reason;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        OccurredAt = now;
    }

    public Guid LayerId { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public string Reason { get; private set; } = default!;

    public string ReferenceType { get; private set; } = default!;

    public Guid ReferenceId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>Append-only history of every inventory location/status change (SPEC §9).</summary>
internal sealed class InventoryMovement : Entity
{
    private InventoryMovement()
    {
    }

    public InventoryMovement(Guid skuId, Guid? itemId, int quantity, Guid? fromBranchId, Guid? toBranchId, InventoryStatus? fromStatus, InventoryStatus? toStatus,
        string movementType, string referenceType, Guid referenceId, string? referenceNumber, Guid? actorUserId, string? reason, DateTimeOffset now)
    {
        SkuId = skuId;
        ItemId = itemId;
        Quantity = quantity;
        FromBranchId = fromBranchId;
        ToBranchId = toBranchId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        MovementType = movementType;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        ReferenceNumber = referenceNumber;
        ActorUserId = actorUserId;
        Reason = reason;
        OccurredAt = now;
    }

    public long Seq { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid? ItemId { get; private set; }

    public int Quantity { get; private set; }

    public Guid? FromBranchId { get; private set; }

    public Guid? ToBranchId { get; private set; }

    public InventoryStatus? FromStatus { get; private set; }

    public InventoryStatus? ToStatus { get; private set; }

    public string MovementType { get; private set; } = default!;

    public string ReferenceType { get; private set; } = default!;

    public Guid ReferenceId { get; private set; }

    public string? ReferenceNumber { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
