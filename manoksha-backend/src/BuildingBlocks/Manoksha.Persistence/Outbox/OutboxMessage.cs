using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Manoksha.Persistence.Outbox;

public sealed class OutboxMessage : Entity
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(string type, string payload, DateTimeOffset occurredAt)
    {
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    public string Type { get; private set; } = default!;

    public string Payload { get; private set; } = default!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public bool Failed { get; private set; }

    public void MarkProcessed(DateTimeOffset at)
    {
        ProcessedAt = at;
        LastError = null;
    }

    public void MarkAttemptFailed(string error, DateTimeOffset now, int maxAttempts)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
        if (Attempts >= maxAttempts)
        {
            Failed = true;
            return;
        }
        // Exponential backoff: 5s, 10s, 20s … capped at 30 minutes.
        var delay = TimeSpan.FromSeconds(Math.Min(1800, 5 * Math.Pow(2, Attempts - 1)));
        NextAttemptAt = now + delay;
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages", ManokshaDbContext.PlatformSchema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasMaxLength(200);
        b.Property(x => x.Payload).HasColumnType("jsonb").Unbounded();
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.HasIndex(x => x.NextAttemptAt).HasFilter("processed_at IS NULL AND failed = false");
    }
}
