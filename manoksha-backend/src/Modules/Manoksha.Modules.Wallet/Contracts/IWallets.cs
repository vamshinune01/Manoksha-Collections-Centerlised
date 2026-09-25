namespace Manoksha.Modules.Wallet.Contracts;

public sealed record WalletInfo(Guid WalletId, Guid ResellerId, decimal Balance);

/// <summary>Prepaid reseller wallets (SPEC §16). Balance can never go below zero.</summary>
public interface IWallets
{
    /// <summary>Opens the reseller's wallet with a ₹0 balance (SPEC §6: no arbitrary opening balance).</summary>
    Task<WalletInfo> OpenAsync(Guid resellerId, CancellationToken cancellationToken = default);

    Task<WalletInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default);
}
