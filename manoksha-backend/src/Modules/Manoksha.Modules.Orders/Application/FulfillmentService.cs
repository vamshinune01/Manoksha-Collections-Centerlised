using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Orders.Application;

public sealed record OrderStatusChanged(Guid OrderId, string OrderNumber, string Channel, string Status, Guid FulfillmentBranchId) : IIntegrationEvent
{
    public static string EventType => "orders.order_status_changed";
}

public sealed record FulfillmentExceptionRaised(Guid ExceptionId, Guid OrderId, string OrderNumber, Guid BranchId, string Reason) : IIntegrationEvent
{
    public static string EventType => "orders.fulfillment_exception_raised";
}

internal sealed record AllocationLine(Guid SkuId, int Quantity, Guid[] ItemIds, decimal? Cost);

/// <summary>Where an order's sold stock currently is: the latest sold allocation record, else (reseller orders) the order lines.</summary>
internal sealed record CurrentAllocation(Guid BranchId, Reservation? Record, IReadOnlyList<AllocationLine> Lines);

internal static class OrderLocking
{
    public static async Task<Order> LockAsync(ManokshaDbContext db, Guid orderId, CancellationToken ct)
    {
        db.Forget<Order>(o => o.Id == orderId);
        return await db.Set<Order>().FromSqlInterpolated($"SELECT *, xmin FROM orders.orders WHERE id = {orderId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ORDER_NOT_FOUND", "Order not found.");
    }

    public static async Task<CurrentAllocation> CurrentAllocationAsync(ManokshaDbContext db, Order order, CancellationToken ct)
    {
        var record = await db.Set<Reservation>().Where(r => r.OrderId == order.Id && r.Status == ReservationStatus.Consumed)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        if (record is not null)
        {
            var lines = await db.Set<ReservationLine>().AsNoTracking().Where(l => l.ReservationId == record.Id).ToListAsync(ct);
            return new CurrentAllocation(record.BranchId, record, lines.Select(l => new AllocationLine(l.SkuId, l.Quantity, l.ItemIds, l.CostAmount)).ToList());
        }
        var orderLines = await db.Set<OrderLine>().AsNoTracking().Where(l => l.OrderId == order.Id).ToListAsync(ct);
        return new CurrentAllocation(order.FulfillmentBranchId, null, orderLines.Select(l => new AllocationLine(l.SkuId, l.Quantity, l.ItemIds, l.CostAmount)).ToList());
    }
}

/// <summary>
/// Branch fulfillment of confirmed orders (SPEC §19, §22, §27.1; ADR-001 §8): processing → packed → shipped → delivered, fulfillment
/// exceptions, whole-order reroute and administrative cancellation. Every step locks the order row, validates the transition,
/// writes status history and audit, and runs in one transaction. Branch staff act only on orders of their branch.
/// </summary>
internal sealed class FulfillmentService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IPermissionService permissions,
    IStockAllocator allocator,
    IOrderStock orderStock,
    ICatalogLookup catalog,
    ICatalogBarcodes barcodes,
    IFulfillmentPriorityProvider priority,
    IBranchDirectory branches,
    IWallets wallets,
    IPayments payments,
    OrderQueryService orders,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    public static readonly string[] Couriers = ["XPRESSBEES", "DELHIVERY", "OTHER"];

    // ---- Normal flow ----

    public Task<OrderDto> StartProcessingAsync(Guid id, FulfillmentStepRequest r, CancellationToken ct) =>
        StepAsync(id, P.Orders.Fulfill, "orders.order.processing", r.Note, o => o.StartProcessing(), ct);

    public Task<OrderDto> MarkPackedAsync(Guid id, FulfillmentStepRequest r, CancellationToken ct) =>
        StepAsync(id, P.Orders.Fulfill, "orders.order.packed", r.Note, o => o.MarkPacked(), ct);

    public Task<OrderDto> MarkShippedAsync(Guid id, ShipOrderRequest r, CancellationToken ct)
    {
        var courier = (r.Courier ?? string.Empty).Trim().ToUpperInvariant();
        if (!Couriers.Contains(courier))
        {
            throw new BusinessRuleException("COURIER_INVALID", "Choose the courier: Xpressbees, Delhivery or Other.", 400);
        }
        var courierName = r.CourierName?.Trim();
        if (courier == "OTHER" && string.IsNullOrWhiteSpace(courierName))
        {
            throw new BusinessRuleException("COURIER_NAME_REQUIRED", "Enter the courier's name.", 400);
        }
        var tracking = string.IsNullOrWhiteSpace(r.TrackingNumber) ? null : r.TrackingNumber.Trim();
        if (tracking is { Length: > 100 } || courierName is { Length: > 100 })
        {
            throw new BusinessRuleException("SHIPMENT_INVALID", "Courier name and tracking number must be at most 100 characters.", 400);
        }
        return StepAsync(id, P.Orders.Fulfill, "orders.order.shipped", r.Note, o =>
        {
            var from = o.MarkShipped();
            db.Add(new Shipment(o.Id, o.FulfillmentBranchId, courier, courier == "OTHER" ? courierName : null, tracking, currentUser.UserId, clock.UtcNow));
            return from;
        }, ct, new { courier, courierName, tracking });
    }

    public async Task<OrderDto> MarkDeliveredAsync(Guid id, DeliverOrderRequest r, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var on = r.DeliveredOn ?? today;
        return await StepAsync(id, P.Orders.Fulfill, "orders.order.delivered", r.Note, o =>
        {
            var shipment = db.Set<Shipment>().Single(s => s.OrderId == o.Id);
            var shippedOn = DateOnly.FromDateTime(shipment.ShippedAt.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
            if (on > today || on < shippedOn)
            {
                throw new BusinessRuleException("DELIVERY_DATE_INVALID", $"The delivery date must be between {shippedOn:dd MMM yyyy} and today.", 400);
            }
            var from = o.MarkDelivered();
            shipment.RecordDelivered(on, r.Note?.Trim(), currentUser.UserId, clock.UtcNow);
            return from;
        }, ct, new { deliveredOn = on });
    }

    private Task<OrderDto> StepAsync(Guid id, string permission, string action, string? note, Func<Order, OrderStatus> step, CancellationToken ct, object? details = null) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var order = await OrderLocking.LockAsync(db, id, innerCt);
            await permissions.EnsurePermissionForBranchAsync(permission, order.FulfillmentBranchId, innerCt);
            var from = step(order);
            await FinishAsync(order, from, action, note?.Trim(), details, innerCt);
            return await orders.GetAsync(id, innerCt);
        }, ct);

    // ---- Fulfillment exception (SPEC §22) ----

    public Task<OrderDto> RaiseExceptionAsync(Guid id, RaiseFulfillmentExceptionRequest r, CancellationToken ct)
    {
        var reason = (r.Reason ?? string.Empty).Trim().ToUpperInvariant();
        if (!FulfillmentException.Reasons.Contains(reason))
        {
            throw new BusinessRuleException("EXCEPTION_REASON_INVALID", "Choose ITEM_NOT_FOUND, DAMAGED, INVENTORY_MISMATCH or OTHER.", 400);
        }
        var notes = RequireText(r.Notes, "Describe what happened.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var order = await OrderLocking.LockAsync(db, id, innerCt);
            await permissions.EnsurePermissionForBranchAsync(P.Orders.FulfillmentExceptionRaise, order.FulfillmentBranchId, innerCt);
            var allocation = await OrderLocking.CurrentAllocationAsync(db, order, innerCt);
            var issues = await ResolveIssuesAsync(allocation, r.Lines, innerCt);
            var from = order.RaiseFulfillmentException();
            var ex = new FulfillmentException(order.Id, order.FulfillmentBranchId, reason, notes, currentUser.UserId, clock.UtcNow);
            db.Add(ex);
            foreach (var i in issues.Where(i => i.MissingQty + i.DamagedQty > 0))
            {
                db.Add(new FulfillmentExceptionLine(ex.Id, i.SkuId, i.MissingQty, i.DamagedQty, [.. i.MissingItemIds], [.. i.DamagedItemIds]));
            }
            outbox.Enqueue(new FulfillmentExceptionRaised(ex.Id, order.Id, order.Number, order.FulfillmentBranchId, reason));
            await FinishAsync(order, from, "orders.fulfillment_exception.raised", $"{reason}: {notes}",
                new { exceptionId = ex.Id, reason, lines = issues.Select(i => new { i.SkuId, i.MissingQty, i.DamagedQty }) }, innerCt);
            return await orders.GetAsync(id, innerCt);
        }, ct);
    }

    /// <summary>Resolved at the same branch (e.g. the piece was found) — back to processing, no stock change.</summary>
    public Task<OrderDto> ResolveInPlaceAsync(Guid id, ResolveExceptionRequest r, CancellationToken ct)
    {
        var note = RequireText(r.Note, "Explain how it was resolved.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var order = await OrderLocking.LockAsync(db, id, innerCt);
            await permissions.EnsurePermissionForBranchAsync(P.Orders.Reroute, order.FulfillmentBranchId, innerCt);
            var ex = await OpenExceptionAsync(order.Id, innerCt) ?? throw new BusinessRuleException("EXCEPTION_NOT_OPEN", "This order has no open fulfillment exception.", 409);
            var from = order.ResumeAfterException();
            ex.Close(FulfillmentExceptionStatus.ResolvedInPlace, note, currentUser.UserId, clock.UtcNow);
            await FinishAsync(order, from, "orders.fulfillment_exception.resolved", $"Resolved at the same branch: {note}", new { exceptionId = ex.Id }, innerCt);
            return await orders.GetAsync(id, innerCt);
        }, ct);
    }

    /// <summary>Branches that could fulfil the COMPLETE order now (SPEC §22: never split). The reroute re-checks under lock.</summary>
    public async Task<IReadOnlyList<RerouteOptionDto>> RerouteOptionsAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Set<Order>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("ORDER_NOT_FOUND", "Order not found.");
        await permissions.EnsurePermissionForBranchAsync(P.Orders.Reroute, order.FulfillmentBranchId, ct);
        var lines = await db.Set<OrderLine>().AsNoTracking().Where(l => l.OrderId == id).ToListAsync(ct);
        var (_, entries) = await priority.GetCurrentAsync(ct);
        var result = new List<RerouteOptionDto>();
        foreach (var e in entries.Where(e => e.BranchId != order.FulfillmentBranchId))
        {
            var available = await orderStock.AvailableAtBranchAsync(e.BranchId, lines.Select(l => l.SkuId).ToList(), ct);
            var shortfalls = lines.Where(l => available[l.SkuId] < l.Quantity).Select(l => new ShortfallDto(l.SkuId, l.Quantity, available[l.SkuId])).ToList();
            result.Add(new RerouteOptionDto(e.BranchId, e.BranchName, e.Priority, e.IsActive, e.IsActive && shortfalls.Count == 0, shortfalls));
        }
        return result;
    }

    /// <summary>
    /// Whole-order reroute (SPEC §22): the target branch must fulfil the complete order (sold there now), the original branch's
    /// stock returns — good units to AVAILABLE, damaged to DAMAGED, missing written off with a discrepancy — and the reroute is
    /// recorded with its inventory effects. Prices, payment and wallet are unchanged.
    /// </summary>
    public Task<OrderDto> RerouteAsync(Guid id, RerouteRequest r, CancellationToken ct)
    {
        var reason = RequireText(r.Reason, "A reason is required to reroute.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var order = await OrderLocking.LockAsync(db, id, innerCt);
            await permissions.EnsurePermissionForBranchAsync(P.Orders.Reroute, order.FulfillmentBranchId, innerCt);
            if (order.Status != OrderStatus.FulfillmentException)
            {
                throw new BusinessRuleException("REROUTE_NEEDS_EXCEPTION", "Only an order with an open fulfillment exception can be rerouted.", 409);
            }
            var ex = await OpenExceptionAsync(order.Id, innerCt) ?? throw new BusinessRuleException("EXCEPTION_NOT_OPEN", "This order has no open fulfillment exception.", 409);
            var target = await branches.FindAsync(r.TargetBranchId, innerCt);
            if (target is null || !target.IsActive || target.Id == order.FulfillmentBranchId)
            {
                throw new BusinessRuleException("REROUTE_TARGET_INVALID", "Choose another active branch.", 400);
            }

            var current = await OrderLocking.CurrentAllocationAsync(db, order, innerCt);
            var orderLines = await db.Set<OrderLine>().AsNoTracking().Where(l => l.OrderId == order.Id).ToListAsync(innerCt);
            var sold = await allocator.TrySellBasketAsync(target.Id, orderLines.Select(l => new BasketLine(l.SkuId, l.Quantity)).ToList(), "Order", order.Id, order.Number, innerCt);
            if (!sold.Success)
            {
                var err = new BusinessRuleException("REROUTE_TARGET_CANNOT_FULFIL", $"{target.Name} cannot fulfil the complete order.", 409);
                err.Details["shortfalls"] = sold.Shortfalls;
                throw err;
            }

            var issues = await ExceptionIssuesAsync(ex.Id, current, innerCt);
            var returned = await orderStock.ReturnSoldStockAsync(current.BranchId, issues, "Order", order.Id, order.Number, $"Rerouted to {target.Name}: {reason}", innerCt);
            current.Record?.MarkReturned($"Rerouted to {target.Name}", clock.UtcNow);

            var now = clock.UtcNow;
            var record = new Reservation(order.Id, target.Id, now, now, ReservationStatus.Consumed);
            db.Add(record);
            foreach (var line in orderLines)
            {
                var s = sold.Lines.Single(a => a.SkuId == line.SkuId);
                db.Add(new ReservationLine(record.Id, line.Id, line.SkuId, line.Quantity, [.. s.ItemIds], s.CostAmount));
            }
            var fromBranch = order.FulfillmentBranchId;
            var from = order.Reroute(target.Id);
            ex.Close(FulfillmentExceptionStatus.Rerouted, $"Rerouted to {target.Name}: {reason}", currentUser.UserId, now);
            var effects = new { returnedAt = fromBranch, returned.Lines, returned.DiscrepancyNumbers, soldAt = target.Id, sold = sold.Lines.Select(l => new { l.SkuId, l.Quantity, l.CostAmount }) };
            db.Add(new OrderReroute(order.Id, fromBranch, target.Id, reason, ex.Id, JsonSerializer.Serialize(effects, JsonDefaults.Options), currentUser.UserId, now));
            await FinishAsync(order, from, "orders.order.rerouted", $"Rerouted to {target.Name}: {reason}",
                new { fromBranchId = fromBranch, toBranchId = target.Id, exceptionId = ex.Id, effects }, innerCt);
            return await orders.GetAsync(id, innerCt);
        }, ct);
    }

    // ---- Administrative cancellation (ADR-001 §8) ----

    public Task<CancelOrderResult> CancelAsync(Guid id, CancelOrderRequest r, CancellationToken ct)
    {
        var reason = RequireText(r.Reason, "A reason is required to cancel an order.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var order = await OrderLocking.LockAsync(db, id, innerCt);
            await permissions.EnsurePermissionForBranchAsync(P.Orders.Cancel, order.FulfillmentBranchId, innerCt);
            var from = order.Cancel();

            // Stock: everything returns to AVAILABLE except units reported damaged or missing (Phase 7 decision).
            var current = await OrderLocking.CurrentAllocationAsync(db, order, innerCt);
            var ex = await OpenExceptionAsync(order.Id, innerCt);
            var issues = r.Lines is { Count: > 0 } ? await ResolveIssuesAsync(current, r.Lines, innerCt)
                : ex is not null ? await ExceptionIssuesAsync(ex.Id, current, innerCt)
                : await ResolveIssuesAsync(current, [], innerCt);
            var returned = await orderStock.ReturnSoldStockAsync(current.BranchId, issues, "Order", order.Id, order.Number, $"Order cancelled: {reason}", innerCt);
            current.Record?.MarkReturned("Order cancelled", clock.UtcNow);
            ex?.Close(FulfillmentExceptionStatus.Cancelled, $"Order cancelled: {reason}", currentUser.UserId, clock.UtcNow);

            // Money: a reseller's wallet debit is reversed by a linked REVERSAL entry; an online payment needs Owner reconciliation.
            decimal? refunded = null;
            string? caseNumber = null;
            if (order.Channel == OrderChannel.Reseller)
            {
                var reversal = await wallets.ReverseOrderDebitAsync(order.Id, $"Order {order.Number} cancelled: {reason}", innerCt);
                refunded = reversal.BalanceAfter - reversal.BalanceBefore;
            }
            else if (order.Channel == OrderChannel.Online)
            {
                caseNumber = await payments.OpenReconciliationAsync(PaymentPurposes.Order, order.Id, "ORDER_CANCELLED_AFTER_PAYMENT",
                    $"Order {order.Number} was cancelled after payment: {reason}", innerCt);
            }

            await FinishAsync(order, from, "orders.order.cancelled", $"Cancelled: {reason}",
                new { returned.Lines, returned.DiscrepancyNumbers, walletRefunded = refunded, reconciliationCase = caseNumber }, innerCt);
            return new CancelOrderResult(await orders.GetAsync(id, innerCt), refunded, caseNumber, returned.DiscrepancyNumbers);
        }, ct);
    }

    // ---- Queues ----

    public async Task<IReadOnlyList<FulfillmentExceptionDto>> ListExceptionsAsync(string? status, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var visible = access.BranchesWith(P.Orders.View);
        var q = db.Set<FulfillmentException>().AsNoTracking();
        if (visible is not null)
        {
            q = q.Where(e => visible.Contains(e.BranchId));
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<FulfillmentExceptionStatus>(status, true, out var s))
        {
            q = q.Where(e => e.Status == s);
        }
        return await orders.ExceptionDtosAsync(await q.OrderByDescending(e => e.RaisedAt).Take(200).ToListAsync(ct), ct);
    }

    // ---- helpers ----

    private async Task FinishAsync(Order order, OrderStatus from, string action, string? note, object? details, CancellationToken ct)
    {
        db.Add(new OrderStatusChange(order.Id, from, order.Status, currentUser.UserId, note, clock.UtcNow));
        await audit.RecordAsync(new AuditRecord(action, "Order", order.Id.ToString(), Before: new { status = from.ToString() },
            After: new { order.Number, status = order.Status.ToString(), branchId = order.FulfillmentBranchId, details }, Reason: note, BranchId: order.FulfillmentBranchId), ct);
        outbox.Enqueue(new OrderStatusChanged(order.Id, order.Number, order.Channel.ToString(), order.Status.ToString(), order.FulfillmentBranchId));
        await db.SaveChangesAsync(ct);
    }

    private async Task<FulfillmentException?> OpenExceptionAsync(Guid orderId, CancellationToken ct) =>
        await db.Set<FulfillmentException>().SingleOrDefaultAsync(e => e.OrderId == orderId && e.Status == FulfillmentExceptionStatus.Open, ct);

    private async Task<List<SoldStockReturn>> ExceptionIssuesAsync(Guid exceptionId, CurrentAllocation current, CancellationToken ct)
    {
        var lines = await db.Set<FulfillmentExceptionLine>().AsNoTracking().Where(l => l.ExceptionId == exceptionId).ToListAsync(ct);
        return current.Lines.Select(a =>
        {
            var l = lines.FirstOrDefault(x => x.SkuId == a.SkuId);
            // Pieces reported at another branch no longer apply after a reroute; only this allocation's pieces count.
            var missingItems = (l?.MissingItemIds ?? []).Where(a.ItemIds.Contains).ToList();
            var damagedItems = (l?.DamagedItemIds ?? []).Where(a.ItemIds.Contains).ToList();
            return a.ItemIds.Length > 0
                ? new SoldStockReturn(a.SkuId, a.Quantity, a.ItemIds, damagedItems.Count, damagedItems, missingItems.Count, missingItems)
                : new SoldStockReturn(a.SkuId, a.Quantity, [], l?.DamagedQty ?? 0, [], l?.MissingQty ?? 0, []);
        }).ToList();
    }

    /// <summary>Validates reported missing/damaged units against the order's current allocation; serialized pieces by barcode.</summary>
    private async Task<List<SoldStockReturn>> ResolveIssuesAsync(CurrentAllocation current, IReadOnlyList<StockIssueLineRequest>? requested, CancellationToken ct)
    {
        var req = (requested ?? []).ToList();
        if (req.Exists(r => current.Lines.All(a => a.SkuId != r.SkuId)) || req.Select(r => r.SkuId).Distinct().Count() != req.Count)
        {
            throw new BusinessRuleException("ORDER_LINE_INVALID", "Report each item of this order at most once.", 400);
        }
        var skus = await catalog.FindSkusAsync(current.Lines.Select(l => l.SkuId).ToList(), ct);
        var result = new List<SoldStockReturn>();
        foreach (var a in current.Lines)
        {
            var r = req.FirstOrDefault(x => x.SkuId == a.SkuId);
            if (skus[a.SkuId].TrackingMode == "Serialized")
            {
                var missing = await ItemsAsync(a, r?.MissingBarcodes, ct);
                var damaged = await ItemsAsync(a, r?.DamagedBarcodes, ct);
                if (missing.Intersect(damaged).Any())
                {
                    throw new BusinessRuleException("ORDER_ITEMS_INVALID", "A piece cannot be both missing and damaged.", 400);
                }
                result.Add(new SoldStockReturn(a.SkuId, a.Quantity, a.ItemIds, damaged.Count, damaged, missing.Count, missing));
            }
            else
            {
                var missingQty = r?.MissingQty ?? 0;
                var damagedQty = r?.DamagedQty ?? 0;
                if (missingQty < 0 || damagedQty < 0 || missingQty + damagedQty > a.Quantity)
                {
                    throw new BusinessRuleException("ORDER_QTY_INVALID", $"Missing plus damaged units cannot exceed the ordered quantity ({a.Quantity}).", 400);
                }
                result.Add(new SoldStockReturn(a.SkuId, a.Quantity, [], damagedQty, [], missingQty, []));
            }
        }
        return result;
    }

    private async Task<List<Guid>> ItemsAsync(AllocationLine line, IReadOnlyList<string>? codes, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var code in (codes ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct())
        {
            var found = await barcodes.FindAsync(code, ct);
            if (found?.InventoryItemId is not { } itemId || !line.ItemIds.Contains(itemId))
            {
                throw new BusinessRuleException("ORDER_ITEMS_INVALID", $"Barcode {code} is not a piece of this order.", 400);
            }
            ids.Add(itemId);
        }
        return ids;
    }

    private static string RequireText(string? value, string message)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is < 3 or > 2000)
        {
            throw new BusinessRuleException("REASON_REQUIRED", message, 400);
        }
        return text;
    }
}
