using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Inventory.Persistence;

public sealed class InventoryModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "inventory";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        foreach (var seq in new[] { "transfer_seq", "discrepancy_seq", "count_seq", "adjustment_seq" })
        {
            modelBuilder.HasSequence<long>(seq, SchemaName);
        }

        modelBuilder.Entity<StockLevel>(b =>
        {
            b.ToTable("stock_levels", SchemaName, t => t.HasCheckConstraint("ck_stock_levels_quantity_non_negative", "quantity >= 0"));
            b.HasKey(x => new { x.SkuId, x.BranchId, x.Status });
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(x => new { x.BranchId, x.Status });
        });

        modelBuilder.Entity<InventoryItem>(b =>
        {
            b.ToTable("inventory_items", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Barcode).HasMaxLength(40);
            b.Property(x => x.SourceType).HasMaxLength(30);
            b.HasIndex(x => x.Barcode).IsUnique();
            b.HasIndex(x => new { x.SkuId, x.BranchId, x.Status });
            b.HasIndex(x => new { x.SkuId, x.BranchId }).HasFilter("status = 'Available'").HasDatabaseName("ix_inventory_items_available");
        });

        modelBuilder.Entity<CostLayer>(b =>
        {
            b.ToTable("cost_layers", SchemaName, t =>
            {
                t.HasCheckConstraint("ck_cost_layers_remaining", "remaining_qty >= 0 AND remaining_qty <= original_qty");
                t.HasCheckConstraint("ck_cost_layers_unit_cost", "unit_cost >= 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.Property(x => x.SourceType).HasMaxLength(30);
            b.HasIndex(x => new { x.SkuId, x.BranchId, x.LayerDate, x.Seq }).HasFilter("remaining_qty > 0").HasDatabaseName("ix_cost_layers_fifo");
        });

        modelBuilder.Entity<CostLayerConsumption>(b =>
        {
            b.ToTable("cost_layer_consumptions", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(30);
            b.Property(x => x.ReferenceType).HasMaxLength(30);
            b.HasIndex(x => x.LayerId);
            b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
            b.HasOne<CostLayer>().WithMany().HasForeignKey(x => x.LayerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryMovement>(b =>
        {
            b.ToTable("inventory_movements", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.MovementType).HasMaxLength(40);
            b.Property(x => x.ReferenceType).HasMaxLength(30);
            b.Property(x => x.ReferenceNumber).HasMaxLength(30);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.SkuId, x.Seq });
            b.HasIndex(x => x.ItemId);
            b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        });

        modelBuilder.Entity<Transfer>(b =>
        {
            b.ToTable("transfers", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.Property(x => x.DecisionNote).HasMaxLength(2000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.SourceBranchId, x.Status });
            b.HasIndex(x => new { x.DestinationBranchId, x.Status });
        });

        modelBuilder.Entity<TransferLine>(b =>
        {
            b.ToTable("transfer_lines", SchemaName);
            b.HasKey(x => x.Id);
            b.Ignore(x => x.OutstandingQty);
            b.HasIndex(x => new { x.TransferId, x.SkuId }).IsUnique();
            b.HasOne<Transfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransferLineItem>(b =>
        {
            b.ToTable("transfer_line_items", SchemaName);
            b.HasKey(x => new { x.TransferLineId, x.ItemId });
            b.Property(x => x.Outcome).HasMaxLength(20);
            b.HasOne<TransferLine>().WithMany().HasForeignKey(x => x.TransferLineId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransferCostAllocation>(b =>
        {
            b.ToTable("transfer_cost_allocations", SchemaName, t => t.HasCheckConstraint("ck_transfer_alloc_settled", "settled_qty >= 0 AND settled_qty <= quantity"));
            b.HasKey(x => x.Id);
            b.Ignore(x => x.Unsettled);
            b.HasIndex(x => x.TransferLineId);
            b.HasOne<TransferLine>().WithMany().HasForeignKey(x => x.TransferLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryDiscrepancy>(b =>
        {
            b.ToTable("discrepancies", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.SourceType).HasMaxLength(20);
            b.Property(x => x.SourceNumber).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Resolution).HasMaxLength(30);
            b.Property(x => x.ResolutionNotes).HasMaxLength(4000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Ignore(x => x.Variance);
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.BranchId, x.Status });
            b.HasIndex(x => new { x.SourceType, x.SourceId });
        });

        modelBuilder.Entity<StockCount>(b =>
        {
            b.ToTable("stock_counts", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.BranchId, x.Status });
        });

        modelBuilder.Entity<StockCountLine>(b =>
        {
            b.ToTable("stock_count_lines", SchemaName);
            b.HasKey(x => new { x.CountId, x.SkuId });
            b.HasOne<StockCount>().WithMany().HasForeignKey(x => x.CountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryAdjustment>(b =>
        {
            b.ToTable("adjustments", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ReasonCode).HasMaxLength(40);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.DecisionNote).HasMaxLength(2000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.BranchId, x.Status });
            b.HasOne<InventoryDiscrepancy>().WithMany().HasForeignKey(x => x.DiscrepancyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
