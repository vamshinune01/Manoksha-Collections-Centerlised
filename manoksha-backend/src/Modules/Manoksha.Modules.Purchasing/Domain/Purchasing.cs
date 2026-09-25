using Manoksha.SharedKernel;

namespace Manoksha.Modules.Purchasing.Domain;

internal sealed class Supplier : Entity
{
    private Supplier()
    {
    }

    public Supplier(string code, string name, DateTimeOffset now)
    {
        Code = code;
        Name = name.Trim();
        IsActive = true;
        CreatedAt = now;
    }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? ContactName { get; private set; }

    public string? MobileE164 { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Recorded as an attribute only (GST engine is postponed, SPEC §35).</summary>
    public string? Gstin { get; private set; }

    public string? Address { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, string? contactName, string? mobile, string? email, string? gstin, string? address, bool isActive)
    {
        Name = name.Trim();
        ContactName = contactName?.Trim();
        MobileE164 = mobile;
        Email = email?.Trim();
        Gstin = gstin?.Trim().ToUpperInvariant();
        Address = address?.Trim();
        IsActive = isActive;
    }
}

internal enum PurchaseOrderStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyReceived = 3,
    Received = 4,
    Closed = 5,
    Cancelled = 6,
}

/// <summary>Supplier → PO (with expected receiving branch, ADR-001 §4) → goods receipt(s) → inventory.</summary>
internal sealed class PurchaseOrder : Entity
{
    private PurchaseOrder()
    {
    }

    public PurchaseOrder(string number, Guid supplierId, Guid receivingBranchId, string? supplierReference, DateOnly? expectedDate, string? notes, Guid createdBy, DateTimeOffset now)
    {
        Number = number;
        SupplierId = supplierId;
        ReceivingBranchId = receivingBranchId;
        SupplierReference = supplierReference;
        ExpectedDate = expectedDate;
        Notes = notes;
        CreatedBy = createdBy;
        CreatedAt = now;
        Status = PurchaseOrderStatus.Draft;
    }

    public string Number { get; private set; } = default!;

    public Guid SupplierId { get; private set; }

    public Guid ReceivingBranchId { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    public string? SupplierReference { get; private set; }

    public DateOnly? ExpectedDate { get; private set; }

    public string? Notes { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? IssuedBy { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public Guid? ClosedBy { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? CloseReason { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsReceivable => Status is PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived;

    public bool IsAmendable => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Issued or PurchaseOrderStatus.PartiallyReceived;

    public void UpdateHeader(Guid supplierId, Guid receivingBranchId, string? supplierReference, DateOnly? expectedDate, string? notes)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new BusinessRuleException("PO_NOT_DRAFT", "Supplier and receiving branch can only change while the PO is a draft.");
        }
        SupplierId = supplierId;
        ReceivingBranchId = receivingBranchId;
        SupplierReference = supplierReference;
        ExpectedDate = expectedDate;
        Notes = notes;
    }

    public void Issue(Guid by, DateTimeOffset now)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new BusinessRuleException("PO_STATUS_INVALID", "Only a draft PO can be issued.");
        }
        Status = PurchaseOrderStatus.Issued;
        IssuedBy = by;
        IssuedAt = now;
    }

    public void RefreshReceiptStatus(bool fullyReceived) =>
        Status = fullyReceived ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;

    public void ReopenAfterAmendment()
    {
        if (Status == PurchaseOrderStatus.Received)
        {
            Status = PurchaseOrderStatus.PartiallyReceived;
        }
    }

    public void Close(Guid by, string reason, DateTimeOffset now)
    {
        if (!IsReceivable)
        {
            throw new BusinessRuleException("PO_STATUS_INVALID", "Only an issued or partially received PO can be closed.");
        }
        Status = PurchaseOrderStatus.Closed;
        ClosedBy = by;
        ClosedAt = now;
        CloseReason = reason;
    }

    public void Cancel(Guid by, string reason, bool anythingReceived, DateTimeOffset now)
    {
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Issued) || anythingReceived)
        {
            throw new BusinessRuleException("PO_STATUS_INVALID", "Only a draft or issued PO with nothing received can be cancelled; close it instead.");
        }
        Status = PurchaseOrderStatus.Cancelled;
        ClosedBy = by;
        ClosedAt = now;
        CloseReason = reason;
    }
}

internal sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine()
    {
    }

    public PurchaseOrderLine(Guid purchaseOrderId, Guid skuId, int orderedQty, decimal expectedUnitCost)
    {
        PurchaseOrderId = purchaseOrderId;
        SkuId = skuId;
        SetOrdered(orderedQty);
        SetExpectedCost(expectedUnitCost);
    }

    public Guid PurchaseOrderId { get; private set; }

    public Guid SkuId { get; private set; }

    public int OrderedQty { get; private set; }

    public decimal ExpectedUnitCost { get; private set; }

    /// <summary>Total units received so far (good + damaged).</summary>
    public int ReceivedQty { get; private set; }

    public int DamagedQty { get; private set; }

    public int RemainingQty => Math.Max(0, OrderedQty - ReceivedQty);

    public void SetOrdered(int qty)
    {
        if (qty is <= 0 or > 1_000_000)
        {
            throw new BusinessRuleException("QUANTITY_INVALID", "Ordered quantity must be between 1 and 1,000,000.", 400);
        }
        if (qty < ReceivedQty)
        {
            throw new BusinessRuleException("PO_LINE_BELOW_RECEIVED", $"Ordered quantity cannot be less than the {ReceivedQty} already received.");
        }
        OrderedQty = qty;
    }

    public void SetExpectedCost(decimal cost)
    {
        if (cost < 0 || !Money.HasValidScale(cost))
        {
            throw new BusinessRuleException("UNIT_COST_INVALID", "Unit cost must be a non-negative amount with at most 2 decimals.", 400);
        }
        ExpectedUnitCost = cost;
    }

    /// <summary>Over-receipt is blocked: amend the PO first (ADR-001 §13).</summary>
    public void Receive(int received, int damaged)
    {
        if (received > RemainingQty)
        {
            throw new BusinessRuleException("OVER_RECEIPT_NOT_ALLOWED",
                $"Receiving {received} exceeds the {RemainingQty} still on order. Amend the purchase order first.", 422);
        }
        ReceivedQty += received;
        DamagedQty += damaged;
    }
}

internal sealed class GoodsReceipt : Entity
{
    private GoodsReceipt()
    {
    }

    public GoodsReceipt(string number, Guid purchaseOrderId, Guid supplierId, Guid branchId, string supplierInvoiceRef, DateTimeOffset receivedAt, string? notes, Guid receivedBy, DateTimeOffset now)
    {
        Number = number;
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        BranchId = branchId;
        SupplierInvoiceRef = supplierInvoiceRef;
        ReceivedAt = receivedAt;
        Notes = notes;
        ReceivedBy = receivedBy;
        CreatedAt = now;
    }

    public string Number { get; private set; } = default!;

    public Guid PurchaseOrderId { get; private set; }

    public Guid SupplierId { get; private set; }

    public Guid BranchId { get; private set; }

    public string SupplierInvoiceRef { get; private set; } = default!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public string? Notes { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class GoodsReceiptLine : Entity
{
    private GoodsReceiptLine()
    {
    }

    public GoodsReceiptLine(Guid goodsReceiptId, Guid purchaseOrderLineId, Guid skuId, int receivedQty, int damagedQty, decimal unitCost)
    {
        if (receivedQty <= 0 || damagedQty < 0 || damagedQty > receivedQty)
        {
            throw new BusinessRuleException("QUANTITY_INVALID", "Received must be positive and damaged between 0 and received.", 400);
        }
        if (unitCost < 0 || !Money.HasValidScale(unitCost))
        {
            throw new BusinessRuleException("UNIT_COST_INVALID", "Unit cost must be a non-negative amount with at most 2 decimals.", 400);
        }
        GoodsReceiptId = goodsReceiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        SkuId = skuId;
        ReceivedQty = receivedQty;
        DamagedQty = damagedQty;
        UnitCost = unitCost;
    }

    public Guid GoodsReceiptId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public Guid SkuId { get; private set; }

    public int ReceivedQty { get; private set; }

    public int DamagedQty { get; private set; }

    public int AcceptedQty => ReceivedQty - DamagedQty;

    public decimal UnitCost { get; private set; }
}
