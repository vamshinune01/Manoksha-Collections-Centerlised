using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Modules.Wallet.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Wallet.Application;

/// <summary>
/// The only code that changes a wallet. Every change locks the wallet row (serializing concurrent spends of the same balance),
/// writes an immutable ledger entry with balance before/after, and updates the cached balance — all in the caller's
/// transaction. Negative balances are impossible in code and in the database (SPEC §16, §33).
/// Must not depend on the Resellers module's services: Resellers consumes this class through its contracts (DI cycle).
/// </summary>
internal sealed class WalletService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock) : IWallets, IResellerOnboardingParticipant, IResellerBalanceView
{
    // ---- Onboarding (called by the Resellers module) ----

    public async Task OnResellerCreatedAsync(Guid resellerId, CancellationToken cancellationToken = default)
    {
        if (await db.Set<ResellerWallet>().AnyAsync(w => w.ResellerId == resellerId, cancellationToken))
        {
            throw new ConflictException("WALLET_EXISTS", "This reseller already has a wallet.");
        }
        db.Add(new ResellerWallet(resellerId, clock.UtcNow)); // ₹0 opening balance — never typed in (SPEC §6)
    }

    public Task<bool> IsReadyAsync(Guid resellerId, CancellationToken cancellationToken = default) =>
        db.Set<ResellerWallet>().AnyAsync(w => w.ResellerId == resellerId, cancellationToken);

    public async Task<decimal?> GetBalanceAsync(Guid resellerId, CancellationToken cancellationToken = default) =>
        await db.Set<ResellerWallet>().AsNoTracking().Where(w => w.ResellerId == resellerId).Select(w => (decimal?)w.Balance).SingleOrDefaultAsync(cancellationToken);

    // ---- IWallets ----

    public async Task<WalletInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default) =>
        await db.Set<ResellerWallet>().AsNoTracking().Where(w => w.ResellerId == resellerId)
            .Select(w => new WalletInfo(w.Id, w.ResellerId, w.Balance)).SingleOrDefaultAsync(cancellationToken);

    public async Task<LedgerPosting> DebitForOrderAsync(Guid resellerId, decimal amount, Guid orderId, string orderNumber, CancellationToken cancellationToken = default)
    {
        var entry = await PostAsync(resellerId, LedgerEntryType.Debit, LedgerDirection.Debit, amount, orderId, orderNumber, null, null, $"Order {orderNumber}", cancellationToken);
        return new LedgerPosting(entry.Id, entry.BalanceBefore, entry.BalanceAfter);
    }

    public async Task<LedgerPosting> ReverseOrderDebitAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var debit = await db.Set<WalletLedgerEntry>().AsNoTracking().SingleOrDefaultAsync(e => e.OrderId == orderId && e.Type == LedgerEntryType.Debit, cancellationToken)
            ?? throw new NotFoundException("WALLET_DEBIT_NOT_FOUND", "No wallet debit exists for this order.");
        // Lock the wallet first so two concurrent reversals serialize; then a debit can be reversed at most once
        // (also enforced by the unique index on reverses_entry_id).
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM wallet.wallets WHERE reseller_id = {debit.ResellerId} FOR UPDATE", cancellationToken);
        if (await db.Set<WalletLedgerEntry>().AnyAsync(e => e.ReversesEntryId == debit.Id, cancellationToken))
        {
            throw new ConflictException("WALLET_DEBIT_ALREADY_REVERSED", "This order's wallet debit has already been reversed.");
        }
        var entry = await PostAsync(debit.ResellerId, LedgerEntryType.Reversal, LedgerDirection.Credit, debit.Amount, orderId, debit.OrderNumber, null, debit.Id,
            reason, cancellationToken);
        await audit.RecordAsync(new AuditRecord("wallet.debit.reversed", "Wallet", debit.ResellerId.ToString(),
            new { debitEntryId = debit.Id, balance = entry.BalanceBefore }, new { reversalEntryId = entry.Id, amount = debit.Amount, balance = entry.BalanceAfter, orderId }, reason), cancellationToken);
        return new LedgerPosting(entry.Id, entry.BalanceBefore, entry.BalanceAfter);
    }

    // ---- Owner manual adjustment (SPEC §26: Owner only) ----

    public Task<LedgerEntryDto> AdjustAsync(Guid resellerId, ManualAdjustmentRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for a manual wallet adjustment.", 400);
        }
        if (!Enum.TryParse<LedgerDirection>(r.Direction, true, out var direction))
        {
            throw new BusinessRuleException("DIRECTION_INVALID", "Direction must be Credit or Debit.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var entry = await PostAsync(resellerId, LedgerEntryType.Adjustment, direction, r.Amount, null, null, null, null, r.Reason.Trim(), innerCt);
            await audit.RecordAsync(new AuditRecord("wallet.manual_adjustment", "Wallet", resellerId.ToString(),
                new { balance = entry.BalanceBefore }, new { entryId = entry.Id, direction = direction.ToString(), amount = r.Amount, balance = entry.BalanceAfter }, r.Reason), innerCt);
            return ToDto(entry);
        }, ct);
    }

    // ---- Queries ----

    public async Task<LedgerPage> LedgerAsync(Guid resellerId, long? beforeSeq, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var q = db.Set<WalletLedgerEntry>().AsNoTracking().Where(e => e.ResellerId == resellerId);
        if (beforeSeq is { } b)
        {
            q = q.Where(e => e.Seq < b);
        }
        var entries = await q.OrderByDescending(e => e.Seq).Take(take + 1).ToListAsync(ct);
        long? next = null;
        if (entries.Count > take)
        {
            entries.RemoveAt(entries.Count - 1);
            next = entries[^1].Seq;
        }
        return new LedgerPage(await GetBalanceAsync(resellerId, ct) ?? 0m, entries.Select(ToDto).ToList(), next);
    }

    /// <summary>Locks the wallet row, applies the change and appends the ledger entry.</summary>
    internal async Task<WalletLedgerEntry> PostAsync(Guid resellerId, LedgerEntryType type, LedgerDirection direction, decimal amount,
        Guid? orderId, string? orderNumber, Guid? depositId, Guid? reversesId, string? reason, CancellationToken ct, Guid? onlineDepositId = null)
    {
        var wallet = await db.Set<ResellerWallet>()
            .FromSqlInterpolated($"SELECT *, xmin FROM wallet.wallets WHERE reseller_id = {resellerId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("WALLET_NOT_FOUND", "This reseller has no wallet.");
        var now = clock.UtcNow;
        var (before, after) = wallet.Apply(direction, amount, now);
        var entry = new WalletLedgerEntry(wallet.Id, resellerId, type, direction, amount, before, after, orderId, orderNumber, depositId, reversesId, reason,
            currentUser.UserIdOrNull, now, onlineDepositId);
        db.Add(entry);
        await db.SaveChangesAsync(ct);
        return entry;
    }

    internal static LedgerEntryDto ToDto(WalletLedgerEntry e) =>
        new(e.Id, e.Seq, e.CreatedAt, e.Type.ToString(), e.Direction.ToString(), e.Amount, e.BalanceBefore, e.BalanceAfter, e.OrderId, e.OrderNumber,
            e.DepositRequestId, e.ReversesEntryId, e.Reason, e.CreatedBy);
}
