using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Manoksha.Modules.Orders.Persistence;

public sealed class OrdersModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "orders";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("order_number_seq", SchemaName);
        modelBuilder.HasSequence<long>("inquiry_number_seq", SchemaName);

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders", SchemaName, t => t.HasCheckConstraint("ck_orders_totals", "grand_total = merchandise_total + shipping_fee AND merchandise_total >= 0 AND shipping_fee >= 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Number).HasMaxLength(30);
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => new { x.ResellerId, x.CreatedAt });
            b.HasIndex(x => new { x.FulfillmentBranchId, x.Status });
            b.HasIndex(x => new { x.CustomerUserId, x.CreatedAt });
            b.HasIndex(x => x.PaymentAttemptId).IsUnique().HasFilter("payment_attempt_id IS NOT NULL");
            b.OwnsOne(x => x.Delivery, d => MapDelivery(d, "delivery_"));
            b.Navigation(x => x.Delivery).IsRequired();
        });

        modelBuilder.Entity<OrderLine>(b =>
        {
            b.ToTable("order_lines", SchemaName, t => t.HasCheckConstraint("ck_order_lines_amounts",
                "quantity > 0 AND final_unit_price >= 0 AND final_unit_price <= retail_unit_price AND line_total = final_unit_price * quantity"));
            b.HasKey(x => x.Id);
            b.Property(x => x.SkuCode).HasMaxLength(40);
            b.Property(x => x.ProductName).HasMaxLength(200);
            b.Property(x => x.VariantName).HasMaxLength(200);
            b.Property(x => x.DiscountSource).HasMaxLength(30);
            b.Property(x => x.DiscountPct).HasPrecision(7, 4);
            b.HasIndex(x => x.OrderId);
            b.HasIndex(x => x.SkuId);
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderStatusChange>(b =>
        {
            b.ToTable("order_status_changes", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.Note).HasMaxLength(2000);
            b.HasIndex(x => new { x.OrderId, x.OccurredAt });
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reservation>(b =>
        {
            b.ToTable("reservations", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.CloseReason).HasMaxLength(500);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.OrderId);
            // One live hold per order; the sweeper scans active holds by expiry.
            b.HasIndex(x => x.OrderId).IsUnique().HasFilter("status = 'Active'").HasDatabaseName("ux_reservations_one_active_per_order");
            b.HasIndex(x => x.ExpiresAt).HasFilter("status = 'Active'").HasDatabaseName("ix_reservations_active_expiry");
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReservationLine>(b =>
        {
            b.ToTable("reservation_lines", SchemaName, t => t.HasCheckConstraint("ck_reservation_lines_quantity", "quantity > 0"));
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ReservationId);
            b.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrderLine>().WithMany().HasForeignKey(x => x.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ResellerCustomer>(b =>
        {
            b.ToTable("reseller_customers", SchemaName);
            b.HasKey(x => x.Id);
            // Unique (reseller_id, mobile) is created in the migration (spans owner + owned columns).
            b.OwnsOne(x => x.Details, d => MapDelivery(d, string.Empty));
            b.Navigation(x => x.Details).IsRequired();
            b.HasIndex(x => x.ResellerId);
        });

        modelBuilder.Entity<FulfillmentInquiry>(b =>
        {
            b.ToTable("fulfillment_inquiries", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Reference).HasMaxLength(20);
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ContactName).HasMaxLength(200);
            b.Property(x => x.ContactMobile).HasMaxLength(20);
            b.Property(x => x.CartJson).HasColumnName("cart").HasColumnType("jsonb").Unbounded();
            b.Property(x => x.EvaluationsJson).HasColumnName("evaluations").HasColumnType("jsonb").Unbounded();
            b.Property(x => x.FailureReason).HasMaxLength(200);
            b.Property(x => x.Status).HasMaxLength(20);
            b.HasIndex(x => x.Reference).IsUnique();
            b.HasIndex(x => new { x.Status, x.CreatedAt });
        });
    }

    private static void MapDelivery<T>(OwnedNavigationBuilder<T, DeliveryDetails> d, string prefix)
        where T : class
    {
        d.Property(p => p.Name).HasColumnName(prefix + "name").HasMaxLength(200);
        d.Property(p => p.Mobile).HasColumnName(prefix + "mobile").HasMaxLength(20);
        d.Property(p => p.Email).HasColumnName(prefix + "email").HasMaxLength(254);
        d.Property(p => p.AddressLine).HasColumnName(prefix + "address_line").HasMaxLength(500);
        d.Property(p => p.City).HasColumnName(prefix + "city").HasMaxLength(100);
        d.Property(p => p.State).HasColumnName(prefix + "state").HasMaxLength(100);
        d.Property(p => p.Pin).HasColumnName(prefix + "pin").HasMaxLength(6);
    }
}
