using Manoksha.Modules.Resellers.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Resellers.Persistence;

public sealed class ResellersModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "resellers";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("reseller_number_seq", SchemaName);

        modelBuilder.Entity<Reseller>(b =>
        {
            b.ToTable("resellers", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.ResellerNumber).HasMaxLength(20);
            b.Property(x => x.MobileE164).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.ResellerNumber).IsUnique();
            b.HasIndex(x => x.MobileE164).IsUnique();
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => x.Status);
            b.OwnsOne(x => x.Profile, p =>
            {
                p.Property(x => x.ContactName).HasColumnName("contact_name").HasMaxLength(200);
                p.Property(x => x.BusinessName).HasColumnName("business_name").HasMaxLength(200);
                p.Property(x => x.Email).HasColumnName("email").HasMaxLength(254);
                p.Property(x => x.AddressLine).HasColumnName("address_line").HasMaxLength(500);
                p.Property(x => x.City).HasColumnName("city").HasMaxLength(100);
                p.Property(x => x.State).HasColumnName("state").HasMaxLength(100);
                p.Property(x => x.Pin).HasColumnName("pin").HasMaxLength(6);
                p.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
            });
            b.Navigation(x => x.Profile).IsRequired();
        });

        modelBuilder.Entity<ResellerStatusChange>(b =>
        {
            b.ToTable("reseller_status_changes", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.ResellerId, x.OccurredAt });
            b.HasOne<Reseller>().WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommercialTerm>(b =>
        {
            b.ToTable("commercial_terms", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.DiscountPct).HasPrecision(7, 4);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.ResellerId, x.Version }).IsUnique();
            b.HasOne<Reseller>().WithMany().HasForeignKey(x => x.ResellerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
