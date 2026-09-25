using Manoksha.Modules.Audit.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Audit.Persistence;

public sealed class AuditModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "audit";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("audit_log", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).UseIdentityAlwaysColumn();
            b.HasIndex(x => x.Seq).IsUnique();
            b.Property(x => x.ActorType).HasMaxLength(20);
            b.Property(x => x.Action).HasMaxLength(150);
            b.Property(x => x.EntityType).HasMaxLength(100);
            b.Property(x => x.EntityId).HasMaxLength(100);
            b.Property(x => x.Before).HasColumnType("jsonb").Unbounded();
            b.Property(x => x.After).HasColumnType("jsonb").Unbounded();
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.Property(x => x.UserAgent).HasMaxLength(500);
            b.Property(x => x.CorrelationId).HasMaxLength(100);
            b.Property(x => x.Hash).HasMaxLength(64);
            b.HasIndex(x => new { x.EntityType, x.EntityId });
            b.HasIndex(x => x.ActorUserId);
            b.HasIndex(x => x.Action);
            b.HasIndex(x => x.OccurredAt);
        });
    }
}
