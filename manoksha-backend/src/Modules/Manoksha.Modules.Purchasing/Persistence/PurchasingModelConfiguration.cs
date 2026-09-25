using Manoksha.Modules.Purchasing.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Purchasing.Persistence;

public sealed class PurchasingModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "purchasing";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("supplier_seq", SchemaName);
        modelBuilder.HasSequence<long>("po_seq", SchemaName);
        modelBuilder.HasSequence<long>("grn_seq", SchemaName);

        modelBuilder.Entity<Supplier>(b =>
        {
            b.ToTable("suppliers", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.ContactName).HasMaxLength(200);
            b.Property(x => x.MobileE164).HasMaxLength(20);
            b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.Gstin).HasMaxLength(20);
            b.Property(x => x.Address).HasMaxLength(1000);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<PurchaseOrder>(b =>
        {
            b.ToTable("purchase_orders", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.SupplierReference).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.Property(x => x.CloseReason).HasMaxLength(2000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.Ignore(x => x.IsReceivable);
            b.Ignore(x => x.IsAmendable);
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.ReceivingBranchId, x.Status });
            b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderLine>(b =>
        {
            b.ToTable("purchase_order_lines", SchemaName, t => t.HasCheckConstraint("ck_po_lines_received", "received_qty >= 0 AND received_qty <= ordered_qty AND damaged_qty >= 0 AND damaged_qty <= received_qty"));
            b.HasKey(x => x.Id);
            b.Ignore(x => x.RemainingQty);
            b.HasIndex(x => new { x.PurchaseOrderId, x.SkuId }).IsUnique();
            b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GoodsReceipt>(b =>
        {
            b.ToTable("goods_receipts", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(20);
            b.Property(x => x.SupplierInvoiceRef).HasMaxLength(100);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => x.PurchaseOrderId);
            b.HasIndex(x => new { x.BranchId, x.ReceivedAt });
            b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GoodsReceiptLine>(b =>
        {
            b.ToTable("goods_receipt_lines", SchemaName);
            b.HasKey(x => x.Id);
            b.Ignore(x => x.AcceptedQty);
            b.HasIndex(x => x.GoodsReceiptId);
            b.HasOne<GoodsReceipt>().WithMany().HasForeignKey(x => x.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
