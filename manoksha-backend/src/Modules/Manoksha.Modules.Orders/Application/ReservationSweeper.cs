using Manoksha.Application.Abstractions;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Orders.Application;

/// <summary>
/// Physically releases reservations whose window has ended (SPEC §13): RESERVED → AVAILABLE, reservation EXPIRED, order
/// PAYMENT_EXPIRED. Each order is handled in its own transaction and skipped if a payment is being processed for it right now
/// (SKIP LOCKED) — the payment path then decides. The payment attempt itself is expired by the payment poller after asking the
/// provider, so a payment that did arrive is never lost.
/// </summary>
internal sealed class ReservationSweeper(ManokshaDbContext db, IUnitOfWork unitOfWork, OrderPaymentHandler handler, IClock clock, ILogger<ReservationSweeper> logger)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.Set<Reservation>().AsNoTracking().Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt).Take(100).Select(r => r.OrderId).ToListAsync(ct);
        var released = 0;
        foreach (var orderId in due)
        {
            try
            {
                released += await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
                {
                    var order = await db.Set<Order>().FromSqlInterpolated($"SELECT *, xmin FROM orders.orders WHERE id = {orderId} FOR UPDATE SKIP LOCKED")
                        .SingleOrDefaultAsync(innerCt);
                    if (order is null)
                    {
                        return 0;
                    }
                    await handler.ExpireAsync(order, innerCt);
                    return 1;
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Releasing the expired reservation of order {OrderId} failed", orderId);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        return released;
    }
}
