using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Orders.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record OrderStatusChanged(Guid OrderId, string OrderNumber, string Channel, string Status, Guid? FulfillmentBranchId, string? Mode = null) : IIntegrationEvent
{
    public static string EventType => "orders.order_status_changed";
}

public sealed record FulfillmentExceptionRaised(Guid ExceptionId, Guid OrderId, string OrderNumber, Guid BranchId, string Reason) : IIntegrationEvent
{
    public static string EventType => "orders.fulfillment_exception_raised";
}

public sealed record OrderConfirmed(Guid OrderId, string OrderNumber, string Channel, Guid? FulfillmentBranchId) : IIntegrationEvent
{
    public static string EventType => "orders.order_confirmed";
}

public sealed record FulfillmentInquiryCreated(Guid InquiryId, string Reference, string? Channel = null, string? ContactName = null) : IIntegrationEvent
{
    public static string EventType => "orders.fulfillment_inquiry_created";
}

/// <summary>One vendor's parcel of an order was shipped (ADR-001 §46): the buyer gets that parcel's courier and tracking.</summary>
public sealed record ParcelShipped(Guid OrderId, string OrderNumber, Guid ParcelId, string VendorName, string Courier, string? TrackingNumber) : IIntegrationEvent
{
    public static string EventType => "orders.parcel_shipped";
}
