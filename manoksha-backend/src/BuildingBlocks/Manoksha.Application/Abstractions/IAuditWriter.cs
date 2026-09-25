namespace Manoksha.Application.Abstractions;

/// <summary>
/// Records an immutable business audit entry in the SAME unit of work as the change it describes, so an
/// audit row exists if and only if the business change commits (design §18). The entry is added to the
/// current DbContext and persisted by the caller's SaveChanges/commit.
/// </summary>
public interface IAuditWriter
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken = default);
}

/// <param name="Action">Stable action code, e.g. "identity.role_assignment.granted".</param>
/// <param name="Reason">Why — mandatory for sensitive actions.</param>
public sealed record AuditRecord(
    string Action,
    string EntityType,
    string EntityId,
    object? Before = null,
    object? After = null,
    string? Reason = null,
    Guid? BranchId = null);
