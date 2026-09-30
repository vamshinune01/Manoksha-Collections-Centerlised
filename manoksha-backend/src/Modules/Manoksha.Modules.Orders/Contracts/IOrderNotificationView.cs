namespace Manoksha.Modules.Orders.Contracts;

public sealed record OrderNotificationLine(string Name, int Quantity, decimal LineTotal);

/// <param name="ContactEmail">The email entered with the delivery details (online orders), if any.</param>
public sealed record OrderNotificationSummary(Guid OrderId, string Number, string Channel, string Status, Guid? CustomerUserId, Guid? ResellerId,
    Guid FulfillmentBranchId, string DeliveryName, string? ContactEmail, string DeliveryCity, decimal MerchandiseTotal, decimal ShippingFee, decimal GrandTotal,
    IReadOnlyList<OrderNotificationLine> Lines, string? Courier, string? TrackingNumber, DateOnly? DeliveredOn);

/// <summary>Read-only order facts for customer/reseller emails (Notifications module).</summary>
public interface IOrderNotificationView
{
    Task<OrderNotificationSummary?> GetAsync(Guid orderId, CancellationToken cancellationToken = default);
}
