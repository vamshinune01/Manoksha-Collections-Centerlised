using Manoksha.Modules.Wallet.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Wallet.Persistence;

public sealed class WalletModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "wallet";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResellerWallet>(b =>
        {
            b.ToTable("wallets", SchemaName, t => t.HasCheckConstraint("ck_wallets_balance_non_negative", "balance >= 0"));
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ResellerId).IsUnique();
            b.Property(x => x.RowVersion).IsRowVersion();
        });
    }
}
