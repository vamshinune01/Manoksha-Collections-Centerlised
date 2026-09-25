using Manoksha.SharedKernel;

namespace Manoksha.Modules.Inventory.Domain;

internal enum TransferStatus
{
    Requested = 1,
    Approved = 2,
    Prepared = 3,
    InTransit = 4,
    Received = 5,
    Discrepancy = 6,
    Rejected = 7,
    Cancelled = 8,
}

/// <summary>Branch-to-branch stock transfer (SPEC §10, §27.5; ADR-001 §7).</summary>
internal sealed class Transfer : Entity
{
    private Transfer()
    {
    }

    public Transfer(string number, Guid sourceBranchId, Guid destinationBranchId, string reason, Guid requestedBy, DateTimeOffset now)
    {
        Number = number;
        SourceBranchId = sourceBranchId;
        DestinationBranchId = destinationBranchId;
        Reason = reason;
        RequestedBy = requestedBy;
        RequestedAt = now;
        Status = TransferStatus.Requested;
    }

    public string Number { get; private set; } = default!;

    public Guid SourceBranchId { get; private set; }

    public Guid DestinationBranchId { get; private set; }

    public TransferStatus Status { get; private set; }

    public string Reason { get; private set; } = default!;

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>True when the Owner both initiated and authorized the transfer (ADR-001 §7).</summary>
    public bool OwnerSelfAuthorized { get; private set; }

    public string? DecisionNote { get; private set; }

    public Guid? PreparedBy { get; private set; }

    public DateTimeOffset? PreparedAt { get; private set; }

    public Guid? DispatchedBy { get; private set; }

    public DateTimeOffset? DispatchedAt { get; private set; }

    public Guid? ReceivedBy { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void Approve(Guid by, bool ownerSelfAuthorized, string? note, DateTimeOffset now)
    {
        Expect(TransferStatus.Requested);
        Status = TransferStatus.Approved;
        ApprovedBy = by;
        ApprovedAt = now;
        OwnerSelfAuthorized = ownerSelfAuthorized;
        DecisionNote = note;
    }

    public void Reject(Guid by, string note, DateTimeOffset now)
    {
        Expect(TransferStatus.Requested);
        Status = TransferStatus.Rejected;
        ApprovedBy = by;
        ApprovedAt = now;
        DecisionNote = note;
    }

    public void MarkPrepared(Guid by, DateTimeOffset now)
    {
        Expect(TransferStatus.Approved);
        Status = TransferStatus.Prepared;
        PreparedBy = by;
        PreparedAt = now;
    }

    public void MarkDispatched(Guid by, DateTimeOffset now)
    {
        Expect(TransferStatus.Prepared);
        Status = TransferStatus.InTransit;
        DispatchedBy = by;
        DispatchedAt = now;
    }

    public void MarkReceived(Guid by, bool hasDiscrepancy, DateTimeOffset now)
    {
        Expect(TransferStatus.InTransit);
        Status = hasDiscrepancy ? TransferStatus.Discrepancy : TransferStatus.Received;
        ReceivedBy = by;
        ReceivedAt = now;
    }

    public void MarkDiscrepanciesResolved()
    {
        Expect(TransferStatus.Discrepancy);
        Status = TransferStatus.Received;
    }

    public void Cancel(Guid by, string note, DateTimeOffset now)
    {
        if (Status is not (TransferStatus.Requested or TransferStatus.Approved or TransferStatus.Prepared))
        {
            throw new BusinessRuleException("TRANSFER_STATUS_INVALID", $"A transfer that is {Status} cannot be cancelled.");
        }
        Status = TransferStatus.Cancelled;
        CancelledBy = by;
        CancelledAt = now;
        DecisionNote = note;
    }

    private void Expect(TransferStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessRuleException("TRANSFER_STATUS_INVALID", $"This action needs the transfer to be {expected}, but it is {Status}.");
        }
    }
}

internal sealed class TransferLine : Entity
{
    private TransferLine()
    {
    }

    public TransferLine(Guid transferId, Guid skuId, bool serialized, int requestedQty)
    {
        if (requestedQty is <= 0 or > 100_000)
        {
            throw new BusinessRuleException("QUANTITY_INVALID", "Quantity must be between 1 and 100,000.", 400);
        }
        TransferId = transferId;
        SkuId = skuId;
        Serialized = serialized;
        RequestedQty = requestedQty;
    }

    public Guid TransferId { get; private set; }

    public Guid SkuId { get; private set; }

    public bool Serialized { get; private set; }

    public int RequestedQty { get; private set; }

    public int PreparedQty { get; set; }

    public int DispatchedQty { get; set; }

    public int ReceivedQty { get; set; }

    /// <summary>Missing units later resolved (received late / returned / written off).</summary>
    public int ResolvedQty { get; set; }

    public int OutstandingQty => DispatchedQty - ReceivedQty - ResolvedQty;
}

internal sealed class TransferLineItem
{
    private TransferLineItem()
    {
    }

    public TransferLineItem(Guid transferLineId, Guid itemId)
    {
        TransferLineId = transferLineId;
        ItemId = itemId;
    }

    public Guid TransferLineId { get; private set; }

    public Guid ItemId { get; private set; }

    /// <summary>Null while in transit; RECEIVED / RECEIVED_LATE / RETURNED / WRITTEN_OFF afterwards.</summary>
    public string? Outcome { get; set; }
}

/// <summary>Cost basis carried by a transfer: the source layers consumed at dispatch (ADR-001 §3).</summary>
internal sealed class TransferCostAllocation : Entity
{
    private TransferCostAllocation()
    {
    }

    public TransferCostAllocation(Guid transferLineId, Guid sourceLayerId, DateTimeOffset layerDate, long layerSeq, decimal unitCost, int quantity)
    {
        TransferLineId = transferLineId;
        SourceLayerId = sourceLayerId;
        LayerDate = layerDate;
        LayerSeq = layerSeq;
        UnitCost = unitCost;
        Quantity = quantity;
    }

    public Guid TransferLineId { get; private set; }

    public Guid SourceLayerId { get; private set; }

    public DateTimeOffset LayerDate { get; private set; }

    public long LayerSeq { get; private set; }

    public decimal UnitCost { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Units of this allocation already settled (received, returned or written off).</summary>
    public int SettledQty { get; set; }

    public int Unsettled => Quantity - SettledQty;
}

internal enum DiscrepancyStatus
{
    Open = 1,
    Resolved = 2,
}

/// <summary>A difference between expected and actual stock that needs a decision (transfer receipt, stock count).</summary>
internal sealed class InventoryDiscrepancy : Entity
{
    private InventoryDiscrepancy()
    {
    }

    public InventoryDiscrepancy(string number, string sourceType, Guid sourceId, string sourceNumber, Guid branchId, Guid skuId, int expectedQty, int actualQty, Guid[] missingItemIds, DateTimeOffset now)
    {
        Number = number;
        SourceType = sourceType;
        SourceId = sourceId;
        SourceNumber = sourceNumber;
        BranchId = branchId;
        SkuId = skuId;
        ExpectedQty = expectedQty;
        ActualQty = actualQty;
        MissingItemIds = missingItemIds;
        Status = DiscrepancyStatus.Open;
        CreatedAt = now;
    }

    public string Number { get; private set; } = default!;

    /// <summary>TRANSFER or COUNT (FULFILLMENT from Phase 7).</summary>
    public string SourceType { get; private set; } = default!;

    public Guid SourceId { get; private set; }

    public string SourceNumber { get; private set; } = default!;

    public Guid BranchId { get; private set; }

    public Guid SkuId { get; private set; }

    public int ExpectedQty { get; private set; }

    public int ActualQty { get; private set; }

    public int Variance => ActualQty - ExpectedQty;

    public Guid[] MissingItemIds { get; private set; } = [];

    public DiscrepancyStatus Status { get; private set; }

    public string? Resolution { get; private set; }

    public string? ResolutionNotes { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void Resolve(string resolution, string notes, Guid by, DateTimeOffset now)
    {
        if (Status != DiscrepancyStatus.Open)
        {
            throw new BusinessRuleException("DISCREPANCY_ALREADY_RESOLVED", "This discrepancy is already resolved.");
        }
        Status = DiscrepancyStatus.Resolved;
        Resolution = resolution;
        ResolutionNotes = notes;
        ResolvedBy = by;
        ResolvedAt = now;
    }

    public void AppendNote(string note) => ResolutionNotes = string.IsNullOrEmpty(ResolutionNotes) ? note : $"{ResolutionNotes}\n{note}";
}

internal enum StockCountStatus
{
    Open = 1,
    Submitted = 2,
}

internal sealed class StockCount : Entity
{
    private StockCount()
    {
    }

    public StockCount(string number, Guid branchId, string? notes, Guid createdBy, DateTimeOffset now)
    {
        Number = number;
        BranchId = branchId;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAt = now;
        Status = StockCountStatus.Open;
    }

    public string Number { get; private set; } = default!;

    public Guid BranchId { get; private set; }

    public StockCountStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? SubmittedBy { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void Submit(Guid by, DateTimeOffset now)
    {
        if (Status != StockCountStatus.Open)
        {
            throw new BusinessRuleException("COUNT_ALREADY_SUBMITTED", "This stock count was already submitted.");
        }
        Status = StockCountStatus.Submitted;
        SubmittedBy = by;
        SubmittedAt = now;
    }
}

internal sealed class StockCountLine
{
    private StockCountLine()
    {
    }

    public StockCountLine(Guid countId, Guid skuId)
    {
        CountId = countId;
        SkuId = skuId;
    }

    public Guid CountId { get; private set; }

    public Guid SkuId { get; private set; }

    public int? CountedQty { get; set; }

    /// <summary>System quantity captured at submission (hidden from counters while the count is open).</summary>
    public int? SystemQty { get; set; }
}

internal enum AdjustmentKind
{
    /// <summary>Move units between on-hand statuses (e.g. AVAILABLE → DAMAGED, LOST → AVAILABLE).</summary>
    StatusChange = 1,

    /// <summary>Remove DAMAGED/LOST units from stock; their FIFO cost is consumed as a loss.</summary>
    WriteOff = 2,

    /// <summary>Add units found in a count; the approver supplies the unit cost (ADR-001 §16).</summary>
    Found = 3,
}

internal enum AdjustmentStatus
{
    Pending = 1,
    Applied = 2,
    Rejected = 3,
}

/// <summary>An inventory correction that always needs a different, authorized approver (ADR-001 §14–16).</summary>
internal sealed class InventoryAdjustment : Entity
{
    private InventoryAdjustment()
    {
    }

    public InventoryAdjustment(string number, Guid branchId, Guid skuId, AdjustmentKind kind, InventoryStatus? fromStatus, InventoryStatus? toStatus,
        int quantity, Guid[] itemIds, string reasonCode, string notes, Guid? discrepancyId, Guid requestedBy, DateTimeOffset now)
    {
        Number = number;
        BranchId = branchId;
        SkuId = skuId;
        Kind = kind;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Quantity = quantity;
        ItemIds = itemIds;
        ReasonCode = reasonCode;
        Notes = notes;
        DiscrepancyId = discrepancyId;
        RequestedBy = requestedBy;
        RequestedAt = now;
        Status = AdjustmentStatus.Pending;
    }

    public string Number { get; private set; } = default!;

    public Guid BranchId { get; private set; }

    public Guid SkuId { get; private set; }

    public AdjustmentKind Kind { get; private set; }

    public InventoryStatus? FromStatus { get; private set; }

    public InventoryStatus? ToStatus { get; private set; }

    public int Quantity { get; private set; }

    public Guid[] ItemIds { get; private set; } = [];

    public string ReasonCode { get; private set; } = default!;

    public string Notes { get; private set; } = default!;

    public Guid? DiscrepancyId { get; private set; }

    public AdjustmentStatus Status { get; private set; }

    public Guid RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public Guid? DecidedBy { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecisionNote { get; private set; }

    public decimal? UnitCost { get; private set; }

    public decimal? ValueAtCost { get; private set; }

    public bool RequiredOwner { get; private set; }

    public uint RowVersion { get; private set; }

    public void Apply(Guid by, string? note, decimal? unitCost, decimal value, bool requiredOwner, DateTimeOffset now)
    {
        EnsurePending();
        Status = AdjustmentStatus.Applied;
        DecidedBy = by;
        DecidedAt = now;
        DecisionNote = note;
        UnitCost = unitCost;
        ValueAtCost = value;
        RequiredOwner = requiredOwner;
    }

    public void Reject(Guid by, string note, DateTimeOffset now)
    {
        EnsurePending();
        Status = AdjustmentStatus.Rejected;
        DecidedBy = by;
        DecidedAt = now;
        DecisionNote = note;
    }

    private void EnsurePending()
    {
        if (Status != AdjustmentStatus.Pending)
        {
            throw new BusinessRuleException("ADJUSTMENT_ALREADY_DECIDED", $"This adjustment is already {Status}.");
        }
    }
}
