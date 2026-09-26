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

internal sealed class Order : Entity
{
    private Order()
    {
    }

    public Order(Guid id, string number, OrderChannel channel, Guid? resellerId, Guid? resellerCustomerId, Guid fulfillmentBranchId, DeliveryDetails delivery,
        decimal merchandiseTotal, decimal shippingFee, Guid placedBy, DateTimeOffset now, Guid? customerUserId = null)
        : base(id)
    {
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

    public Guid FulfillmentBranchId { get; private set; }

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

    public string Status { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }
}

internal enum ReservationStatus
{
    Active = 1,
    Consumed = 2,
    Expired = 3,
    Released = 4,
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
