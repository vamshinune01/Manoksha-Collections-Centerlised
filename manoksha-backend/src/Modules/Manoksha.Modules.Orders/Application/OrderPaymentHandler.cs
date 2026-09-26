using Manoksha.Application.Abstractions;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Orders.Application;

/// <summary>
/// What a payment means for an ONLINE order. Runs inside the payment-processing transaction (attempt row locked), and locks the
/// order before its reservation — the same order the sweeper uses — so the two never deadlock.
/// </summary>
internal sealed class OrderPaymentHandler(
    ManokshaDbContext db,
    IStockAllocator allocator,
    FulfillmentRouter router,
    IAuditWriter audit,
    IOutbox outbox,
    IClock clock) : IPaymentPurposeHandler
{
    public string Purpose => PaymentPurposes.Order;

    public async Task<PaymentHandlingResult> OnPaymentSucceededAsync(PaymentSuccess success, CancellationToken cancellationToken)
    {
        var order = await LockOrderAsync(success.ReferenceId, cancellationToken);
        if (order.Status is not (OrderStatus.PaymentPending or OrderStatus.PaymentExpired or OrderStatus.PaymentFailed))
        {
            // Already confirmed by this payment (the attempt lock makes a second confirmation impossible) — nothing to do.
            return new PaymentHandlingResult(PaymentOutcome.Confirmed);
        }
        var now = clock.UtcNow;
        var reservation = await LockActiveReservationAsync(order.Id, cancellationToken);

        // SPEC §14.1: valid payment while the reservation is still valid → finalise the reserved stock.
        if (success.WithinWindow && reservation is not null && now < reservation.ExpiresAt && order.Status == OrderStatus.PaymentPending)
        {
            var lines = await db.Set<ReservationLine>().Where(l => l.ReservationId == reservation.Id).ToListAsync(cancellationToken);
            var sold = await allocator.CommitReservedAsync(reservation.BranchId, lines.Select(ToReserved).ToList(), "Order", order.Id, order.Number, cancellationToken);
            foreach (var line in lines)
            {
                line.RecordCost(sold.Single(s => s.SkuId == line.SkuId).CostAmount);
            }
            reservation.Close(ReservationStatus.Consumed, "Payment confirmed", now);
            var from = order.ConfirmOnlinePaid(success.AttemptId, now);
            db.Add(new OrderStatusChange(order.Id, from, OrderStatus.Confirmed, null, "Payment confirmed; reserved stock sold", now));
            await audit.RecordAsync(new AuditRecord("orders.online_order.confirmed", "Order", order.Id.ToString(),
                After: new { order.Number, order.GrandTotal, branchId = reservation.BranchId, paymentAttemptId = success.AttemptId, success.ProviderPaymentRef },
                BranchId: reservation.BranchId), cancellationToken);
            outbox.Enqueue(new OrderConfirmed(order.Id, order.Number, order.Channel.ToString(), reservation.BranchId));
            await db.SaveChangesAsync(cancellationToken);
            return new PaymentHandlingResult(PaymentOutcome.Confirmed);
        }

        // SPEC §14.2: never blindly confirm. Release any stale hold, then re-run branch resolution for the snapshotted basket.
        if (reservation is not null)
        {
            await ReleaseAsync(order, reservation, ReservationStatus.Expired, "Payment arrived after the reservation window; rechecking stock", cancellationToken);
        }
        if (order.Status == OrderStatus.PaymentPending)
        {
            order.MarkPaymentExpired();
            db.Add(new OrderStatusChange(order.Id, OrderStatus.PaymentPending, OrderStatus.PaymentExpired, null, "Reservation window ended before the payment was confirmed", now));
        }
        var orderLines = await db.Set<OrderLine>().Where(l => l.OrderId == order.Id).ToListAsync(cancellationToken);
        var basket = orderLines.Select(l => new BasketLine(l.SkuId, l.Quantity)).ToList();
        var (resolution, evaluations) = await router.ResolveAsync(basket, StockHold.Sell, order.Id, order.Number, cancellationToken);
        if (resolution is null)
        {
            db.Add(new OrderStatusChange(order.Id, order.Status, order.Status, null,
                "Payment received late, but no single branch can fulfil the complete order — sent to Owner reconciliation", now));
            await audit.RecordAsync(new AuditRecord("orders.online_order.late_payment_unfulfillable", "Order", order.Id.ToString(),
                After: new { order.Number, order.GrandTotal, paymentAttemptId = success.AttemptId, success.ProviderPaymentRef, evaluations }), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new PaymentHandlingResult(PaymentOutcome.NotFulfillable, "INVENTORY_UNAVAILABLE_AFTER_LATE_PAYMENT",
                $"Order {order.Number}: payment received after the reservation expired and no single branch can fulfil the complete basket any more.");
        }

        var recovered = new Reservation(order.Id, resolution.BranchId, now, now, ReservationStatus.Consumed);
        db.Add(recovered);
        foreach (var line in orderLines)
        {
            var sold = resolution.Allocation.Lines.Single(a => a.SkuId == line.SkuId);
            db.Add(new ReservationLine(recovered.Id, line.Id, line.SkuId, line.Quantity, [.. sold.ItemIds], sold.CostAmount));
        }
        var previousBranch = order.FulfillmentBranchId;
        var previous = order.RecoverLatePayment(success.AttemptId, resolution.BranchId, now);
        db.Add(new OrderStatusChange(order.Id, previous, OrderStatus.Confirmed, null, "Late payment recovered: complete basket re-acquired at original prices", now));
        await audit.RecordAsync(new AuditRecord("orders.online_order.recovered", "Order", order.Id.ToString(),
            Before: new { branchId = previousBranch, status = previous.ToString() },
            After: new { order.Number, order.GrandTotal, branchId = resolution.BranchId, paymentAttemptId = success.AttemptId, success.ProviderPaymentRef, evaluations },
            BranchId: resolution.BranchId), cancellationToken);
        outbox.Enqueue(new OrderConfirmed(order.Id, order.Number, order.Channel.ToString(), resolution.BranchId));
        await db.SaveChangesAsync(cancellationToken);
        return new PaymentHandlingResult(PaymentOutcome.Recovered);
    }

    public async Task OnPaymentFailedAsync(Guid referenceId, string reason, CancellationToken cancellationToken)
    {
        var order = await LockOrderAsync(referenceId, cancellationToken);
        var reservation = await LockActiveReservationAsync(order.Id, cancellationToken);
        if (reservation is not null)
        {
            await ReleaseAsync(order, reservation, ReservationStatus.Released, reason, cancellationToken);
        }
        if (order.Status == OrderStatus.PaymentPending)
        {
            order.MarkPaymentFailed();
            db.Add(new OrderStatusChange(order.Id, OrderStatus.PaymentPending, OrderStatus.PaymentFailed, null, reason, clock.UtcNow));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnPaymentExpiredAsync(Guid referenceId, CancellationToken cancellationToken)
    {
        var order = await LockOrderAsync(referenceId, cancellationToken);
        await ExpireAsync(order, cancellationToken);
    }

    /// <summary>Releases an expired hold (sweeper or payment expiry). The caller holds the order lock.</summary>
    internal async Task ExpireAsync(Order order, CancellationToken ct)
    {
        var reservation = await LockActiveReservationAsync(order.Id, ct);
        if (reservation is not null && reservation.ExpiresAt > clock.UtcNow)
        {
            return; // not due yet
        }
        if (reservation is not null)
        {
            await ReleaseAsync(order, reservation, ReservationStatus.Expired, "Payment window ended", ct);
        }
        if (order.Status == OrderStatus.PaymentPending)
        {
            order.MarkPaymentExpired();
            db.Add(new OrderStatusChange(order.Id, OrderStatus.PaymentPending, OrderStatus.PaymentExpired, null, "Payment window ended; reserved stock released", clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task ReleaseAsync(Order order, Reservation reservation, ReservationStatus to, string reason, CancellationToken ct)
    {
        var lines = await db.Set<ReservationLine>().AsNoTracking().Where(l => l.ReservationId == reservation.Id).ToListAsync(ct);
        await allocator.ReleaseReservedAsync(reservation.BranchId, lines.Select(ToReserved).ToList(), "Order", order.Id, order.Number, reason, ct);
        reservation.Close(to, reason, clock.UtcNow);
    }

    internal async Task<Order> LockOrderAsync(Guid orderId, CancellationToken ct)
    {
        db.Forget<Order>(o => o.Id == orderId);
        return await db.Set<Order>().FromSqlInterpolated($"SELECT *, xmin FROM orders.orders WHERE id = {orderId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ORDER_NOT_FOUND", "Order not found.");
    }

    private async Task<Reservation?> LockActiveReservationAsync(Guid orderId, CancellationToken ct)
    {
        db.Forget<Reservation>(r => r.OrderId == orderId);
        return await db.Set<Reservation>().FromSqlInterpolated($"SELECT *, xmin FROM orders.reservations WHERE order_id = {orderId} AND status = 'Active' FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    }

    private static ReservedLine ToReserved(ReservationLine l) => new(l.SkuId, l.Quantity, l.ItemIds);
}
