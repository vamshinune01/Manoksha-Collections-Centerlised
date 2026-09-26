using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
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
    ICurrentUser currentUser)
{
    // ---- Reseller: own orders only (SPEC §24). Another reseller's order id is simply "not found". ----

    public async Task<IReadOnlyList<OrderDto>> ListMineAsync(CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        var ids = await db.Set<Order>().AsNoTracking().Where(o => o.ResellerId == me.ResellerId && o.Status != OrderStatus.CheckoutAttempt)
            .OrderByDescending(o => o.CreatedAt).Take(200).Select(o => o.Id).ToListAsync(ct);
        return await ToDtosAsync(ids, includeCost: false, ct);
    }

    public async Task<OrderDto> GetMineAsync(Guid id, CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        if (!await db.Set<Order>().AnyAsync(o => o.Id == id && o.ResellerId == me.ResellerId, ct))
        {
            throw NotFound();
        }
        return (await ToDtosAsync([id], includeCost: false, ct))[0];
    }

    // ---- Customer: own ONLINE orders only (SPEC §24). Another customer's order id is simply "not found". ----

    public async Task<IReadOnlyList<OrderDto>> ListForCustomerAsync(CancellationToken ct)
    {
        var me = currentUser.UserId;
        var ids = await db.Set<Order>().AsNoTracking().Where(o => o.CustomerUserId == me && o.Status != OrderStatus.CheckoutAttempt)
            .OrderByDescending(o => o.CreatedAt).Take(200).Select(o => o.Id).ToListAsync(ct);
        return await ToDtosAsync(ids, includeCost: false, ct);
    }

    public async Task<OrderDto> GetForCustomerAsync(Guid id, CancellationToken ct)
    {
        var me = currentUser.UserId;
        if (!await db.Set<Order>().AnyAsync(o => o.Id == id && o.CustomerUserId == me, ct))
        {
            throw NotFound();
        }
        return (await ToDtosAsync([id], includeCost: false, ct))[0];
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
        return (await ToDtosAsync([id], includeCost: (await permissions.GetEffectiveAccessAsync(ct)).IsOwner, ct))[0];
    }

    private async Task<IReadOnlyList<OrderDto>> ToDtosAsync(IReadOnlyList<Guid> ids, bool includeCost, CancellationToken ct)
    {
        var orders = await db.Set<Order>().AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var lines = await db.Set<OrderLine>().AsNoTracking().Where(l => ids.Contains(l.OrderId)).ToListAsync(ct);
        var history = await db.Set<OrderStatusChange>().AsNoTracking().Where(h => ids.Contains(h.OrderId)).OrderBy(h => h.OccurredAt).ToListAsync(ct);
        // ONLINE orders record FIFO cost on the reservation line that was sold.
        var soldCosts = new List<(Guid OrderId, decimal? CostAmount)>();
        if (includeCost)
        {
            soldCosts = (await (from rl in db.Set<ReservationLine>()
                                join r in db.Set<Reservation>() on rl.ReservationId equals r.Id
                                where ids.Contains(r.OrderId) && r.Status == ReservationStatus.Consumed
                                select new { r.OrderId, rl.CostAmount }).AsNoTracking().ToListAsync(ct))
                .Select(x => (x.OrderId, x.CostAmount)).ToList();
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
                history.Where(h => h.OrderId == id).Select(h => new OrderStatusChangeDto(h.FromStatus?.ToString(), h.ToStatus.ToString(), h.Note, h.OccurredAt)).ToList(),
                await whatsApp.OrderHelpUrlAsync(o.Number, ct),
                includeCost ? own.Sum(l => l.CostAmount ?? 0m) + soldCosts.Where(c => c.OrderId == id).Sum(c => c.CostAmount ?? 0m) : null));
        }
        return result;
    }

    private async Task<ResellerInfo> SelfAsync(CancellationToken ct) =>
        await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");

    private static NotFoundException NotFound() => new("ORDER_NOT_FOUND", "Order not found.");
}
