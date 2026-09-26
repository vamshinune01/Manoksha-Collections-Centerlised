namespace Manoksha.Modules.Orders.Application;

public sealed record DeliveryDto(string Name, string Mobile, string? Email, string AddressLine, string City, string State, string Pin);

public sealed record ResellerCustomerDto(Guid Id, DeliveryDto Details, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record SaveResellerCustomerRequest(DeliveryDto Details);

public sealed record CheckoutLineRequest(Guid SkuId, int Quantity);

/// <param name="ResellerCustomerId">Use a saved customer's details, or enter <paramref name="Delivery"/> (ADR-001 §21).</param>
public sealed record ResellerCheckoutRequest(IReadOnlyList<CheckoutLineRequest> Lines, Guid? ResellerCustomerId, DeliveryDto? Delivery, bool SaveCustomer);

public sealed record OrderLineDto(
    Guid Id,
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string VariantName,
    int Quantity,
    decimal RetailUnitPrice,
    string DiscountSource,
    decimal DiscountPct,
    decimal DiscountAmountPerUnit,
    decimal FinalUnitPrice,
    decimal LineTotal,
    int? CommercialTermVersion);

public sealed record OrderStatusChangeDto(string? FromStatus, string ToStatus, string? Note, DateTimeOffset OccurredAt);

public sealed record OrderDto(
    Guid Id,
    string Number,
    string Channel,
    string Status,
    Guid? ResellerId,
    Guid FulfillmentBranchId,
    string FulfillmentBranchName,
    DeliveryDto Delivery,
    decimal MerchandiseTotal,
    decimal ShippingFee,
    decimal GrandTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<OrderStatusChangeDto> History,
    string HelpWhatsAppUrl,
    decimal? CostOfGoods);

public sealed record InquiryDto(string Reference, string Message, string WhatsAppUrl);

public sealed record CheckoutResult(string Outcome, OrderDto? Order, decimal? WalletBalance, InquiryDto? Inquiry);

public sealed record FulfillmentInquiryDto(Guid Id, string Reference, string Channel, Guid? ResellerId, string? ContactName, string? ContactMobile,
    string Cart, string Evaluations, string FailureReason, string Status, DateTimeOffset CreatedAt);

// ---- Online (customer) checkout ----

public sealed record CustomerCheckoutRequest(IReadOnlyList<CheckoutLineRequest> Lines, DeliveryDto Delivery);

/// <param name="Status">SPEC §27.2 payment status (PENDING, SUCCESS, FAILED, EXPIRED, ORDER_RECOVERED, PAYMENT_RECONCILIATION_REQUIRED …).</param>
/// <param name="RedirectUrl">Where the payer completes the UPI payment while the status is PENDING.</param>
public sealed record OnlinePaymentDto(Guid AttemptId, string Status, decimal Amount, string? RedirectUrl, DateTimeOffset ExpiresAt, DateTimeOffset? CompletedAt, string Message);

/// <param name="Outcome">PAYMENT_PENDING (go to <c>Payment.RedirectUrl</c>), PAYMENT_NOT_COMPLETED, ORDER_PLACED or UNFULFILLABLE (see <c>Inquiry</c>).</param>
public sealed record CustomerCheckoutResult(string Outcome, OrderDto? Order, OnlinePaymentDto? Payment, InquiryDto? Inquiry);

/// <summary>What the idempotent part of checkout produced (stored with the Idempotency-Key and replayed for repeats).</summary>
public sealed record CheckoutStage(Guid? OrderId, Guid? PaymentAttemptId, InquiryDto? Inquiry);

public sealed record OrderPaymentStatusDto(Guid OrderId, string OrderNumber, string OrderStatus, OnlinePaymentDto? Payment);

// ---- Public storefront (anonymous browsing, SPEC §19.1) ----

/// <param name="InStock">Hint only: some branch has AVAILABLE units now. Checkout decides per branch for the complete basket.</param>
public sealed record StorefrontItemDto(Guid SkuId, string SkuCode, Guid ProductId, string ProductName, string VariantName, string CategoryName, decimal Price, bool InStock);

public sealed record StorefrontPage(IReadOnlyList<StorefrontItemDto> Items, int Total, int Page, int PageSize);

public sealed record StorefrontVariantDto(Guid SkuId, string SkuCode, string VariantName, decimal Price, bool InStock);

public sealed record StorefrontProductDto(Guid ProductId, string ProductName, IReadOnlyList<StorefrontVariantDto> Variants);

public sealed record CartQuoteRequest(IReadOnlyList<Guid> SkuIds);

/// <summary>Current display price for a cart line (non-authoritative; checkout re-prices and snapshots).</summary>
public sealed record CartQuoteLineDto(Guid SkuId, bool Sellable, string? ProductName, string? VariantName, decimal? Price, bool InStock, string? Message);

/// <summary>Per-order charges to display before payment (SPEC §18). The backend applies the authoritative values at checkout.</summary>
public sealed record OrderChargesDto(decimal ShippingFeePerOrder);
