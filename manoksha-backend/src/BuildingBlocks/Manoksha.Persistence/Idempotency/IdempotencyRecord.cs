using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Manoksha.Persistence.Idempotency;

public sealed class IdempotencyRecord
{
    public string Scope { get; private set; } = default!;

    /// <summary>Guid.Empty for anonymous callers (e.g. OTP flows).</summary>
    public Guid UserId { get; private set; }

    public string Key { get; private set; } = default!;

    public string RequestHash { get; private set; } = default!;

    public string Status { get; private set; } = default!;

    public string? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records", ManokshaDbContext.PlatformSchema);
        b.HasKey(x => new { x.Scope, x.UserId, x.Key });
        b.Property(x => x.Scope).HasMaxLength(100);
        b.Property(x => x.Key).HasMaxLength(128);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.ResponseBody).HasColumnType("jsonb").Unbounded();
        b.HasIndex(x => x.ExpiresAt);
    }
}
