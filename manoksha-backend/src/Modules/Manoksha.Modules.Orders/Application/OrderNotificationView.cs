using Manoksha.Modules.Orders.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Orders.Application;

internal sealed class OrderNotificationView(ManokshaDbContext db) : IOrderNotificationView
{
    public async Task<OrderNotificationSummary?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var o = await db.Set<Order>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (o is null)
        {
            return null;
        }
        var lines = await db.Set<OrderLine>().AsNoTracking().Where(l => l.OrderId == orderId).OrderBy(l => l.SkuCode)
            .Select(l => new OrderNotificationLine(l.ProductName + " · " + l.VariantName, l.Quantity, l.LineTotal)).ToListAsync(cancellationToken);
        var sh = await db.Set<Shipment>().AsNoTracking().SingleOrDefaultAsync(s => s.OrderId == orderId, cancellationToken);
        return new OrderNotificationSummary(o.Id, o.Number, o.Channel.ToString(), o.Status.ToString(), o.CustomerUserId, o.ResellerId, o.FulfillmentBranchId,
            o.Delivery.Name, o.Delivery.Email, o.Delivery.City, o.MerchandiseTotal, o.ShippingFee, o.GrandTotal, lines,
            sh is null ? null : OrderQueryService.CourierLabel(sh), sh?.TrackingNumber, sh?.DeliveredOn);
    }
}
