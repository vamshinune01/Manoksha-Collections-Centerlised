using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Orders.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record OrderStatusChanged(Guid OrderId, string OrderNumber, string Channel, string Status, Guid FulfillmentBranchId) : IIntegrationEvent
{
    public static string EventType => "orders.order_status_changed";
}

public sealed record FulfillmentExceptionRaised(Guid ExceptionId, Guid OrderId, string OrderNumber, Guid BranchId, string Reason) : IIntegrationEvent
{
    public static string EventType => "orders.fulfillment_exception_raised";
}

public sealed record OrderConfirmed(Guid OrderId, string OrderNumber, string Channel, Guid FulfillmentBranchId) : IIntegrationEvent
{
    public static string EventType => "orders.order_confirmed";
}

public sealed record FulfillmentInquiryCreated(Guid InquiryId, string Reference, string? Channel = null, string? ContactName = null) : IIntegrationEvent
{
    public static string EventType => "orders.fulfillment_inquiry_created";
}
