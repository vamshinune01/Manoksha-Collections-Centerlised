using Manoksha.SharedKernel;

namespace Manoksha.Modules.Wallet.Domain;

/// <summary>
/// A reseller's prepaid wallet. <see cref="Balance"/> is a lock-protected cache of the immutable ledger (Phase 5);
/// the database enforces balance ≥ 0.
/// </summary>
internal sealed class ResellerWallet : Entity
{
    private ResellerWallet()
    {
    }

    public ResellerWallet(Guid resellerId, DateTimeOffset now)
    {
        ResellerId = resellerId;
        Balance = 0m;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid ResellerId { get; private set; }

    public decimal Balance { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint RowVersion { get; private set; }
}
