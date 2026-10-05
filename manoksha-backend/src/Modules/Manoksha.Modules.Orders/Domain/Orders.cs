using Manoksha.SharedKernel;

namespace Manoksha.Modules.Orders.Domain;

internal enum OrderChannel
{
    Online = 1,
    Store = 2,
    Reseller = 3,
}

/// <summary>SPEC §27.1. CHECKOUT_ATTEMPT / WALLET_VALIDATION happen inside the checkout transaction and are never persisted alone.</summary>
internal enum OrderStatus
{
    CheckoutAttempt = 1,
    PaymentPending = 2,
    WalletValidation = 3,
    Confirmed = 4,
    Processing = 5,
    Packed = 6,
    Shipped = 7,
    Delivered = 8,
    FulfillmentException = 9,
    Cancelled = 10,
    PaymentFailed = 11,
    PaymentExpired = 12,
    Completed = 13,
}

internal sealed record DeliveryDetails(string Name, string Mobile, string? Email, string AddressLine, string City, string State, string Pin);

/// <summary>How an order is fulfilled: from branch stock (paused, ADR-001 §48) or shipped directly by vendors (ADR-001 §43, §46).</summary>
internal enum FulfillmentMode
{
    Branch = 1,
    Vendor = 2,
}

internal sealed class Order : Entity
{
    private Order()
    {
    }

    public Order(Guid id, string number, OrderChannel channel, Guid? resellerId, Guid? resellerCustomerId, Guid? fulfillmentBranchId, DeliveryDetails delivery,
        decimal merchandiseTotal, decimal shippingFee, Guid placedBy, DateTimeOffset now, Guid? customerUserId = null)
        : base(id)
    {
        FulfillmentMode = fulfillmentBranchId is null ? FulfillmentMode.Vendor : FulfillmentMode.Branch;
        CustomerUserId = customerUserId;
        Number = number;
        Channel = channel;
        ResellerId = resellerId;
        ResellerCustomerId = resellerCustomerId;
        FulfillmentBranchId = fulfillmentBranchId;
        Delivery = delivery;
        MerchandiseTotal = merchandiseTotal;
        ShippingFee = shippingFee;
        GrandTotal = merchandiseTotal + shippingFee;
        PlacedBy = placedBy;
        CreatedAt = now;
        Status = OrderStatus.CheckoutAttempt;
    }

    public string Number { get; private set; } = default!;

    public OrderChannel Channel { get; private set; }

    public OrderStatus Status { get; private set; }

    public Guid? ResellerId { get; private set; }

    public Guid? ResellerCustomerId { get; private set; }

    /// <summary>The signed-in customer who placed an ONLINE order (SPEC §24: they alone see it).</summary>
    public Guid? CustomerUserId { get; private set; }

    /// <summary>The payment attempt that paid an ONLINE order.</summary>
    public Guid? PaymentAttemptId { get; private set; }

    /// <summary>The fulfilling branch; null for vendor orders, which vendors ship directly (ADR-001 §43).</summary>
    public Guid? FulfillmentBranchId { get; private set; }

    public FulfillmentMode FulfillmentMode { get; private set; }

    /// <summary>Delivery snapshot as entered at checkout (ADR-001 §21).</summary>
    public DeliveryDetails Delivery { get; private set; } = default!;

    public decimal MerchandiseTotal { get; private set; }

    /// <summary>₹100 per separate order (SPEC §18), snapshotted from settings.</summary>
    public decimal ShippingFee { get; private set; }

    public decimal GrandTotal { get; private set; }

    public Guid? WalletLedgerEntryId { get; private set; }

    public Guid PlacedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void ConfirmWalletPaid(Guid ledgerEntryId, DateTimeOffset now)
    {
        if (Status != OrderStatus.CheckoutAttempt)
        {
            throw new BusinessRuleException("ORDER_STATUS_INVALID", "Only a checkout attempt can be confirmed.");
        }
        WalletLedgerEntryId = ledgerEntryId;
        Status = OrderStatus.Confirmed;
        ConfirmedAt = now;
    }

    /// <summary>POS: a store sale is created directly as COMPLETED in its finalize transaction (ADR-001 §9).</summary>
    public void CompleteStoreSale(DateTimeOffset now)
    {
        Ensure(OrderStatus.CheckoutAttempt);
        Status = OrderStatus.Completed;
        ConfirmedAt = now;
    }

    /// <summary>Online: the complete basket is reserved at one branch and payment can start (SPEC §19.1).</summary>
    public void AwaitPayment()
    {
        Ensure(OrderStatus.CheckoutAttempt);
        Status = OrderStatus.PaymentPending;
    }

    /// <summary>Valid payment inside the reservation window (SPEC §14.1).</summary>
    public OrderStatus ConfirmOnlinePaid(Guid paymentAttemptId, DateTimeOffset now)
    {
        var from = Ensure(OrderStatus.PaymentPending);
        PaymentAttemptId = paymentAttemptId;
        Status = OrderStatus.Confirmed;
        ConfirmedAt = now;
        return from;
    }

    /// <summary>
    /// Late success recovered by re-acquiring the complete basket (SPEC §14.2). The branch may differ from the original reservation;
    /// the price snapshot never changes (ADR-001 §12).
    /// </summary>
    public OrderStatus RecoverLatePayment(Guid paymentAttemptId, Guid branchId, DateTimeOffset now)
    {
        var from = Ensure(OrderStatus.PaymentPending, OrderStatus.PaymentExpired, OrderStatus.PaymentFailed);
        PaymentAttemptId = paymentAttemptId;
        FulfillmentBranchId = branchId;
        Status = OrderStatus.Confirmed;
        ConfirmedAt = now;
        return from;
    }

    /// <summary>Vendor orders: a payment confirms the order even when it arrives late — there is no stock hold to lose (ADR-001 §43).</summary>
    public OrderStatus ConfirmVendorOnlinePaid(Guid paymentAttemptId, DateTimeOffset now)
    {
        var from = Ensure(OrderStatus.PaymentPending, OrderStatus.PaymentExpired, OrderStatus.PaymentFailed);
        PaymentAttemptId = paymentAttemptId;
        Status = OrderStatus.Confirmed;
        ConfirmedAt = now;
        return from;
    }

    /// <summary>
    /// Vendor orders follow their parcels (ADR-001 §46): Processing once any parcel is ordered or shipped, Shipped when every parcel
    /// has shipped, Delivered when every parcel is delivered. Never moves backwards.
    /// </summary>
    public OrderStatus? FollowParcels(IReadOnlyCollection<OrderParcel> parcels)
    {
        if (FulfillmentMode != FulfillmentMode.Vendor)
        {
            throw new InvalidOperationException("Only vendor orders follow parcels.");
        }
        var live = parcels.Where(p => p.Status != ParcelStatus.Cancelled).ToList();
        var target = live.Count > 0 && live.All(p => p.Status == ParcelStatus.Delivered) ? OrderStatus.Delivered
            : live.Count > 0 && live.All(p => p.Status is ParcelStatus.Shipped or ParcelStatus.Delivered) ? OrderStatus.Shipped
            : live.Any(p => p.Status != ParcelStatus.Pending) ? OrderStatus.Processing
            : OrderStatus.Confirmed;
        var rank = new Dictionary<OrderStatus, int> { [OrderStatus.Confirmed] = 0, [OrderStatus.Processing] = 1, [OrderStatus.Shipped] = 2, [OrderStatus.Delivered] = 3 };
        if (!rank.TryGetValue(Status, out var current))
        {
            throw new BusinessRuleException("ORDER_STATUS_INVALID", $"The order is {Status}; this step is not possible.", 409);
        }
        if (rank[target] <= current)
        {
            return null;
        }
        var from = Status;
        Status = target;
        return from;
    }

    // ---- Fulfillment (SPEC §19, §22, §27.1) ----

    public OrderStatus StartProcessing() => Move(OrderStatus.Processing, OrderStatus.Confirmed);

    public OrderStatus MarkPacked() => Move(OrderStatus.Packed, OrderStatus.Processing);

    public OrderStatus MarkShipped() => Move(OrderStatus.Shipped, OrderStatus.Packed);

    public OrderStatus MarkDelivered() => Move(OrderStatus.Delivered, OrderStatus.Shipped);

    /// <summary>The assigned branch cannot physically fulfil the confirmed order (SPEC §22).</summary>
    public OrderStatus RaiseFulfillmentException() => Move(OrderStatus.FulfillmentException, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Packed);

    /// <summary>Resolved at the same branch (e.g. the piece was found).</summary>
    public OrderStatus ResumeAfterException() => Move(OrderStatus.Processing, OrderStatus.FulfillmentException);

    /// <summary>The whole order moves to a branch that can fulfil it completely (never split).</summary>
    public OrderStatus Reroute(Guid toBranchId)
    {
        var from = Move(OrderStatus.Processing, OrderStatus.FulfillmentException);
        FulfillmentBranchId = toBranchId;
        return from;
    }

    /// <summary>Administrative cancellation (ADR-001 §8, Phase 7 decision: until Packed; never after Shipped).</summary>
    public OrderStatus Cancel() => Move(OrderStatus.Cancelled, OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Packed, OrderStatus.FulfillmentException);

    private OrderStatus Move(OrderStatus to, params OrderStatus[] from)
    {
        var previous = Ensure(from);
        Status = to;
        return previous;
    }

    public void MarkPaymentFailed()
    {
        Ensure(OrderStatus.PaymentPending);
        Status = OrderStatus.PaymentFailed;
    }

    public void MarkPaymentExpired()
    {
        Ensure(OrderStatus.PaymentPending);
        Status = OrderStatus.PaymentExpired;
    }

    private OrderStatus Ensure(params OrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new BusinessRuleException("ORDER_STATUS_INVALID", $"The order is {Status}; this step is not possible.", 409);
        }
        return Status;
    }
}

/// <summary>
/// Immutable price snapshot (SPEC §15): retail/base price, applicable discount source and %, final unit price. Later price or
/// discount changes never touch it (the table is append-only in the database).
/// </summary>
internal enum ParcelStatus
{
    /// <summary>Waiting to be passed on to the vendor.</summary>
    Pending = 1,

    /// <summary>The Owner placed it with the vendor.</summary>
    OrderedFromVendor = 2,

    Shipped = 3,
    Delivered = 4,
    Cancelled = 5,
}

/// <summary>One vendor's part of an order, shipped by that vendor with its own courier and tracking (ADR-001 §46).</summary>
internal sealed class OrderParcel : Entity
{
    private OrderParcel()
    {
    }

    public OrderParcel(Guid orderId, Guid vendorId, string vendorCode, string vendorName, decimal shippingFee, DateTimeOffset now)
    {
        OrderId = orderId;
        VendorId = vendorId;
        VendorCode = vendorCode;
        VendorName = vendorName;
        ShippingFee = shippingFee;
        Status = ParcelStatus.Pending;
        UpdatedAt = now;
    }

    public Guid OrderId { get; private set; }

    public Guid VendorId { get; private set; }

    public string VendorCode { get; private set; } = default!;

    public string VendorName { get; private set; } = default!;

    /// <summary>The vendor's shipping fee at checkout (snapshot).</summary>
    public decimal ShippingFee { get; private set; }

    public ParcelStatus Status { get; private set; }

    /// <summary>The vendor's own order/invoice number, if any.</summary>
    public string? VendorReference { get; private set; }

    public string? Courier { get; private set; }

    public string? CourierName { get; private set; }

    public string? TrackingNumber { get; private set; }

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateOnly? DeliveredOn { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void MarkOrderedFromVendor(string? vendorReference, string? note, DateTimeOffset now)
    {
        Require(ParcelStatus.Pending);
        Status = ParcelStatus.OrderedFromVendor;
        VendorReference = vendorReference;
        Note = note;
        UpdatedAt = now;
    }

    public void MarkShipped(string courier, string? courierName, string? tracking, string? note, DateTimeOffset now)
    {
        Require(ParcelStatus.Pending, ParcelStatus.OrderedFromVendor);
        Status = ParcelStatus.Shipped;
        Courier = courier;
        CourierName = courierName;
        TrackingNumber = tracking;
        ShippedAt = now;
        Note = note ?? Note;
        UpdatedAt = now;
    }

    public void MarkDelivered(DateOnly on, string? note, DateTimeOffset now)
    {
        Require(ParcelStatus.Shipped);
        Status = ParcelStatus.Delivered;
        DeliveredOn = on;
        Note = note ?? Note;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is ParcelStatus.Shipped or ParcelStatus.Delivered)
        {
            throw new BusinessRuleException("PARCEL_ALREADY_SHIPPED", $"The {VendorName} parcel has already shipped; the order can no longer be cancelled.", 409);
        }
        Status = ParcelStatus.Cancelled;
        UpdatedAt = now;
    }

    private void Require(params ParcelStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new BusinessRuleException("PARCEL_STATUS_INVALID", $"The {VendorName} parcel is {Status}; this step is not possible.", 409);
        }
    }
}

internal sealed class OrderLine : Entity
{
    private OrderLine()
    {
    }

    public OrderLine(Guid orderId, Guid skuId, string skuCode, string productName, string variantName, int quantity, Guid? retailPriceId, decimal retailUnitPrice,
        string discountSource, decimal discountPct, decimal finalUnitPrice, Guid? commercialTermId, int? commercialTermVersion, Guid? productDiscountId,
        decimal? costAmount, Guid[] itemIds)
    {
        OrderId = orderId;
        SkuId = skuId;
        SkuCode = skuCode;
        ProductName = productName;
        VariantName = variantName;
        Quantity = quantity;
        RetailPriceId = retailPriceId;
        RetailUnitPrice = retailUnitPrice;
        DiscountSource = discountSource;
        DiscountPct = discountPct;
        DiscountAmountPerUnit = retailUnitPrice - finalUnitPrice;
        FinalUnitPrice = finalUnitPrice;
        LineTotal = finalUnitPrice * quantity;
        CommercialTermId = commercialTermId;
        CommercialTermVersion = commercialTermVersion;
        ProductDiscountId = productDiscountId;
        CostAmount = costAmount;
        ItemIds = itemIds;
    }

    public Guid OrderId { get; private set; }

    public Guid SkuId { get; private set; }

    public string SkuCode { get; private set; } = default!;

    public string ProductName { get; private set; } = default!;

    public string VariantName { get; private set; } = default!;

    public int Quantity { get; private set; }

    public Guid? RetailPriceId { get; private set; }

    public decimal RetailUnitPrice { get; private set; }

    public string DiscountSource { get; private set; } = default!;

    public decimal DiscountPct { get; private set; }

    public decimal DiscountAmountPerUnit { get; private set; }

    public decimal FinalUnitPrice { get; private set; }

    public decimal LineTotal { get; private set; }

    public Guid? CommercialTermId { get; private set; }

    public int? CommercialTermVersion { get; private set; }

    public Guid? ProductDiscountId { get; private set; }

    /// <summary>
    /// FIFO cost of goods for gross-profit reporting (SPEC §31), known when stock is sold. Null for ONLINE lines, whose cost is
    /// recorded on the consumed reservation line at payment confirmation.
    /// </summary>
    public decimal? CostAmount { get; private set; }

    public Guid[] ItemIds { get; private set; } = [];

    /// <summary>Vendor products: the vendor and its parcel in this order (ADR-001 §46).</summary>
    public Guid? VendorId { get; private set; }

    public Guid? ParcelId { get; private set; }

    /// <summary>The product's public ID at sale time, e.g. ZR-000123 (snapshot).</summary>
    public string? ProductCode { get; private set; }

    /// <summary>Vendor products ship from the vendor's parcel. Cost is the vendor price: retail × (1 − Owner margin) (ADR-001 §47).</summary>
    public void PlaceInParcel(Guid parcelId, Guid vendorId, string? productCode, decimal? costAmount)
    {
        ParcelId = parcelId;
        VendorId = vendorId;
        ProductCode = productCode;
        CostAmount = costAmount;
    }
}

internal sealed class OrderStatusChange : Entity
{
    private OrderStatusChange()
    {
    }

    public OrderStatusChange(Guid orderId, OrderStatus? from, OrderStatus to, Guid? actorUserId, string? note, DateTimeOffset now)
    {
        OrderId = orderId;
        FromStatus = from;
        ToStatus = to;
        ActorUserId = actorUserId;
        Note = note;
        OccurredAt = now;
    }

    public Guid OrderId { get; private set; }

    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>A reseller's own customer relationship (ADR-001 §5, §22). Never visible to any other reseller.</summary>
internal sealed class ResellerCustomer : Entity
{
    private ResellerCustomer()
    {
    }

    public ResellerCustomer(Guid resellerId, DeliveryDetails details, DateTimeOffset now)
    {
        ResellerId = resellerId;
        Details = details;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid ResellerId { get; private set; }

    public DeliveryDetails Details { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(DeliveryDetails details, DateTimeOffset now)
    {
        Details = details;
        UpdatedAt = now;
    }
}

/// <summary>
/// "No qualifying branch" record (SPEC §12): no order, no payment/debit; a reference the buyer can share on WhatsApp and the
/// Owner sees in the Exception Center with the attempted cart and each branch's evaluation.
/// </summary>
internal sealed class FulfillmentInquiry : Entity
{
    private FulfillmentInquiry()
    {
    }

    public FulfillmentInquiry(string reference, OrderChannel channel, Guid? resellerId, Guid userId, string? contactName, string? contactMobile,
        string cartJson, string evaluationsJson, string failureReason, DateTimeOffset now)
    {
        Reference = reference;
        Channel = channel;
        ResellerId = resellerId;
        UserId = userId;
        ContactName = contactName;
        ContactMobile = contactMobile;
        CartJson = cartJson;
        EvaluationsJson = evaluationsJson;
        FailureReason = failureReason;
        Status = "Open";
        CreatedAt = now;
    }

    public string Reference { get; private set; } = default!;

    public OrderChannel Channel { get; private set; }

    public Guid? ResellerId { get; private set; }

    public Guid UserId { get; private set; }

    public string? ContactName { get; private set; }

    public string? ContactMobile { get; private set; }

    public string CartJson { get; private set; } = default!;

    public string EvaluationsJson { get; private set; } = default!;

    public string FailureReason { get; private set; } = default!;

    /// <summary>Open, or Closed once support has followed up with the customer (SPEC §28).</summary>
    public string Status { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public string? FollowUpNote { get; private set; }

    public Guid? ClosedBy { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public void Close(Guid userId, string note, DateTimeOffset now)
    {
        if (Status != "Open")
        {
            throw new BusinessRuleException("INQUIRY_CLOSED", "This inquiry is already closed.", 409);
        }
        Status = "Closed";
        FollowUpNote = note;
        ClosedBy = userId;
        ClosedAt = now;
    }
}

internal enum ReservationStatus
{
    Active = 1,
    Consumed = 2,
    Expired = 3,
    Released = 4,

    /// <summary>A sold allocation that came back to stock (order cancelled or rerouted).</summary>
    Returned = 5,
}

/// <summary>
/// Timed hold of an ONLINE order's complete basket at one branch (SPEC §13, §27.3). The expiry is copied from the setting at
/// creation, so a later setting change never affects it. Validity is time-based: a payment confirms it only before
/// <see cref="ExpiresAt"/>; the sweeper merely performs the physical release.
/// </summary>
internal sealed class Reservation : Entity
{
    private Reservation()
    {
    }

    public Reservation(Guid orderId, Guid branchId, DateTimeOffset expiresAt, DateTimeOffset now, ReservationStatus status = ReservationStatus.Active)
    {
        OrderId = orderId;
        BranchId = branchId;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        Status = status;
        if (status != ReservationStatus.Active)
        {
            ClosedAt = now;
        }
    }

    public Guid OrderId { get; private set; }

    public Guid BranchId { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? CloseReason { get; private set; }

    public uint RowVersion { get; private set; }

    public void MarkReturned(string reason, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Consumed)
        {
            throw new BusinessRuleException("ALLOCATION_NOT_SOLD", $"The allocation is {Status}.", 409);
        }
        Status = ReservationStatus.Returned;
        CloseReason = reason;
        ClosedAt = now;
    }

    public void Close(ReservationStatus to, string reason, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active || to == ReservationStatus.Active)
        {
            throw new BusinessRuleException("RESERVATION_NOT_ACTIVE", $"The reservation is already {Status}.", 409);
        }
        Status = to;
        CloseReason = reason;
        ClosedAt = now;
    }
}

internal sealed class ReservationLine : Entity
{
    private ReservationLine()
    {
    }

    public ReservationLine(Guid reservationId, Guid orderLineId, Guid skuId, int quantity, Guid[] itemIds, decimal? costAmount = null)
    {
        ReservationId = reservationId;
        OrderLineId = orderLineId;
        SkuId = skuId;
        Quantity = quantity;
        ItemIds = itemIds;
        CostAmount = costAmount;
    }

    public Guid ReservationId { get; private set; }

    public Guid OrderLineId { get; private set; }

    public Guid SkuId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>The exact pieces held (serialized SKUs), so release and sale are exact (design §14).</summary>
    public Guid[] ItemIds { get; private set; } = [];

    /// <summary>FIFO cost consumed when the reservation was sold.</summary>
    public decimal? CostAmount { get; private set; }

    public void RecordCost(decimal cost) => CostAmount = cost;
}

/// <summary>Courier hand-over (Phase 7 decision: courier required, tracking number optional). One shipment per order in V1.</summary>
internal sealed class Shipment : Entity
{
    private Shipment()
    {
    }

    public Shipment(Guid orderId, Guid branchId, string courier, string? courierName, string? trackingNumber, Guid shippedBy, DateTimeOffset now)
    {
        OrderId = orderId;
        BranchId = branchId;
        Courier = courier;
        CourierName = courierName;
        TrackingNumber = trackingNumber;
        ShippedBy = shippedBy;
        ShippedAt = now;
    }

    public Guid OrderId { get; private set; }

    public Guid BranchId { get; private set; }

    /// <summary>XPRESSBEES, DELHIVERY or OTHER (with <see cref="CourierName"/>). Courier APIs are integrated later.</summary>
    public string Courier { get; private set; } = default!;

    public string? CourierName { get; private set; }

    public string? TrackingNumber { get; private set; }

    public Guid ShippedBy { get; private set; }

    public DateTimeOffset ShippedAt { get; private set; }

    public DateOnly? DeliveredOn { get; private set; }

    public Guid? DeliveredRecordedBy { get; private set; }

    public DateTimeOffset? DeliveredRecordedAt { get; private set; }

    public string? DeliveryNote { get; private set; }

    public uint RowVersion { get; private set; }

    public void RecordDelivered(DateOnly on, string? note, Guid by, DateTimeOffset now)
    {
        DeliveredOn = on;
        DeliveryNote = note;
        DeliveredRecordedBy = by;
        DeliveredRecordedAt = now;
    }
}

internal enum FulfillmentExceptionStatus
{
    Open = 1,
    Rerouted = 2,
    ResolvedInPlace = 3,
    Cancelled = 4,
}

/// <summary>SPEC §22: the assigned branch cannot locate or fulfil an item of a confirmed order.</summary>
internal sealed class FulfillmentException : Entity
{
    public static readonly string[] Reasons = ["ITEM_NOT_FOUND", "DAMAGED", "INVENTORY_MISMATCH", "OTHER"];

    private FulfillmentException()
    {
    }

    public FulfillmentException(Guid orderId, Guid branchId, string reason, string notes, Guid raisedBy, DateTimeOffset now)
    {
        OrderId = orderId;
        BranchId = branchId;
        Reason = reason;
        Notes = notes;
        RaisedBy = raisedBy;
        RaisedAt = now;
        Status = FulfillmentExceptionStatus.Open;
    }

    public Guid OrderId { get; private set; }

    public Guid BranchId { get; private set; }

    public string Reason { get; private set; } = default!;

    public string Notes { get; private set; } = default!;

    public FulfillmentExceptionStatus Status { get; private set; }

    public Guid RaisedBy { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? Resolution { get; private set; }

    public uint RowVersion { get; private set; }

    public void Close(FulfillmentExceptionStatus to, string resolution, Guid by, DateTimeOffset now)
    {
        if (Status != FulfillmentExceptionStatus.Open)
        {
            throw new BusinessRuleException("EXCEPTION_NOT_OPEN", "This fulfillment exception is already closed.", 409);
        }
        Status = to;
        Resolution = resolution;
        ResolvedBy = by;
        ResolvedAt = now;
    }
}

/// <summary>What staff found for one order line: units missing or damaged (pieces for serialized SKUs).</summary>
internal sealed class FulfillmentExceptionLine : Entity
{
    private FulfillmentExceptionLine()
    {
    }

    public FulfillmentExceptionLine(Guid exceptionId, Guid skuId, int missingQty, int damagedQty, Guid[] missingItemIds, Guid[] damagedItemIds)
    {
        ExceptionId = exceptionId;
        SkuId = skuId;
        MissingQty = missingQty;
        DamagedQty = damagedQty;
        MissingItemIds = missingItemIds;
        DamagedItemIds = damagedItemIds;
    }

    public Guid ExceptionId { get; private set; }

    public Guid SkuId { get; private set; }

    public int MissingQty { get; private set; }

    public int DamagedQty { get; private set; }

    public Guid[] MissingItemIds { get; private set; } = [];

    public Guid[] DamagedItemIds { get; private set; } = [];
}

/// <summary>Whole-order reroute record (SPEC §22): original branch, new branch, reason, actor, time and inventory effects.</summary>
internal sealed class OrderReroute : Entity
{
    private OrderReroute()
    {
    }

    public OrderReroute(Guid orderId, Guid fromBranchId, Guid toBranchId, string reason, Guid? exceptionId, string effectsJson, Guid actorUserId, DateTimeOffset now)
    {
        OrderId = orderId;
        FromBranchId = fromBranchId;
        ToBranchId = toBranchId;
        Reason = reason;
        ExceptionId = exceptionId;
        EffectsJson = effectsJson;
        ActorUserId = actorUserId;
        OccurredAt = now;
    }

    public Guid OrderId { get; private set; }

    public Guid FromBranchId { get; private set; }

    public Guid ToBranchId { get; private set; }

    public string Reason { get; private set; } = default!;

    public Guid? ExceptionId { get; private set; }

    public string EffectsJson { get; private set; } = default!;

    public Guid ActorUserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// POS negotiated price (SPEC §20, §30): original and final price, discount amount and %, branch, seller, approver and reason.
/// Append-only.
/// </summary>
internal sealed class PosPriceOverride : Entity
{
    private PosPriceOverride()
    {
    }

    public PosPriceOverride(Guid orderId, Guid orderLineId, Guid branchId, Guid skuId, decimal originalUnitPrice, decimal finalUnitPrice, decimal discountPct,
        int quantity, string reason, string approvalLevel, Guid sellerUserId, Guid? approverUserId, DateTimeOffset now)
    {
        OrderId = orderId;
        OrderLineId = orderLineId;
        BranchId = branchId;
        SkuId = skuId;
        OriginalUnitPrice = originalUnitPrice;
        FinalUnitPrice = finalUnitPrice;
        DiscountPerUnit = originalUnitPrice - finalUnitPrice;
        DiscountPct = discountPct;
        Quantity = quantity;
        Reason = reason;
        ApprovalLevel = approvalLevel;
        SellerUserId = sellerUserId;
        ApproverUserId = approverUserId;
        OccurredAt = now;
    }

    public Guid OrderId { get; private set; }

    public Guid OrderLineId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid SkuId { get; private set; }

    public decimal OriginalUnitPrice { get; private set; }

    public decimal FinalUnitPrice { get; private set; }

    public decimal DiscountPerUnit { get; private set; }

    public decimal DiscountPct { get; private set; }

    public int Quantity { get; private set; }

    public string Reason { get; private set; } = default!;

    /// <summary>SELLER (within the seller's own limit), MANAGER or OWNER.</summary>
    public string ApprovalLevel { get; private set; } = default!;

    public Guid SellerUserId { get; private set; }

    public Guid? ApproverUserId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>One payment component of a store sale (split payments — SPEC §20). Append-only.</summary>
internal sealed class PosPayment : Entity
{
    private PosPayment()
    {
    }

    public PosPayment(Guid orderId, string method, decimal amount, string? reference, Guid recordedBy, DateTimeOffset now)
    {
        OrderId = orderId;
        Method = method;
        Amount = amount;
        Reference = reference;
        RecordedBy = recordedBy;
        RecordedAt = now;
    }

    public Guid OrderId { get; private set; }

    /// <summary>CASH, UPI, CARD or OTHER (enabled methods come from settings).</summary>
    public string Method { get; private set; } = default!;

    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }

    public Guid RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
