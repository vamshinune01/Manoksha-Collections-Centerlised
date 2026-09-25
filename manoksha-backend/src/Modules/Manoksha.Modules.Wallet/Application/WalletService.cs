using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Modules.Wallet.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Wallet.Application;

internal sealed class WalletService(ManokshaDbContext db, IClock clock) : IWallets
{
    public async Task<WalletInfo> OpenAsync(Guid resellerId, CancellationToken cancellationToken = default)
    {
        if (await db.Set<ResellerWallet>().AnyAsync(w => w.ResellerId == resellerId, cancellationToken))
        {
            throw new ConflictException("WALLET_EXISTS", "This reseller already has a wallet.");
        }
        var wallet = new ResellerWallet(resellerId, clock.UtcNow);
        db.Add(wallet);
        return new WalletInfo(wallet.Id, resellerId, wallet.Balance);
    }

    public async Task<WalletInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default) =>
        await db.Set<ResellerWallet>().AsNoTracking().Where(w => w.ResellerId == resellerId)
            .Select(w => new WalletInfo(w.Id, w.ResellerId, w.Balance)).SingleOrDefaultAsync(cancellationToken);
}
