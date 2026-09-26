using Manoksha.Modules.Payments.Contracts;

namespace Manoksha.Modules.Orders.Application;

/// <summary>
/// The customer's return from the payment page (design §13 point 6): the server consults its own records and the provider —
/// never the browser's claim about the payment.
/// </summary>
internal sealed class OnlineOrderService(OrderQueryService orders, IPayments payments)
{
    public async Task<OrderPaymentStatusDto> PaymentStatusAsync(Guid orderId, CancellationToken ct)
    {
        var order = await orders.GetForCustomerAsync(orderId, ct); // ownership check
        var attempt = await payments.FindLatestAsync(PaymentPurposes.Order, orderId, ct);
        if (attempt is { Status: "INITIATED" or "PENDING" })
        {
            attempt = await payments.SyncAsync(attempt.Id, ct);
            order = await orders.GetForCustomerAsync(orderId, ct);
        }
        return new OrderPaymentStatusDto(order.Id, order.Number, order.Status, attempt is null ? null : OnlinePayments.ToDto(attempt));
    }
}
