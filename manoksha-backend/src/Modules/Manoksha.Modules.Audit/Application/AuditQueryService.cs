using Manoksha.Modules.Audit.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Audit.Application;

internal sealed record AuditSearchQuery(
    string? EntityType,
    string? EntityId,
    Guid? ActorUserId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long? BeforeSeq,
    int? Limit);

internal sealed record AuditEntryDto(
    Guid Id,
    long Seq,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string ActorType,
    string[] ActorRoles,
    Guid? BranchId,
    string Action,
    string EntityType,
    string EntityId,
    string? Before,
    string? After,
    string? Reason,
    string? IpAddress,
    string CorrelationId);

internal sealed record AuditPage(IReadOnlyList<AuditEntryDto> Items, long? NextBeforeSeq);

internal sealed class AuditQueryService(ManokshaDbContext db)
{
    public async Task<AuditPage> SearchAsync(AuditSearchQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit ?? 50, 1, 200);
        var q = db.Set<AuditLogEntry>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            q = q.Where(x => x.EntityType == query.EntityType);
        }
        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            q = q.Where(x => x.EntityId == query.EntityId);
        }
        if (query.ActorUserId is { } actor)
        {
            q = q.Where(x => x.ActorUserId == actor);
        }
        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(x => x.Action.StartsWith(query.Action));
        }
        if (query.From is { } from)
        {
            q = q.Where(x => x.OccurredAt >= from);
        }
        if (query.To is { } to)
        {
            q = q.Where(x => x.OccurredAt < to);
        }
        if (query.BeforeSeq is { } before)
        {
            q = q.Where(x => x.Seq < before);
        }

        var items = await q.OrderByDescending(x => x.Seq).Take(limit + 1)
            .Select(x => new AuditEntryDto(x.Id, x.Seq, x.OccurredAt, x.ActorUserId, x.ActorType, x.ActorRoles, x.BranchId,
                x.Action, x.EntityType, x.EntityId, x.Before, x.After, x.Reason, x.IpAddress, x.CorrelationId))
            .ToListAsync(cancellationToken);

        long? next = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            next = items[^1].Seq;
        }
        return new AuditPage(items, next);
    }
}
