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
        modelBuilder.HasSequence<long>("deposit_seq", SchemaName);

        modelBuilder.Entity<ResellerWallet>(b =>
        {
            b.ToTable("wallets", SchemaName, t => t.HasCheckConstraint("ck_wallets_balance_non_negative", "balance >= 0"));
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ResellerId).IsUnique();
            b.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<WalletLedgerEntry>(b =>
        {
            b.ToTable("ledger_entries", SchemaName, t =>
            {
                t.HasCheckConstraint("ck_ledger_amount_positive", "amount > 0");
                t.HasCheckConstraint("ck_ledger_balance_after_non_negative", "balance_after >= 0");
                t.HasCheckConstraint("ck_ledger_balance_arithmetic",
                    "(direction = 'Credit' AND balance_after = balance_before + amount) OR (direction = 'Debit' AND balance_after = balance_before - amount)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            b.Property(x => x.OrderNumber).HasMaxLength(30);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.ResellerId, x.Seq });
            // One credit per deposit request; one debit per order; one reversal per debit — duplicates are impossible.
            b.HasIndex(x => x.DepositRequestId).IsUnique().HasFilter("deposit_request_id IS NOT NULL");
            b.HasIndex(x => x.OrderId).IsUnique().HasFilter("type = 'Debit' AND order_id IS NOT NULL").HasDatabaseName("ux_ledger_one_debit_per_order");
            b.HasIndex(x => x.ReversesEntryId).IsUnique().HasFilter("reverses_entry_id IS NOT NULL");
            b.HasIndex(x => x.OnlineDepositId).IsUnique().HasFilter("online_deposit_id IS NOT NULL");
            b.HasOne<ResellerWallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DepositRequest>(b =>
        {
            b.ToTable("deposit_requests", SchemaName, t => t.HasCheckConstraint("ck_deposit_amount_positive", "amount > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Method).HasMaxLength(30);
            b.Property(x => x.Reference).HasMaxLength(100);
            b.Property(x => x.ResellerNote).HasMaxLength(1000);
            b.Property(x => x.ReviewNote).HasMaxLength(2000);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.ResellerId, x.SubmittedAt });
            b.HasIndex(x => x.Status);
            // A payment reference can back only one pending/credited deposit.
            b.HasIndex(x => x.Reference).IsUnique().HasFilter("status <> 'Rejected'").HasDatabaseName("ux_deposit_reference_active");
            b.HasOne<WalletFile>().WithMany().HasForeignKey(x => x.ProofFileId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OnlineDeposit>(b =>
        {
            b.ToTable("online_deposits", SchemaName, t => t.HasCheckConstraint("ck_online_deposit_amount_positive", "amount > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ProviderPaymentRef).HasMaxLength(100);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.ResellerId, x.CreatedAt });
            b.HasIndex(x => x.LedgerEntryId).IsUnique().HasFilter("ledger_entry_id IS NOT NULL");
        });

        modelBuilder.Entity<WalletFile>(b =>
        {
            b.ToTable("files", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.ObjectKey).HasMaxLength(300);
            b.Property(x => x.ContentType).HasMaxLength(100);
            b.Property(x => x.Sha256).HasMaxLength(64);
        });
    }
}
