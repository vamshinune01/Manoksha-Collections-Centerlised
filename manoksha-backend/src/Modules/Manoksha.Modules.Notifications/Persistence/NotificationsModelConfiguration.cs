using Manoksha.Modules.Notifications.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Notifications.Persistence;

public sealed class NotificationsModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "notifications";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("notifications", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.DedupeKey).HasMaxLength(200);
            b.Property(x => x.EventType).HasMaxLength(100);
            b.Property(x => x.Category).HasMaxLength(10);
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Body).HasMaxLength(1000);
            b.Property(x => x.Link).HasMaxLength(300);
            b.Property(x => x.TargetPermission).HasMaxLength(100);
            b.Property(x => x.ScopeKey).HasMaxLength(150);
            b.HasIndex(x => x.DedupeKey).IsUnique();
            b.HasIndex(x => x.CreatedAt);
            b.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
        });

        modelBuilder.Entity<NotificationRead>(b =>
        {
            b.ToTable("notification_reads", SchemaName);
            b.HasKey(x => new { x.NotificationId, x.UserId });
            b.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailDelivery>(b =>
        {
            b.ToTable("email_deliveries", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.DedupeKey).HasMaxLength(200);
            b.Property(x => x.EventType).HasMaxLength(100);
            b.Property(x => x.Category).HasMaxLength(10);
            b.Property(x => x.Reference).HasMaxLength(100);
            b.Property(x => x.ToAddress).HasMaxLength(254);
            b.Property(x => x.ToName).HasMaxLength(200);
            b.Property(x => x.RecipientKind).HasMaxLength(20);
            b.Property(x => x.Subject).HasMaxLength(300);
            b.Property(x => x.HtmlBody).Unbounded();
            b.Property(x => x.TextBody).Unbounded();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.LastError).HasMaxLength(1000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.DedupeKey).IsUnique();
            b.HasIndex(x => new { x.Status, x.NextAttemptAt });
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<OperationalAlert>(b =>
        {
            b.ToTable("alerts", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.DedupeKey).HasMaxLength(200);
            b.Property(x => x.Kind).HasMaxLength(50);
            b.Property(x => x.Severity).HasMaxLength(10);
            b.Property(x => x.Title).HasMaxLength(200);
            b.Property(x => x.Detail).HasMaxLength(1000);
            b.Property(x => x.Reference).HasMaxLength(100);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.ResolutionNote).HasMaxLength(1000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.DedupeKey).IsUnique();
            b.HasIndex(x => new { x.Status, x.CreatedAt });
        });
    }
}
