using Manoksha.Application.Abstractions;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Manoksha.Modules.Wallet.Application;

public sealed record WalletIntegrityIssue(Guid WalletId, Guid ResellerId, string Problem, decimal CachedBalance, decimal LedgerBalance, long? AtSeq);

public sealed record WalletIntegrityReport(DateTimeOffset CheckedAt, int WalletsChecked, IReadOnlyList<WalletIntegrityIssue> Issues)
{
    public bool Healthy => Issues.Count == 0;
}

public sealed record WalletIntegrityMismatch(int IssueCount) : IIntegrationEvent
{
    public static string EventType => "wallet.integrity_mismatch";
}

/// <summary>
/// Verifies the ledger is the truth (design §15): for every wallet, (1) the cached balance equals credits − debits of its ledger,
/// (2) the latest entry's balance_after equals the cached balance, and (3) the entries chain — each balance_before equals the
/// previous entry's balance_after. Any issue is audited and raised as a CRITICAL event; nothing is auto-corrected.
/// </summary>
internal sealed class WalletIntegrityService(ManokshaDbContext db, IAuditWriter audit, IOutbox outbox, IClock clock, ILogger<WalletIntegrityService> logger)
{
    // Column aliases are snake_case: the model's naming convention also applies to raw-SQL result mapping.
    private sealed record SumRow(Guid WalletId, Guid ResellerId, decimal Balance, decimal LedgerBalance, decimal? LastBalanceAfter);

    private sealed record ChainRow(Guid WalletId, Guid ResellerId, long Seq, decimal BalanceBefore, decimal? PreviousAfter);

    public async Task<WalletIntegrityReport> CheckAsync(bool recordFindings, CancellationToken ct)
    {
        var sums = await db.Database.SqlQuery<SumRow>($"""
            SELECT w.id AS wallet_id, w.reseller_id AS reseller_id, w.balance AS balance,
                   COALESCE(SUM(CASE WHEN e.direction = 'Credit' THEN e.amount ELSE -e.amount END), 0) AS ledger_balance,
                   (SELECT l.balance_after FROM wallet.ledger_entries l WHERE l.wallet_id = w.id ORDER BY l.seq DESC LIMIT 1) AS last_balance_after
            FROM wallet.wallets w
            LEFT JOIN wallet.ledger_entries e ON e.wallet_id = w.id
            GROUP BY w.id, w.reseller_id, w.balance
            """).ToListAsync(ct);

        var breaks = await db.Database.SqlQuery<ChainRow>($"""
            SELECT wallet_id, reseller_id, seq, balance_before, previous_after
            FROM (
                SELECT wallet_id, reseller_id, seq, balance_before,
                       LAG(balance_after) OVER (PARTITION BY wallet_id ORDER BY seq) AS previous_after
                FROM wallet.ledger_entries
            ) t
            WHERE (previous_after IS NULL AND balance_before <> 0) OR (previous_after IS NOT NULL AND balance_before <> previous_after)
            """).ToListAsync(ct);

        var issues = new List<WalletIntegrityIssue>();
        foreach (var s in sums)
        {
            if (s.Balance != s.LedgerBalance)
            {
                issues.Add(new WalletIntegrityIssue(s.WalletId, s.ResellerId, "CACHED_BALANCE_DIFFERS_FROM_LEDGER", s.Balance, s.LedgerBalance, null));
            }
            else if (s.LastBalanceAfter is { } last && last != s.Balance)
            {
                issues.Add(new WalletIntegrityIssue(s.WalletId, s.ResellerId, "LAST_ENTRY_BALANCE_DIFFERS", s.Balance, last, null));
            }
        }
        foreach (var b in breaks)
        {
            var sum = sums.First(s => s.WalletId == b.WalletId);
            issues.Add(new WalletIntegrityIssue(b.WalletId, b.ResellerId, "LEDGER_CHAIN_BROKEN", sum.Balance, sum.LedgerBalance, b.Seq));
        }

        var report = new WalletIntegrityReport(clock.UtcNow, sums.Count, issues);
        if (recordFindings && !report.Healthy)
        {
            logger.LogCritical("Wallet integrity check found {Count} issue(s)", issues.Count);
            await audit.RecordAsync(new AuditRecord("wallet.integrity.mismatch", "WalletIntegrity", report.CheckedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                After: new { issues }), ct);
            outbox.Enqueue(new WalletIntegrityMismatch(issues.Count));
            await db.SaveChangesAsync(ct);
        }
        return report;
    }
}
