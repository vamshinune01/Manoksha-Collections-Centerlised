using Manoksha.Modules.Branches.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Branches.Persistence;

public sealed class BranchesModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "branches";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("branches", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(10);
            b.Property(x => x.Name).HasMaxLength(100);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.RowVersion).IsRowVersion();
            b.OwnsOne(x => x.Address, a =>
            {
                a.Property(p => p.Line1).HasColumnName("address_line1").HasMaxLength(300);
                a.Property(p => p.City).HasColumnName("city").HasMaxLength(100);
                a.Property(p => p.State).HasColumnName("state").HasMaxLength(100);
                a.Property(p => p.Pin).HasColumnName("pin").HasMaxLength(10);
                a.Property(p => p.Phone).HasColumnName("phone").HasMaxLength(20);
            });
            b.Navigation(x => x.Address).IsRequired();
        });

        modelBuilder.Entity<FulfillmentPriorityVersion>(b =>
        {
            b.ToTable("fulfillment_priority_versions", SchemaName);
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.VersionNo).IsUnique();
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.Ignore(x => x.Entries);
            b.HasMany<FulfillmentPriorityVersionEntry>("_entries").WithOne().HasForeignKey(e => e.VersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FulfillmentPriorityVersionEntry>(b =>
        {
            b.ToTable("fulfillment_priority_entries", SchemaName);
            b.HasKey(x => new { x.VersionId, x.BranchId });
            b.HasIndex(x => new { x.VersionId, x.Priority }).IsUnique();
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
