using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Notifications.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Notifications.Application;

public sealed record NotificationDto(Guid Id, string Category, string Title, string Body, string? Link, DateTimeOffset CreatedAt, bool Read);

public sealed record InboxDto(int Unread, IReadOnlyList<NotificationDto> Items);

public sealed record EmailDeliveryDto(Guid Id, string EventType, string Category, string? Reference, string ToAddress, string? ToName, string RecipientKind,
    string Subject, string Status, int Attempts, DateTimeOffset NextAttemptAt, string? LastError, DateTimeOffset? SentAt, DateTimeOffset CreatedAt);

public sealed record EmailPreviewDto(Guid Id, string Subject, string ToAddress, string HtmlBody, string TextBody);

public sealed record AlertDto(Guid Id, string Kind, string Severity, string Title, string Detail, string? Reference, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt, string? ResolutionNote);

public sealed record ResolveAlertRequest(string Note);

/// <summary>In-app inbox (the bell) and the Owner's delivery log, failed-email retry and sensitive-alert follow-up.</summary>
internal sealed class NotificationService(ManokshaDbContext db, IPermissionService permissions, ICurrentUser currentUser, IAuditWriter audit, IClock clock)
{
    private static readonly TimeSpan InboxWindow = TimeSpan.FromDays(30);

    public async Task<InboxDto> InboxAsync(bool unreadOnly, CancellationToken ct)
    {
        var me = currentUser.UserId;
        var q = await VisibleAsync(ct);
        var unread = await q.CountAsync(n => !db.Set<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == me), ct);
        if (unreadOnly)
        {
            q = q.Where(n => !db.Set<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == me));
        }
        var items = await q.OrderByDescending(n => n.CreatedAt).Take(100)
            .Select(n => new NotificationDto(n.Id, n.Category, n.Title, n.Body, n.Link, n.CreatedAt,
                db.Set<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == me)))
            .ToListAsync(ct);
        return new InboxDto(unread, items);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var me = currentUser.UserId;
        return await (await VisibleAsync(ct)).CountAsync(n => !db.Set<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == me), ct);
    }

    public async Task MarkReadAsync(Guid? id, CancellationToken ct)
    {
        var me = currentUser.UserId;
        var q = (await VisibleAsync(ct)).Where(n => !db.Set<NotificationRead>().Any(r => r.NotificationId == n.Id && r.UserId == me));
        if (id is { } one)
        {
            q = q.Where(n => n.Id == one);
        }
        foreach (var nid in await q.Select(n => n.Id).ToListAsync(ct))
        {
            db.Add(new NotificationRead(nid, me, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Notifications addressed to me, or to a permission I hold (globally, or at the notification's branch).</summary>
    private async Task<IQueryable<Notification>> VisibleAsync(CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var me = currentUser.UserId;
        var since = clock.UtcNow - InboxWindow;
        var q = db.Set<Notification>().AsNoTracking().Where(n => n.CreatedAt >= since);
        if (access.IsOwner)
        {
            return q.Where(n => n.RecipientUserId == me || n.TargetPermission != null);
        }
        var global = access.GlobalPermissions.ToList();
        var scoped = access.BranchPermissions.SelectMany(kv => kv.Value.Select(p => Notification.ScopeKeyFor(kv.Key, p))).ToList();
        return q.Where(n => n.RecipientUserId == me
                            || (n.TargetPermission != null && global.Contains(n.TargetPermission))
                            || (n.ScopeKey != null && scoped.Contains(n.ScopeKey)));
    }

    // ---- Owner: delivery log, retry, alerts ----

    public async Task<IReadOnlyList<EmailDeliveryDto>> EmailsAsync(string? status, CancellationToken ct)
    {
        var q = db.Set<EmailDelivery>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DeliveryStatus>(status, true, out var s))
            {
                throw new BusinessRuleException("STATUS_INVALID", "Status must be Pending, Sent or Failed.", 400);
            }
            q = q.Where(d => d.Status == s);
        }
        return await q.OrderByDescending(d => d.CreatedAt).Take(200)
            .Select(d => new EmailDeliveryDto(d.Id, d.EventType, d.Category, d.Reference, d.ToAddress, d.ToName, d.RecipientKind, d.Subject, d.Status.ToString(),
                d.Attempts, d.NextAttemptAt, d.LastError, d.SentAt, d.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<EmailPreviewDto> EmailPreviewAsync(Guid id, CancellationToken ct) =>
        await db.Set<EmailDelivery>().AsNoTracking().Where(d => d.Id == id)
            .Select(d => new EmailPreviewDto(d.Id, d.Subject, d.ToAddress, d.HtmlBody, d.TextBody)).SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("EMAIL_NOT_FOUND", "Email not found.");

    public async Task<EmailDeliveryDto> RetryEmailAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Set<EmailDelivery>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("EMAIL_NOT_FOUND", "Email not found.");
        d.Retry(clock.UtcNow);
        await audit.RecordAsync(new AuditRecord("notifications.email.retried", "EmailDelivery", id.ToString(), new { status = "Failed" }, new { status = "Pending", d.ToAddress }), ct);
        await db.SaveChangesAsync(ct);
        return new EmailDeliveryDto(d.Id, d.EventType, d.Category, d.Reference, d.ToAddress, d.ToName, d.RecipientKind, d.Subject, d.Status.ToString(), d.Attempts,
            d.NextAttemptAt, d.LastError, d.SentAt, d.CreatedAt);
    }

    public async Task<IReadOnlyList<AlertDto>> AlertsAsync(string? status, CancellationToken ct)
    {
        var q = db.Set<OperationalAlert>().AsNoTracking();
        if (Enum.TryParse<AlertStatus>(status, true, out var s))
        {
            q = q.Where(a => a.Status == s);
        }
        return await q.OrderByDescending(a => a.CreatedAt).Take(200).Select(a => ToDto(a)).ToListAsync(ct);
    }

    public async Task<AlertDto> ResolveAlertAsync(Guid id, ResolveAlertRequest request, CancellationToken ct)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            throw new BusinessRuleException("RESOLUTION_NOTE_REQUIRED", "Describe the follow-up.", 400);
        }
        var a = await db.Set<OperationalAlert>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ALERT_NOT_FOUND", "Alert not found.");
        a.Resolve(currentUser.UserId, note, clock.UtcNow);
        await audit.RecordAsync(new AuditRecord("notifications.alert.resolved", "OperationalAlert", id.ToString(), new { status = "Open" },
            new { status = "Resolved", a.Kind }, note), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    private static AlertDto ToDto(OperationalAlert a) =>
        new(a.Id, a.Kind, a.Severity, a.Title, a.Detail, a.Reference, a.Status.ToString(), a.CreatedAt, a.ResolvedAt, a.ResolutionNote);
}
