using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Orders.Application;

internal sealed class OrderQueryService(
    ManokshaDbContext db,
    IPermissionService permissions,
    IResellerDirectory resellers,
    IBranchDirectory branches,
    WhatsApp whatsApp,
    ICurrentUser currentUser,
    IApprovals approvals)
{
    // ---- Reseller: own orders only (SPEC §24). Another reseller's order id is simply "not found". ----

    public async Task<IReadOnlyList<OrderDto>> ListMineAsync(CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        var ids = await db.Set<Order>().AsNoTracking().Where(o => o.ResellerId == me.ResellerId && o.Status != OrderStatus.CheckoutAttempt)
            .OrderByDescending(o => o.CreatedAt).Take(200).Select(o => o.Id).ToListAsync(ct);
        return await ToDtosAsync(ids, includeCost: false, ct, external: true);
    }

    public async Task<OrderDto> GetMineAsync(Guid id, CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        if (!await db.Set<Order>().AnyAsync(o => o.Id == id && o.ResellerId == me.ResellerId, ct))
        {
            throw NotFound();
        }
        return (await ToDtosAsync([id], includeCost: false, ct, external: true))[0];
    }

    // ---- Customer: own ONLINE orders only (SPEC §24). Another customer's order id is simply "not found". ----

    public async Task<IReadOnlyList<OrderDto>> ListForCustomerAsync(CancellationToken ct)
    {
        var me = currentUser.UserId;
        var ids = await db.Set<Order>().AsNoTracking().Where(o => o.CustomerUserId == me && o.Status != OrderStatus.CheckoutAttempt)
            .OrderByDescending(o => o.CreatedAt).Take(200).Select(o => o.Id).ToListAsync(ct);
        return await ToDtosAsync(ids, includeCost: false, ct, external: true);
    }

    public async Task<OrderDto> GetForCustomerAsync(Guid id, CancellationToken ct)
    {
        var me = currentUser.UserId;
        if (!await db.Set<Order>().AnyAsync(o => o.Id == id && o.CustomerUserId == me, ct))
        {
            throw NotFound();
        }
        return (await ToDtosAsync([id], includeCost: false, ct, external: true))[0];
    }

    /// <summary>Delivery details of the customer's latest online order, to prefill checkout.</summary>
    public async Task<DeliveryDto?> LastDeliveryAsync(CancellationToken ct)
    {
        var me = currentUser.UserId;
        var last = await db.Set<Order>().AsNoTracking().Where(o => o.CustomerUserId == me).OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        return last is null ? null : ResellerCustomerService.ToDto(last.Delivery);
    }

    // ---- Internal users: scoped by fulfillment branch ----

    public async Task<IReadOnlyList<OrderDto>> ListAsync(string? channel, string? status, Guid? branchId, Guid? resellerId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var visible = access.BranchesWith(P.Orders.View);
        var q = db.Set<Order>().AsNoTracking().Where(o => o.Status != OrderStatus.CheckoutAttempt);
        if (visible is not null)
        {
            q = q.Where(o => visible.Contains(o.FulfillmentBranchId));
        }
        if (!string.IsNullOrWhiteSpace(channel) && Enum.TryParse<OrderChannel>(channel, true, out var c))
        {
            q = q.Where(o => o.Channel == c);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, true, out var s))
        {
            q = q.Where(o => o.Status == s);
        }
        if (branchId is { } b)
        {
            q = q.Where(o => o.FulfillmentBranchId == b);
        }
        if (resellerId is { } r)
        {
            q = q.Where(o => o.ResellerId == r);
        }
        var ids = await q.OrderByDescending(o => o.CreatedAt).Take(300).Select(o => o.Id).ToListAsync(ct);
        return await ToDtosAsync(ids, includeCost: access.IsOwner, ct);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Set<Order>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFound();
        if (currentUser.AccountType == AccountType.Reseller)
        {
            return await GetMineAsync(id, ct);
        }
        await permissions.EnsurePermissionForBranchAsync(P.Orders.View, order.FulfillmentBranchId, ct);
        var dto = (await ToDtosAsync([id], includeCost: (await permissions.GetEffectiveAccessAsync(ct)).IsOwner, ct))[0];
        return order.Channel == OrderChannel.Store ? dto with { PosSale = await PosSaleDetailAsync(order, ct) } : dto;
    }

    private async Task<PosSaleDetailDto> PosSaleDetailAsync(Order order, CancellationToken ct)
    {
        var payments = await db.Set<PosPayment>().AsNoTracking().Where(p => p.OrderId == order.Id).OrderBy(p => p.RecordedAt).ToListAsync(ct);
        var overrides = await (from x in db.Set<PosPriceOverride>().AsNoTracking()
                               join l in db.Set<OrderLine>() on x.OrderLineId equals l.Id
                               where x.OrderId == order.Id
                               orderby l.SkuCode
                               select new { x, l.SkuCode }).ToListAsync(ct);
        var names = new Dictionary<Guid, string>();
        async Task<string> Name(Guid userId) => names.TryGetValue(userId, out var n) ? n : names[userId] = await approvals.DisplayNameAsync(userId, ct);
        var overrideDtos = new List<PosOverrideDto>();
        foreach (var o in overrides)
        {
            overrideDtos.Add(new PosOverrideDto(o.SkuCode, o.x.Quantity, o.x.OriginalUnitPrice, o.x.FinalUnitPrice, o.x.DiscountPct, o.x.Reason, o.x.ApprovalLevel,
                await Name(o.x.SellerUserId), o.x.ApproverUserId is { } a ? await Name(a) : null, o.x.OccurredAt));
        }
        var walkIn = order.Delivery.Name == "Walk-in customer";
        return new PosSaleDetailDto(await Name(order.PlacedBy), walkIn ? null : order.Delivery.Name,
            string.IsNullOrEmpty(order.Delivery.Mobile) ? null : MobileNumber.Mask(order.Delivery.Mobile),
            payments.Select(p => new PosPaymentDto(p.Method, p.Amount, p.Reference)).ToList(), overrideDtos);
    }

    /// <param name="external">Customer/reseller view: internal notes are not shown.</param>
    private async Task<IReadOnlyList<OrderDto>> ToDtosAsync(IReadOnlyList<Guid> ids, bool includeCost, CancellationToken ct, bool external = false)
    {
        var shipments = await db.Set<Shipment>().AsNoTracking().Where(x => ids.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, ct);
        var openExceptions = external ? [] : await ExceptionDtosAsync(
            await db.Set<FulfillmentException>().AsNoTracking().Where(e => ids.Contains(e.OrderId) && e.Status == FulfillmentExceptionStatus.Open).ToListAsync(ct), ct);
        var orders = await db.Set<Order>().AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var lines = await db.Set<OrderLine>().AsNoTracking().Where(l => ids.Contains(l.OrderId)).ToListAsync(ct);
        var history = await db.Set<OrderStatusChange>().AsNoTracking().Where(h => ids.Contains(h.OrderId)).OrderBy(h => h.OccurredAt).ToListAsync(ct);
        // FIFO cost of the order's CURRENT allocation: the latest sold allocation record (online, rerouted) or the order lines.
        var soldCosts = new List<(Guid OrderId, Guid ReservationId, DateTimeOffset CreatedAt, decimal? CostAmount)>();
        if (includeCost)
        {
            soldCosts = (await (from rl in db.Set<ReservationLine>()
                                join r in db.Set<Reservation>() on rl.ReservationId equals r.Id
                                where ids.Contains(r.OrderId) && r.Status == ReservationStatus.Consumed
                                select new { r.OrderId, ReservationId = r.Id, r.CreatedAt, rl.CostAmount }).AsNoTracking().ToListAsync(ct))
                .Select(x => (x.OrderId, x.ReservationId, x.CreatedAt, x.CostAmount)).ToList();
        }
        var names = (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        var result = new List<OrderDto>();
        foreach (var id in ids)
        {
            var o = orders[id];
            var own = lines.Where(l => l.OrderId == id).OrderBy(l => l.SkuCode).ToList();
            result.Add(new OrderDto(o.Id, o.Number, o.Channel.ToString(), o.Status.ToString(), o.ResellerId, o.FulfillmentBranchId, names.GetValueOrDefault(o.FulfillmentBranchId, "?"),
                ResellerCustomerService.ToDto(o.Delivery), o.MerchandiseTotal, o.ShippingFee, o.GrandTotal, o.CreatedAt, o.ConfirmedAt,
                own.Select(l => new OrderLineDto(l.Id, l.SkuId, l.SkuCode, l.ProductName, l.VariantName, l.Quantity, l.RetailUnitPrice, l.DiscountSource, l.DiscountPct,
                    l.DiscountAmountPerUnit, l.FinalUnitPrice, l.LineTotal, l.CommercialTermVersion)).ToList(),
                history.Where(h => h.OrderId == id).Select(h => new OrderStatusChangeDto(h.FromStatus?.ToString(), h.ToStatus.ToString(), external ? null : h.Note, h.OccurredAt)).ToList(),
                await whatsApp.OrderHelpUrlAsync(o.Number, ct),
                includeCost ? CostOf(o, own, soldCosts.Where(c => c.OrderId == id).ToList()) : null,
                shipments.TryGetValue(id, out var sh) ? new ShipmentDto(sh.Courier, CourierLabel(sh), sh.TrackingNumber, sh.ShippedAt, sh.DeliveredOn) : null,
                openExceptions.FirstOrDefault(e => e.OrderId == id)));
        }
        return result;
    }

    private static decimal? CostOf(Order o, List<OrderLine> lines, List<(Guid OrderId, Guid ReservationId, DateTimeOffset CreatedAt, decimal? CostAmount)> sold)
    {
        if (o.Status == OrderStatus.Cancelled)
        {
            return null;
        }
        if (sold.Count > 0)
        {
            var latest = sold.MaxBy(c => c.CreatedAt).ReservationId;
            return sold.Where(c => c.ReservationId == latest).Sum(c => c.CostAmount ?? 0m);
        }
        return lines.Any(l => l.CostAmount is not null) ? lines.Sum(l => l.CostAmount ?? 0m) : null;
    }

    internal static string CourierLabel(Shipment s) => s.Courier switch
    {
        "XPRESSBEES" => "Xpressbees",
        "DELHIVERY" => "Delhivery",
        _ => s.CourierName ?? "Other",
    };

    internal async Task<IReadOnlyList<FulfillmentExceptionDto>> ExceptionDtosAsync(IReadOnlyList<FulfillmentException> list, CancellationToken ct)
    {
        if (list.Count == 0)
        {
            return [];
        }
        var ids = list.Select(e => e.Id).ToList();
        var orderIds = list.Select(e => e.OrderId).Distinct().ToList();
        var lines = await db.Set<FulfillmentExceptionLine>().AsNoTracking().Where(l => ids.Contains(l.ExceptionId)).ToListAsync(ct);
        var orderInfo = await db.Set<Order>().AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var skuNames = await db.Set<OrderLine>().AsNoTracking().Where(l => orderIds.Contains(l.OrderId))
            .Select(l => new { l.SkuId, l.SkuCode, Name = l.ProductName + " · " + l.VariantName }).Distinct().ToListAsync(ct);
        var names = (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return list.Select(e => new FulfillmentExceptionDto(e.Id, e.OrderId, orderInfo[e.OrderId].Number, orderInfo[e.OrderId].Channel.ToString(), e.BranchId,
            names.GetValueOrDefault(e.BranchId, "?"), e.Reason, e.Notes, e.Status.ToString(), e.RaisedAt, e.ResolvedAt, e.Resolution,
            lines.Where(l => l.ExceptionId == e.Id).Select(l =>
            {
                var sku = skuNames.FirstOrDefault(x => x.SkuId == l.SkuId);
                return new StockIssueLineDto(l.SkuId, sku?.SkuCode ?? "?", sku?.Name ?? "?", l.MissingQty, l.DamagedQty);
            }).ToList())).ToList();
    }

    /// <summary>Branch work queue (SPEC §19.1 "sent to assigned branch"): paid/confirmed orders only, oldest first.</summary>
    public async Task<IReadOnlyList<OrderDto>> QueueAsync(Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var visible = access.BranchesWith(P.Orders.View);
        OrderStatus[] active = [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Packed, OrderStatus.Shipped, OrderStatus.FulfillmentException];
        var q = db.Set<Order>().AsNoTracking().Where(o => active.Contains(o.Status));
        if (visible is not null)
        {
            q = q.Where(o => visible.Contains(o.FulfillmentBranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(o => o.FulfillmentBranchId == b);
        }
        var ids = await q.OrderBy(o => o.ConfirmedAt).Take(500).Select(o => o.Id).ToListAsync(ct);
        return await ToDtosAsync(ids, includeCost: false, ct);
    }

    private async Task<ResellerInfo> SelfAsync(CancellationToken ct) =>
        await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");

    private static NotFoundException NotFound() => new("ORDER_NOT_FOUND", "Order not found.");
}
