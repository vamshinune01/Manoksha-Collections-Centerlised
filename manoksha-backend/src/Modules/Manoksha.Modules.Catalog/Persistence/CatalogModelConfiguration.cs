using Manoksha.Modules.Catalog.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Catalog.Persistence;

public sealed class CatalogModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "catalog";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("sku_code_seq", SchemaName);
        modelBuilder.HasSequence<long>("internal_barcode_seq", SchemaName);

        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.Slug).HasMaxLength(100);
            b.HasIndex(x => x.Slug).IsUnique();
            b.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttributeDefinition>(b =>
        {
            b.ToTable("attribute_definitions", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40);
            b.Property(x => x.Name).HasMaxLength(100);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.AttributeId).OnDelete(DeleteBehavior.Restrict);
            b.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AttributeOption>(b =>
        {
            b.ToTable("attribute_options", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Value).HasMaxLength(100);
            b.HasIndex(x => new { x.AttributeId, x.Value }).IsUnique();
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Slug).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(4000);
            b.Property(x => x.TrackingMode).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Slug).IsUnique();
            b.HasIndex(x => x.CategoryId);
            b.HasIndex(x => x.Name);
            b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(x => x.VariantAttributes);
            b.HasMany<ProductVariantAttribute>("_variantAttributes").WithOne().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductVariantAttribute>(b =>
        {
            b.ToTable("product_variant_attributes", SchemaName);
            b.HasKey(x => new { x.ProductId, x.AttributeId });
            b.HasOne<AttributeDefinition>().WithMany().HasForeignKey(x => x.AttributeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Variant>(b =>
        {
            b.ToTable("variants", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.CombinationKey).HasMaxLength(400);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.ProductId, x.CombinationKey }).IsUnique();
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Values).WithOne().HasForeignKey(v => v.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.Navigation(x => x.Values).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<VariantAttributeValue>(b =>
        {
            b.ToTable("variant_attribute_values", SchemaName);
            b.HasKey(x => new { x.VariantId, x.AttributeId });
            b.HasOne<AttributeOption>().WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Sku>(b =>
        {
            b.ToTable("skus", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.VariantId).IsUnique();
            b.HasIndex(x => x.ProductId);
            b.HasOne<Variant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Barcode>(b =>
        {
            b.ToTable("barcodes", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RetireReason).HasMaxLength(2000);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.SkuId);
            b.HasIndex(x => x.InventoryItemId).IsUnique().HasFilter("inventory_item_id IS NOT NULL");
            b.HasOne<Sku>().WithMany().HasForeignKey(x => x.SkuId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BarcodePrint>(b =>
        {
            b.ToTable("barcode_prints", SchemaName);
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.BarcodeId, x.PrintedAt });
            b.HasOne<Barcode>().WithMany().HasForeignKey(x => x.BarcodeId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
