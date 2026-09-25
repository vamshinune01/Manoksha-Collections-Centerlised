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
