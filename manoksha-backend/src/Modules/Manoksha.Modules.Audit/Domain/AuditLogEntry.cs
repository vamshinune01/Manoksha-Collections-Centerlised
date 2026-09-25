using Manoksha.SharedKernel;

namespace Manoksha.Modules.Audit.Domain;

/// <summary>Immutable audit record. The table is append-only at the database level.</summary>
internal sealed class AuditLogEntry : Entity
{
    private AuditLogEntry()
    {
    }

    public AuditLogEntry(
        DateTimeOffset occurredAt,
        Guid? actorUserId,
        string actorType,
        string[] actorRoles,
        Guid? branchId,
        string action,
        string entityType,
        string entityId,
        string? before,
        string? after,
        string? reason,
        string? ipAddress,
        string? userAgent,
        string correlationId,
        string hash)
    {
        OccurredAt = occurredAt;
        ActorUserId = actorUserId;
        ActorType = actorType;
        ActorRoles = actorRoles;
        BranchId = branchId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Before = before;
        After = after;
        Reason = reason;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CorrelationId = correlationId;
        Hash = hash;
    }

    /// <summary>Monotonic database sequence; gives a stable total order for paging and sealing.</summary>
    public long Seq { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>INTERNAL / CUSTOMER / RESELLER / SYSTEM / ANONYMOUS.</summary>
    public string ActorType { get; private set; } = default!;

    public string[] ActorRoles { get; private set; } = [];

    public Guid? BranchId { get; private set; }

    public string Action { get; private set; } = default!;

    public string EntityType { get; private set; } = default!;

    public string EntityId { get; private set; } = default!;

    public string? Before { get; private set; }

    public string? After { get; private set; }

    public string? Reason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string CorrelationId { get; private set; } = default!;

    /// <summary>SHA-256 of the row content (tamper evidence).</summary>
    public string Hash { get; private set; } = default!;
}
