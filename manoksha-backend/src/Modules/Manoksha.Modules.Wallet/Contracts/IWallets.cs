namespace Manoksha.Modules.Wallet.Contracts;

public sealed record WalletInfo(Guid WalletId, Guid ResellerId, decimal Balance);

public sealed record LedgerPosting(Guid EntryId, decimal BalanceBefore, decimal BalanceAfter);

/// <summary>Prepaid reseller wallets (SPEC §16). Balance can never go below zero; the ledger is append-only.</summary>
public interface IWallets
{
    Task<WalletInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits the wallet for a reseller order inside the caller's transaction (row-locked). Throws
    /// INSUFFICIENT_WALLET_BALANCE — the caller's whole transaction then rolls back (SPEC §16, §32).
    /// </summary>
    Task<LedgerPosting> DebitForOrderAsync(Guid resellerId, decimal amount, Guid orderId, string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>Separate authorized REVERSAL credit linked to the original debit — the debit is never changed (SPEC §16).</summary>
    Task<LedgerPosting> ReverseOrderDebitAsync(Guid orderId, string reason, CancellationToken cancellationToken = default);
}
