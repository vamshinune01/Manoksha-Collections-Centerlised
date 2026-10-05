using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Orders.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Orders.Application;

public sealed record ParcelOrderedRequest(string? VendorReference, string? Note);

/// <summary>
/// Vendor orders (ADR-001 §46): each vendor's items are a parcel the Owner moves Pending → Ordered from vendor → Shipped (courier
/// required, tracking optional) → Delivered. The order follows its parcels. Every step locks the order and is audited.
/// </summary>
internal sealed class ParcelService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    FulfillmentService fulfillment,
    OrderQueryService orders,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    public Task<OrderDto> MarkOrderedAsync(Guid orderId, Guid parcelId, ParcelOrderedRequest r, CancellationToken ct)
    {
        var reference = Clip(r.VendorReference, 100, "The vendor's reference must be at most 100 characters.");
        var note = Clip(r.Note, 1000, "The note must be at most 1000 characters.");
        return StepAsync(orderId, parcelId, "orders.parcel.ordered_from_vendor", (_, p) =>
        {
            p.MarkOrderedFromVendor(reference, note, clock.UtcNow);
            return new { reference };
        }, ct);
    }

    public Task<OrderDto> MarkShippedAsync(Guid orderId, Guid parcelId, ShipOrderRequest r, CancellationToken ct)
    {
        var (courier, courierName, tracking) = FulfillmentService.ValidateShipment(r);
        var note = Clip(r.Note, 1000, "The note must be at most 1000 characters.");
        return StepAsync(orderId, parcelId, "orders.parcel.shipped", (o, p) =>
        {
            p.MarkShipped(courier, courierName, tracking, note, clock.UtcNow);
            outbox.Enqueue(new ParcelShipped(o.Id, o.Number, p.Id, p.VendorName, courierName ?? Label(courier), tracking));
            return new { courier, courierName, tracking };
        }, ct);
    }

    public Task<OrderDto> MarkDeliveredAsync(Guid orderId, Guid parcelId, DeliverOrderRequest r, CancellationToken ct)
    {
        var note = Clip(r.Note, 1000, "The note must be at most 1000 characters.");
        return StepAsync(orderId, parcelId, "orders.parcel.delivered", (_, p) =>
        {
            var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(Ist).DateTime);
            var on = r.DeliveredOn ?? today;
            var shippedOn = DateOnly.FromDateTime(p.ShippedAt!.Value.ToOffset(Ist).DateTime);
            if (on > today || on < shippedOn)
            {
                throw new BusinessRuleException("DELIVERY_DATE_INVALID", $"The delivery date must be between {shippedOn:dd MMM yyyy} and today.", 400);
            }
            p.MarkDelivered(on, note, clock.UtcNow);
            return new { deliveredOn = on };
        }, ct);
    }

    private Task<OrderDto> StepAsync(Guid orderId, Guid parcelId, string action, Func<Order, OrderParcel, object> step, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var order = await OrderLocking.LockAsync(db, orderId, innerCt);
            await fulfillment.EnsureOrderPermissionAsync(P.Orders.Fulfill, order, innerCt);
            if (order.FulfillmentMode != FulfillmentMode.Vendor)
            {
                throw new BusinessRuleException("BRANCH_ORDER", "This order is fulfilled by a branch, not by vendor parcels.", 409);
            }
            if (order.Status is not (OrderStatus.Confirmed or OrderStatus.Processing or OrderStatus.Shipped))
            {
                throw new BusinessRuleException("ORDER_STATUS_INVALID", $"The order is {order.Status}; its parcels can no longer change.", 409);
            }
            var parcels = await db.Set<OrderParcel>().Where(p => p.OrderId == orderId).ToListAsync(innerCt);
            var parcel = parcels.SingleOrDefault(p => p.Id == parcelId) ?? throw new NotFoundException("PARCEL_NOT_FOUND", "Parcel not found on this order.");
            var details = step(order, parcel);
            await audit.RecordAsync(new AuditRecord(action, "Order", orderId.ToString(),
                After: new { order.Number, parcel = parcel.VendorName, status = parcel.Status.ToString(), details }), innerCt);
            if (order.FollowParcels(parcels) is { } from)
            {
                db.Add(new OrderStatusChange(order.Id, from, order.Status, currentUser.UserId, $"{parcel.VendorName} parcel {parcel.Status}", clock.UtcNow));
                outbox.Enqueue(new OrderStatusChanged(order.Id, order.Number, order.Channel.ToString(), order.Status.ToString(), null, nameof(FulfillmentMode.Vendor)));
            }
            await db.SaveChangesAsync(innerCt);
            return await orders.GetAsync(orderId, innerCt);
        }, ct);

    private static string Label(string courier) => courier switch
    {
        "XPRESSBEES" => "Xpressbees",
        "DELHIVERY" => "Delhivery",
        _ => courier,
    };

    private static string? Clip(string? value, int max, string message)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return v is { Length: var n } && n > max ? throw new BusinessRuleException("TEXT_TOO_LONG", message, 400) : v;
    }
}
