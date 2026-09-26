using Manoksha.Modules.Payments.Domain;
using Manoksha.Modules.Payments.Simulator;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Payments.Persistence;

public sealed class PaymentsModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "payments";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("reconciliation_seq", SchemaName);

        modelBuilder.Entity<PaymentAttempt>(b =>
        {
            b.ToTable("payment_attempts", SchemaName, t => t.HasCheckConstraint("ck_payment_attempts_amount_positive", "amount > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Purpose).HasMaxLength(30);
            b.Property(x => x.ReferenceNumber).HasMaxLength(30);
            b.Property(x => x.Provider).HasMaxLength(30);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ProviderOrderRef).HasMaxLength(100);
            b.Property(x => x.ProviderPaymentRef).HasMaxLength(100);
            b.Property(x => x.RedirectUrl).HasMaxLength(500);
            b.Property(x => x.Description).HasMaxLength(200);
            b.Property(x => x.ReturnPath).HasMaxLength(200);
            b.Property(x => x.FailureReason).HasMaxLength(500);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.Provider, x.ProviderOrderRef }).IsUnique().HasFilter("provider_order_ref IS NOT NULL");
            b.HasIndex(x => new { x.Provider, x.ProviderPaymentRef }).IsUnique().HasFilter("provider_payment_ref IS NOT NULL");
            b.HasIndex(x => new { x.Purpose, x.ReferenceId });
            // At most one live attempt per order / deposit (design §13 point 2).
            b.HasIndex(x => new { x.Purpose, x.ReferenceId }).IsUnique().HasFilter("status IN ('Initiated', 'Pending')").HasDatabaseName("ux_payment_attempts_one_live");
            b.HasIndex(x => new { x.Status, x.NextPollAt });
        });

        modelBuilder.Entity<PaymentStatusChange>(b =>
        {
            b.ToTable("payment_status_changes", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Source).HasMaxLength(30);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.AttemptId, x.OccurredAt });
            b.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderEvent>(b =>
        {
            b.ToTable("provider_events", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Provider).HasMaxLength(30);
            b.Property(x => x.ProviderEventId).HasMaxLength(100);
            b.Property(x => x.ProviderOrderRef).HasMaxLength(100);
            b.Property(x => x.EventType).HasMaxLength(60);
            b.Property(x => x.Payload).HasColumnType("jsonb").Unbounded();
            b.Property(x => x.ProcessingResult).HasMaxLength(60);
            b.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique();
            b.HasIndex(x => x.ProcessedAt).HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<PaymentReconciliation>(b =>
        {
            b.ToTable("reconciliations", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.CaseNumber).HasMaxLength(20);
            b.Property(x => x.Provider).HasMaxLength(30);
            b.Property(x => x.ProviderOrderRef).HasMaxLength(100);
            b.Property(x => x.ProviderPaymentRef).HasMaxLength(100);
            b.Property(x => x.Purpose).HasMaxLength(30);
            b.Property(x => x.ReferenceNumber).HasMaxLength(30);
            b.Property(x => x.ReasonCode).HasMaxLength(60);
            b.Property(x => x.Detail).HasMaxLength(2000);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.OwnerAction).HasMaxLength(2000);
            b.Property(x => x.ExternalRefundRef).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(4000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.CaseNumber).IsUnique();
            b.HasIndex(x => x.PaymentAttemptId).IsUnique();
            b.HasIndex(x => new { x.Status, x.CreatedAt });
            b.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SimulatorTransaction>(b =>
        {
            b.ToTable("simulator_transactions", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderOrderRef).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(200);
            b.Property(x => x.ReturnPath).HasMaxLength(200);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ProviderPaymentRef).HasMaxLength(100);
            b.Property(x => x.FailureReason).HasMaxLength(200);
            b.HasIndex(x => x.MerchantAttemptId).IsUnique();
            b.HasIndex(x => x.ProviderOrderRef).IsUnique();
        });
    }
}
