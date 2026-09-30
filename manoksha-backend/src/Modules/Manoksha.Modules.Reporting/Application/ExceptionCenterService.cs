using Manoksha.Application.Security;
using Manoksha.Modules.Reporting.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Reporting.Application;

/// <summary>
/// The Owner Exception Center (SPEC §28): one queue over the authoritative records each module keeps (design §19). Every item links
/// to the page where the domain action happens (reroute, reconcile, resolve discrepancy, approve deposit, retry email, resolve alert).
/// </summary>
internal sealed class ExceptionCenterService(ManokshaDbContext db, IPermissionService permissions)
{
    public static readonly IReadOnlyList<string> Types =
    [
        "PAYMENT_RECONCILIATION", "FULFILLMENT_EXCEPTION", "UNFULFILLED_CHECKOUT", "TRANSFER_DISCREPANCY", "INVENTORY_DISCREPANCY",
        "WALLET_DEPOSIT_PENDING", "FAILED_NOTIFICATION", "SENSITIVE_ALERT",
    ];

    public async Task<ExceptionCenterDto> ListAsync(string? type, CancellationToken ct)
    {
        if (type is not null && !Types.Contains(type))
        {
            throw new BusinessRuleException("EXCEPTION_TYPE_INVALID", $"Type must be one of {string.Join(", ", Types)}.", 400);
        }
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var visible = Visible(await OpenItemsAsync(db, ct), access);
        return new ExceptionCenterDto(Count(visible), visible.Where(i => type is null || i.Type == type).OrderBy(i => Rank(i.Severity)).ThenBy(i => i.CreatedAt).ToList());
    }

    internal static IReadOnlyList<ExceptionCount> Count(IEnumerable<ExceptionItemDto> items)
    {
        var counts = items.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count());
        return Types.Select(t => new ExceptionCount(t, counts.GetValueOrDefault(t))).ToList();
    }

    /// <summary>What each exception type needs to be seen; branch-bound items follow the viewer's branch scope.</summary>
    internal static List<ExceptionItemDto> Visible(IEnumerable<ExceptionItemDto> items, EffectiveAccess access) =>
        items.Where(i =>
        {
            var permission = i.Type switch
            {
                "PAYMENT_RECONCILIATION" => P.Exceptions.ReconciliationManage,
                "FULFILLMENT_EXCEPTION" => P.Orders.View,
                "TRANSFER_DISCREPANCY" or "INVENTORY_DISCREPANCY" => P.Inventory.View,
                "WALLET_DEPOSIT_PENDING" => P.Wallet.DepositApprove,
                "FAILED_NOTIFICATION" or "SENSITIVE_ALERT" => P.Exceptions.Manage,
                _ => P.Exceptions.View,
            };
            return i.BranchId is { } b ? access.HasForBranch(permission, b) : access.GlobalPermissions.Contains(permission);
        }).ToList();

    internal static async Task<List<ExceptionItemDto>> OpenItemsAsync(ManokshaDbContext db, CancellationToken ct) =>
        await WithBranchNames(db, await Sql.QueryAsync(db, """
            SELECT 'PAYMENT_RECONCILIATION', r.id, r.case_number, 'CRITICAL', NULL::uuid, r.created_at,
                   r.reason_code || ' · ' || r.reference_number || ' · expected ' || r.expected_amount || COALESCE(', paid ' || r.paid_amount, '') || ' · ' || r.status,
                   r.reference_id
              FROM payments.reconciliations r WHERE r.status IN ('Open', 'RefundInitiated')
            UNION ALL
            SELECT 'FULFILLMENT_EXCEPTION', e.id, o.number, 'CRITICAL', e.branch_id, e.raised_at, e.reason || COALESCE(' · ' || NULLIF(e.notes, ''), ''), o.id
              FROM orders.fulfillment_exceptions e JOIN orders.orders o ON o.id = e.order_id WHERE e.status = 'Open'
            UNION ALL
            SELECT 'UNFULFILLED_CHECKOUT', i.id, i.reference, 'WARNING', NULL::uuid, i.created_at,
                   i.channel || COALESCE(' · ' || i.contact_name, '') || COALESCE(' · ' || i.contact_mobile, '') || ' · ' || i.failure_reason, NULL::uuid
              FROM orders.fulfillment_inquiries i WHERE i.status = 'Open'
            UNION ALL
            SELECT CASE WHEN d.source_type = 'TRANSFER' THEN 'TRANSFER_DISCREPANCY' ELSE 'INVENTORY_DISCREPANCY' END, d.id, d.number, 'WARNING', d.branch_id, d.created_at,
                   d.source_type || ' ' || d.source_number || ' · ' || s.code || ' · expected ' || d.expected_qty || ', found ' || d.actual_qty, d.source_id
              FROM inventory.discrepancies d JOIN catalog.skus s ON s.id = d.sku_id WHERE d.status = 'Open'
            UNION ALL
            SELECT 'WALLET_DEPOSIT_PENDING', w.id, w.number, 'INFO', NULL::uuid, w.submitted_at,
                   rs.reseller_number || ' · ' || COALESCE(rs.business_name, rs.contact_name) || ' · ₹' || w.amount || ' ' || w.method, w.reseller_id
              FROM wallet.deposit_requests w JOIN resellers.resellers rs ON rs.id = w.reseller_id WHERE w.status = 'Pending'
            UNION ALL
            SELECT 'FAILED_NOTIFICATION', n.id, COALESCE(n.reference, n.event_type), 'WARNING', NULL::uuid, n.created_at,
                   n.subject || ' → ' || n.to_address || ' · ' || n.attempts || ' attempts · ' || COALESCE(n.last_error, ''), NULL::uuid
              FROM notifications.email_deliveries n WHERE n.status = 'Failed'
            UNION ALL
            SELECT 'SENSITIVE_ALERT', a.id, COALESCE(a.reference, a.kind), a.severity, NULL::uuid, a.created_at, a.title || ' · ' || a.detail, NULL::uuid
              FROM notifications.alerts a WHERE a.status = 'Open'
            """, new Dictionary<string, object?>(),
            r => new ExceptionItemDto(r.GetString(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.NullableGuid(4), null, r.Time(5), r.GetString(6), r.NullableGuid(7)),
            ct), ct);

    private static async Task<List<ExceptionItemDto>> WithBranchNames(ManokshaDbContext db, List<ExceptionItemDto> items, CancellationToken ct)
    {
        if (!items.Exists(i => i.BranchId is not null))
        {
            return items;
        }
        var names = (await Sql.QueryAsync(db, "SELECT id, name FROM branches.branches", new Dictionary<string, object?>(), r => (r.GetGuid(0), r.GetString(1)), ct))
            .ToDictionary(x => x.Item1, x => x.Item2);
        return items.Select(i => i.BranchId is { } b ? i with { BranchName = names.GetValueOrDefault(b) } : i).ToList();
    }

    private static int Rank(string severity) => severity switch
    {
        "CRITICAL" => 0,
        "WARNING" => 1,
        _ => 2,
    };
}
