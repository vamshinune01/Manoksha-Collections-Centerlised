using Manoksha.Application.Security;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Reporting.Application;

/// <summary>The signed-in reseller's own figures only (SPEC §24, §31).</summary>
internal sealed class ResellerDashboardService(ManokshaDbContext db, IResellerDirectory resellers, ICurrentUser currentUser, IClock clock)
{
    public async Task<ResellerDashboardDto> GetAsync(CancellationToken ct)
    {
        var me = await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException("RESELLER_REQUIRED", "Sign in as a reseller.");
        var today = Sql.Today(clock.UtcNow);
        return (await Sql.QueryAsync(db, $"""
            SELECT
              COUNT(*) FILTER (WHERE o.confirmed_at >= @month)::int,
              COALESCE(SUM(o.grand_total) FILTER (WHERE o.confirmed_at >= @month), 0),
              COUNT(*) FILTER (WHERE o.status IN ('Confirmed', 'Processing', 'Packed', 'Shipped', 'FulfillmentException'))::int,
              COUNT(*) FILTER (WHERE o.status = 'Delivered' AND o.confirmed_at >= @month)::int,
              COALESCE((SELECT w.balance FROM wallet.wallets w WHERE w.reseller_id = @reseller), 0),
              COALESCE(SUM(o.grand_total), 0),
              COUNT(*)::int
              FROM orders.orders o
             WHERE o.reseller_id = @reseller AND {Sql.SaleFilter}
            """, new Dictionary<string, object?> { ["reseller"] = me.ResellerId, ["month"] = Sql.StartOfDay(new DateOnly(today.Year, today.Month, 1)) },
            r => new ResellerDashboardDto(r.GetInt32(0), r.GetDecimal(1), r.GetInt32(2), r.GetInt32(3), r.GetDecimal(4), r.GetDecimal(5), r.GetInt32(6)), ct))[0];
    }
}
