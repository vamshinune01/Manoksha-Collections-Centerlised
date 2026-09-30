using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Reporting.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Reporting.Application;

/// <summary>
/// Dashboards and reports (SPEC §31). Revenue is the merchandise value of confirmed, not-cancelled orders on their confirmation
/// date (IST); shipping is reported separately. Gross profit uses FIFO cost and is labelled gross, never net. Cost, gross profit,
/// margin and stock value are Owner-only (<c>reports.global</c>; ADR-001 §40).
/// </summary>
internal sealed class ReportingService(ManokshaDbContext db, IPermissionService permissions, ISettingsReader settings, IClock clock) : IReportingQueries
{
    private const int MaxRangeDays = 366;

    // ---- Shared queries (also used by Notifications) ----

    public async Task<IReadOnlyList<LowStockRow>> LowStockAsync(IReadOnlyCollection<Guid>? branchIds, CancellationToken cancellationToken = default)
    {
        var threshold = await settings.GetAsync<int>(SettingKeys.LowStockThreshold, cancellationToken);
        return await Sql.QueryAsync(db, $"""
            WITH per AS (
              SELECT sku_id, branch_id, SUM(available)::int AS available FROM (
                SELECT sku_id, branch_id, SUM(CASE WHEN status = 'Available' THEN quantity ELSE 0 END) AS available
                  FROM inventory.stock_levels GROUP BY sku_id, branch_id
                UNION ALL
                SELECT sku_id, branch_id, COUNT(*) FILTER (WHERE status = 'Available') FROM inventory.inventory_items GROUP BY sku_id, branch_id
              ) x GROUP BY sku_id, branch_id)
            SELECT per.branch_id, b.name, per.sku_id, s.code, p.name, v.name, per.available
              FROM per
              JOIN catalog.skus s ON s.id = per.sku_id
              JOIN catalog.products p ON p.id = s.product_id
              JOIN catalog.variants v ON v.id = s.variant_id
              JOIN branches.branches b ON b.id = per.branch_id
             WHERE per.available <= @threshold AND p.status = 'Active' AND b.is_active
               AND (@branches::uuid[] IS NULL OR per.branch_id = ANY(@branches))
             ORDER BY per.available, b.name, s.code
            """,
            new Dictionary<string, object?> { ["threshold"] = threshold, ["branches"] = branchIds?.ToArray() },
            r => new LowStockRow(r.GetGuid(0), r.GetString(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6)), cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelSales>> SalesByChannelAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? branchIds,
        CancellationToken cancellationToken = default) =>
        (await SalesGroupedAsync("o.channel", from, to, branchIds, cancellationToken))
            .Select(g => new ChannelSales(g.Key, g.Orders, g.Units, g.Revenue, g.Shipping, g.Cost)).OrderBy(c => c.Channel).ToList();

    public async Task<IReadOnlyList<ExceptionCount>> OpenExceptionCountsAsync(CancellationToken cancellationToken = default) =>
        ExceptionCenterService.Count(await ExceptionCenterService.OpenItemsAsync(db, cancellationToken));

    // ---- Dashboard ----

    public async Task<DashboardDto> DashboardAsync(Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var scope = Scope(access, P.Reports.View, branchId);
        var costVisible = access.Has(P.Reports.Global);
        var global = scope is null;
        var today = Sql.Today(clock.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var branches = await VisibleBranchesAsync(scope, ct);

        var todaySales = Block(await SalesByChannelAsync(today, today, scope, ct), costVisible);
        var monthSales = Block(await SalesByChannelAsync(monthStart, today, scope, ct), costVisible);
        var daily = (await SalesGroupedAsync("(o.confirmed_at AT TIME ZONE 'Asia/Kolkata')::date::text", today.AddDays(-13), today, scope, ct))
            .ToDictionary(g => g.Key);
        var last14 = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).Select(d =>
        {
            var g = daily.GetValueOrDefault(d.ToString("yyyy-MM-dd"));
            decimal? profit = !costVisible ? null : g is null ? 0m : g.Cost is { } c ? g.Revenue - c : null;
            return new DailySalesPointDto(d, g?.Orders ?? 0, g?.Revenue ?? 0m, profit);
        }).ToList();

        var byBranchToday = (await SalesGroupedAsync("o.fulfillment_branch_id::text", today, today, scope, ct)).ToDictionary(g => g.Key);
        var byBranchMonth = (await SalesGroupedAsync("o.fulfillment_branch_id::text", monthStart, today, scope, ct)).ToDictionary(g => g.Key);
        var branchSales = branches.Select(b =>
        {
            var t = byBranchToday.GetValueOrDefault(b.Id.ToString());
            var m = byBranchMonth.GetValueOrDefault(b.Id.ToString());
            return new BranchSalesDto(b.Id, b.Name, t?.Orders ?? 0, t?.Revenue ?? 0m, m?.Orders ?? 0, m?.Revenue ?? 0m,
                costVisible ? (m is null ? 0m : m.Cost is { } c ? m.Revenue - c : null) : null);
        }).ToList();

        var lowStock = await LowStockAsync(scope, ct);
        var openExceptions = access.Has(P.Exceptions.View)
            ? ExceptionCenterService.Count(ExceptionCenterService.Visible(await ExceptionCenterService.OpenItemsAsync(db, ct), access))
            : null;

        return new DashboardDto(today, branches, costVisible, todaySales, monthSales, last14, branchSales,
            await InventoryAsync(scope, costVisible, ct),
            await settings.GetAsync<int>(SettingKeys.LowStockThreshold, ct), lowStock.Count, lowStock.Take(15).ToList(),
            await OperationsAsync(scope, global && access.Has(P.Wallet.DepositApprove), ct),
            await AttendanceAsync(scope, today, ct),
            global && access.Has(P.Pricing.View) ? await PriceChangesAsync(ct) : null,
            global && access.Has(P.Resellers.View) ? await ResellersBlockAsync(monthStart, today, ct) : null,
            openExceptions);
    }

    // ---- Reports ----

    public async Task<SalesReportDto> SalesReportAsync(DateOnly? from, DateOnly? to, Guid? branchId, string? groupBy, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var scope = Scope(access, P.Reports.View, branchId);
        var (f, t) = Range(from, to);
        var costVisible = access.Has(P.Reports.Global);
        var by = (groupBy ?? "day").ToLowerInvariant();
        var expr = by switch
        {
            "day" => "(o.confirmed_at AT TIME ZONE 'Asia/Kolkata')::date::text",
            "branch" => "o.fulfillment_branch_id::text",
            "channel" => "o.channel",
            "reseller" when scope is null => "COALESCE(o.reseller_id::text, '')",
            "reseller" => throw new ForbiddenException("REPORT_FORBIDDEN", "Only the Owner can see sales by reseller."),
            _ => throw new BusinessRuleException("GROUP_BY_INVALID", "Group by day, branch, channel or reseller.", 400),
        };
        var groups = await SalesGroupedAsync(expr, f, t, scope, ct, by == "reseller" ? "o.channel = 'Reseller'" : null);
        var labels = by switch
        {
            "branch" => (await VisibleBranchesAsync(null, ct)).ToDictionary(b => b.Id.ToString(), b => b.Name),
            "reseller" => (await Sql.QueryAsync(db, "SELECT id::text, reseller_number || ' · ' || COALESCE(business_name, contact_name) FROM resellers.resellers",
                new Dictionary<string, object?>(), r => (r.GetString(0), r.GetString(1)), ct)).ToDictionary(x => x.Item1, x => x.Item2),
            _ => new Dictionary<string, string>(),
        };
        var rows = groups.Select(g => Row(g.Key, labels.GetValueOrDefault(g.Key, g.Key), g, costVisible))
            .OrderBy(r => by == "day" ? r.Key : null).ThenByDescending(r => by == "day" ? 0 : r.Revenue).ToList();
        var total = new Grouped("TOTAL", groups.Sum(g => g.Orders), groups.Sum(g => g.Units), groups.Sum(g => g.Revenue), groups.Sum(g => g.Shipping),
            groups.All(g => g.Cost is not null) ? groups.Sum(g => g.Cost!.Value) : null);
        return new SalesReportDto(f, t, by, costVisible, rows, Row("TOTAL", "Total", total, costVisible));
    }

    public async Task<ProductSalesReportDto> ProductSalesAsync(DateOnly? from, DateOnly? to, Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var scope = Scope(access, P.Reports.View, branchId);
        var (f, t) = Range(from, to);
        var costVisible = access.Has(P.Reports.Global);
        var rows = await Sql.QueryAsync(db, $"""
            SELECT ol.sku_id, ol.sku_code, ol.product_name, ol.variant_name, SUM(ol.quantity)::int, SUM(ol.line_total),
                   SUM(COALESCE(
                     (SELECT SUM(rl.cost_amount) FROM orders.reservation_lines rl
                       WHERE rl.order_line_id = ol.id
                         AND rl.reservation_id = (SELECT r.id FROM orders.reservations r WHERE r.order_id = o.id AND r.status = 'Consumed' ORDER BY r.created_at DESC LIMIT 1)),
                     ol.cost_amount)),
                   BOOL_AND(ol.cost_amount IS NOT NULL OR EXISTS (SELECT 1 FROM orders.reservations r WHERE r.order_id = o.id AND r.status = 'Consumed'))
              FROM orders.orders o JOIN orders.order_lines ol ON ol.order_id = o.id
             WHERE {Sql.SaleFilter} AND o.confirmed_at >= @from AND o.confirmed_at < @to
               AND (@branches::uuid[] IS NULL OR o.fulfillment_branch_id = ANY(@branches))
             GROUP BY ol.sku_id, ol.sku_code, ol.product_name, ol.variant_name
             ORDER BY SUM(ol.line_total) DESC
             LIMIT 500
            """, Args(f, t, scope),
            r =>
            {
                var revenue = r.GetDecimal(5);
                decimal? cost = costVisible && r.GetBoolean(7) ? r.NullableDecimal(6) : null;
                return new ProductSalesRowDto(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), revenue, cost,
                    cost is { } c ? revenue - c : null, cost is { } c2 && revenue > 0 ? Math.Round((revenue - c2) / revenue * 100m, 2) : null);
            }, ct);
        return new ProductSalesReportDto(f, t, costVisible, rows);
    }

    public async Task<InventoryValuationDto> InventoryValuationAsync(Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        if (!access.Has(P.Reports.Global))
        {
            throw new ForbiddenException("REPORT_FORBIDDEN", "Only the Owner can see stock value.");
        }
        var rows = await Sql.QueryAsync(db, """
            WITH layers AS (
              SELECT branch_id, sku_id, SUM(remaining_qty)::int AS units, SUM(remaining_qty * unit_cost) AS value
                FROM inventory.cost_layers WHERE remaining_qty > 0 GROUP BY branch_id, sku_id),
            stock AS (
              SELECT branch_id, sku_id,
                     SUM(CASE WHEN status = 'Available' THEN q ELSE 0 END)::int AS available,
                     SUM(CASE WHEN status NOT IN ('InTransit', 'Lost', 'Sold', 'Delivered') THEN q ELSE 0 END)::int AS on_hand
                FROM (SELECT branch_id, sku_id, status, quantity AS q FROM inventory.stock_levels
                      UNION ALL SELECT branch_id, sku_id, status, 1 FROM inventory.inventory_items WHERE written_off_at IS NULL) x
               GROUP BY branch_id, sku_id)
            SELECT l.branch_id, b.name, l.sku_id, s.code, p.name, v.name, COALESCE(st.available, 0), COALESCE(st.on_hand, 0), l.units, l.value
              FROM layers l
              JOIN branches.branches b ON b.id = l.branch_id
              JOIN catalog.skus s ON s.id = l.sku_id
              JOIN catalog.products p ON p.id = s.product_id
              JOIN catalog.variants v ON v.id = s.variant_id
              LEFT JOIN stock st ON st.branch_id = l.branch_id AND st.sku_id = l.sku_id
             WHERE (@branch::uuid IS NULL OR l.branch_id = @branch)
             ORDER BY b.name, l.value DESC
            """, new Dictionary<string, object?> { ["branch"] = branchId },
            r => new ValuationRowDto(r.GetGuid(0), r.GetString(1), r.GetGuid(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), r.GetInt32(7),
                r.GetInt32(8), Money.Round(r.GetDecimal(9))), ct);
        var totals = rows.GroupBy(r => (r.BranchId, r.BranchName))
            .Select(g => new ValuationBranchTotalDto(g.Key.BranchId, g.Key.BranchName, g.Sum(x => x.CostedUnits), g.Sum(x => x.Value))).ToList();
        return new InventoryValuationDto(clock.UtcNow, totals, totals.Sum(x => x.Value), rows);
    }

    public async Task<LowStockReportDto> LowStockReportAsync(Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var scope = Scope(access, P.Reports.View, branchId);
        return new LowStockReportDto(await settings.GetAsync<int>(SettingKeys.LowStockThreshold, ct), await LowStockAsync(scope, ct));
    }

    public async Task<ResellerReportDto> ResellerReportAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        if (!access.Has(P.Reports.Global))
        {
            throw new ForbiddenException("REPORT_FORBIDDEN", "Only the Owner can see reseller reports.");
        }
        var (f, t) = Range(from, to);
        var rows = await Sql.QueryAsync(db, $"""
            SELECT rs.id, rs.reseller_number, COALESCE(rs.business_name, rs.contact_name), rs.status,
                   COALESCE((SELECT ct.discount_pct FROM resellers.commercial_terms ct WHERE ct.reseller_id = rs.id ORDER BY ct.version DESC LIMIT 1), 0),
                   COUNT(o.id)::int, COALESCE(SUM(o.merchandise_total), 0),
                   COALESCE((SELECT w.balance FROM wallet.wallets w WHERE w.reseller_id = rs.id), 0),
                   (SELECT MAX(o2.confirmed_at) FROM orders.orders o2 WHERE o2.reseller_id = rs.id AND o2.confirmed_at IS NOT NULL)
              FROM resellers.resellers rs
              LEFT JOIN orders.orders o ON o.reseller_id = rs.id AND {Sql.SaleFilter} AND o.confirmed_at >= @from AND o.confirmed_at < @to
             GROUP BY rs.id
             ORDER BY COALESCE(SUM(o.merchandise_total), 0) DESC, rs.reseller_number
            """, Args(f, t, null),
            r => new ResellerReportRowDto(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), r.GetInt32(5), r.GetDecimal(6), r.GetDecimal(7),
                r.NullableTime(8)), ct);
        return new ResellerReportDto(f, t, rows);
    }

    // ---- Helpers ----

    private sealed record Grouped(string Key, int Orders, int Units, decimal Revenue, decimal Shipping, decimal? Cost);

    private async Task<List<Grouped>> SalesGroupedAsync(string keyExpr, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? branchIds, CancellationToken ct,
        string? extraFilter = null) =>
        await Sql.QueryAsync(db, $"""
            SELECT {keyExpr} AS k, COUNT(*)::int,
                   COALESCE(SUM((SELECT SUM(ol.quantity) FROM orders.order_lines ol WHERE ol.order_id = o.id)), 0)::int,
                   COALESCE(SUM(o.merchandise_total), 0), COALESCE(SUM(o.shipping_fee), 0),
                   SUM({Sql.OrderCost}), BOOL_AND({Sql.OrderCostComplete})
              FROM orders.orders o
             WHERE {Sql.SaleFilter} AND o.confirmed_at >= @from AND o.confirmed_at < @to
               AND (@branches::uuid[] IS NULL OR o.fulfillment_branch_id = ANY(@branches))
               {(extraFilter is null ? string.Empty : "AND " + extraFilter)}
             GROUP BY k
            """, Args(from, to, branchIds),
            r => new Grouped(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetDecimal(3), r.GetDecimal(4), r.GetBoolean(6) ? r.NullableDecimal(5) ?? 0m : null), ct);

    private static Dictionary<string, object?> Args(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? branchIds) => new()
    {
        ["from"] = Sql.StartOfDay(from),
        ["to"] = Sql.StartOfDay(to.AddDays(1)),
        ["branches"] = branchIds?.ToArray(),
    };

    private static SalesBlockDto Block(IReadOnlyList<ChannelSales> channels, bool costVisible)
    {
        var revenue = channels.Sum(c => c.Revenue);
        decimal? cost = costVisible && channels.All(c => c.Cost is not null) ? channels.Sum(c => c.Cost!.Value) : null;
        var shown = costVisible ? channels : channels.Select(c => c with { Cost = null }).ToList();
        return new SalesBlockDto(channels.Sum(c => c.Orders), channels.Sum(c => c.Units), revenue, channels.Sum(c => c.ShippingFees), cost,
            cost is { } c ? revenue - c : null, cost is { } c2 && revenue > 0 ? Math.Round((revenue - c2) / revenue * 100m, 2) : null, shown);
    }

    private static SalesReportRowDto Row(string key, string label, Grouped g, bool costVisible)
    {
        decimal? cost = costVisible ? g.Cost : null;
        return new SalesReportRowDto(key, label, g.Orders, g.Units, g.Revenue, g.Shipping, cost, cost is { } c ? g.Revenue - c : null,
            cost is { } c2 && g.Revenue > 0 ? Math.Round((g.Revenue - c2) / g.Revenue * 100m, 2) : null);
    }

    private (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var t = to ?? from ?? Sql.Today(clock.UtcNow);
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        if (f > t)
        {
            throw new BusinessRuleException("DATE_RANGE_INVALID", "The start date must be on or before the end date.", 400);
        }
        if (t.DayNumber - f.DayNumber >= MaxRangeDays)
        {
            throw new BusinessRuleException("DATE_RANGE_TOO_LONG", "Choose a range of at most one year.", 400);
        }
        return (f, t);
    }

    /// <summary>Branches the viewer may report on (null = all), narrowed to one branch when asked.</summary>
    internal static IReadOnlyCollection<Guid>? Scope(EffectiveAccess access, string permission, Guid? branchId)
    {
        var allowed = access.BranchesWith(permission);
        if (allowed is not null && allowed.Count == 0)
        {
            throw new ForbiddenException("REPORT_FORBIDDEN", "You do not have access to reports.");
        }
        if (branchId is { } b)
        {
            if (allowed is not null && !allowed.Contains(b))
            {
                throw new ForbiddenException("BRANCH_FORBIDDEN", "You do not have access to this branch.");
            }
            return [b];
        }
        return allowed;
    }

    private async Task<List<BranchRefDto>> VisibleBranchesAsync(IReadOnlyCollection<Guid>? scope, CancellationToken ct) =>
        await Sql.QueryAsync(db, "SELECT id, name FROM branches.branches WHERE is_active AND (@branches::uuid[] IS NULL OR id = ANY(@branches)) ORDER BY name",
            new Dictionary<string, object?> { ["branches"] = scope?.ToArray() }, r => new BranchRefDto(r.GetGuid(0), r.GetString(1)), ct);

    private async Task<InventoryBlockDto> InventoryAsync(IReadOnlyCollection<Guid>? scope, bool costVisible, CancellationToken ct)
    {
        var counts = (await Sql.QueryAsync(db, """
            SELECT status, SUM(q)::int FROM (
              SELECT branch_id, status, quantity AS q FROM inventory.stock_levels
              UNION ALL SELECT branch_id, status, 1 FROM inventory.inventory_items WHERE written_off_at IS NULL) x
             WHERE (@branches::uuid[] IS NULL OR branch_id = ANY(@branches))
             GROUP BY status
            """, new Dictionary<string, object?> { ["branches"] = scope?.ToArray() }, r => (r.GetString(0), r.GetInt32(1)), ct)).ToDictionary(x => x.Item1, x => x.Item2);
        decimal? value = null;
        if (costVisible)
        {
            value = Money.Round((await Sql.QueryAsync(db,
                "SELECT COALESCE(SUM(remaining_qty * unit_cost), 0) FROM inventory.cost_layers WHERE remaining_qty > 0 AND (@branches::uuid[] IS NULL OR branch_id = ANY(@branches))",
                new Dictionary<string, object?> { ["branches"] = scope?.ToArray() }, r => r.GetDecimal(0), ct))[0]);
        }
        var onHand = counts.Where(kv => kv.Key is not ("InTransit" or "Lost" or "Sold" or "Delivered")).Sum(kv => kv.Value);
        return new InventoryBlockDto(counts.GetValueOrDefault("Available"), counts.GetValueOrDefault("Reserved"), counts.GetValueOrDefault("InTransit"), onHand, value);
    }

    private async Task<OperationsBlockDto> OperationsAsync(IReadOnlyCollection<Guid>? scope, bool deposits, CancellationToken ct)
    {
        var r = (await Sql.QueryAsync(db, """
            SELECT
              (SELECT COUNT(*)::int FROM inventory.transfers t WHERE t.status = 'Requested'
                 AND (@branches::uuid[] IS NULL OR t.source_branch_id = ANY(@branches))),
              (SELECT COUNT(*)::int FROM inventory.transfers t WHERE t.status IN ('Approved', 'Prepared', 'InTransit')
                 AND (@branches::uuid[] IS NULL OR t.source_branch_id = ANY(@branches) OR t.destination_branch_id = ANY(@branches))),
              (SELECT COUNT(*)::int FROM inventory.adjustments a WHERE a.status = 'Pending' AND (@branches::uuid[] IS NULL OR a.branch_id = ANY(@branches))),
              (SELECT COUNT(*)::int FROM inventory.discrepancies d WHERE d.status = 'Open' AND (@branches::uuid[] IS NULL OR d.branch_id = ANY(@branches))),
              (SELECT COUNT(*)::int FROM wallet.deposit_requests w WHERE w.status = 'Pending')
            """, new Dictionary<string, object?> { ["branches"] = scope?.ToArray() },
            x => new OperationsBlockDto(x.GetInt32(0), x.GetInt32(1), x.GetInt32(2), x.GetInt32(3), x.GetInt32(4)), ct))[0];
        return deposits ? r : r with { DepositsPending = null };
    }

    private async Task<AttendanceBlockDto> AttendanceAsync(IReadOnlyCollection<Guid>? scope, DateOnly today, CancellationToken ct) =>
        (await Sql.QueryAsync(db, """
            SELECT
              (SELECT COUNT(DISTINCT a.employee_id)::int FROM employees.attendance_records a
                WHERE a.clock_out_at IS NULL AND (@branches::uuid[] IS NULL OR a.branch_id = ANY(@branches))),
              (SELECT COUNT(DISTINCT a.employee_id)::int FROM employees.attendance_records a
                WHERE a.clock_in_at >= @start AND (@branches::uuid[] IS NULL OR a.branch_id = ANY(@branches))),
              (SELECT COUNT(*)::int FROM employees.employees e
                WHERE e.status = 'Active' AND (@branches::uuid[] IS NULL OR e.assigned_branch_id = ANY(@branches)))
            """, new Dictionary<string, object?> { ["branches"] = scope?.ToArray(), ["start"] = Sql.StartOfDay(today) },
            r => new AttendanceBlockDto(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)), ct))[0];

    private async Task<List<PriceChangeDto>> PriceChangesAsync(CancellationToken ct) =>
        await Sql.QueryAsync(db, """
            SELECT rp.effective_from, s.code, p.name,
                   (SELECT prev.price FROM pricing.retail_prices prev WHERE prev.sku_id = rp.sku_id AND prev.effective_from < rp.effective_from
                     ORDER BY prev.effective_from DESC LIMIT 1),
                   rp.price, rp.reason
              FROM pricing.retail_prices rp
              JOIN catalog.skus s ON s.id = rp.sku_id
              JOIN catalog.products p ON p.id = s.product_id
             WHERE rp.effective_from >= @since
             ORDER BY rp.effective_from DESC
             LIMIT 10
            """, new Dictionary<string, object?> { ["since"] = clock.UtcNow.AddDays(-7) },
            r => new PriceChangeDto(r.Time(0), r.GetString(1), r.GetString(2), r.NullableDecimal(3), r.GetDecimal(4), r.GetString(5)), ct);

    private async Task<ResellerBlockDto> ResellersBlockAsync(DateOnly monthStart, DateOnly today, CancellationToken ct)
    {
        var head = (await Sql.QueryAsync(db, """
            SELECT COUNT(*) FILTER (WHERE status = 'Active')::int, COUNT(*) FILTER (WHERE status = 'Frozen')::int,
                   (SELECT COALESCE(SUM(balance), 0) FROM wallet.wallets)
              FROM resellers.resellers
            """, new Dictionary<string, object?>(), r => (r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2)), ct))[0];
        var top = await Sql.QueryAsync(db, $"""
            SELECT rs.id, rs.reseller_number, COALESCE(rs.business_name, rs.contact_name), COUNT(*)::int, SUM(o.merchandise_total)
              FROM orders.orders o JOIN resellers.resellers rs ON rs.id = o.reseller_id
             WHERE {Sql.SaleFilter} AND o.confirmed_at >= @from AND o.confirmed_at < @to
             GROUP BY rs.id ORDER BY SUM(o.merchandise_total) DESC LIMIT 5
            """, Args(monthStart, today, null),
            r => new TopResellerDto(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetDecimal(4)), ct);
        return new ResellerBlockDto(head.Item1, head.Item2, head.Item3, top);
    }
}
