using Manoksha.SharedKernel;

namespace Manoksha.Modules.Notifications.Domain;

/// <summary>SPEC §29 categories.</summary>
internal static class Category
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
    public const string Info = "INFO";
}

/// <summary>
/// An in-app notification. Addressed to one user, or to everyone holding a permission (at a branch, or anywhere when BranchId is
/// null). Visibility is evaluated when read, so role changes apply immediately.
/// </summary>
internal sealed class Notification : Entity
{
    private Notification()
    {
    }

    public Notification(string dedupeKey, string eventType, string category, string title, string body, string? link, Guid? recipientUserId, string? targetPermission,
        Guid? branchId, DateTimeOffset now)
    {
        DedupeKey = dedupeKey;
        EventType = eventType;
        Category = category;
        Title = Clip(title, 200);
        Body = Clip(body, 1000);
        Link = link;
        RecipientUserId = recipientUserId;
        TargetPermission = targetPermission;
        BranchId = branchId;
        ScopeKey = targetPermission is not null && branchId is { } b ? ScopeKeyFor(b, targetPermission) : null;
        CreatedAt = now;
    }

    public string DedupeKey { get; private set; } = default!;

    public string EventType { get; private set; } = default!;

    public string Category { get; private set; } = default!;

    public string Title { get; private set; } = default!;

    public string Body { get; private set; } = default!;

    public string? Link { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public string? TargetPermission { get; private set; }

    public Guid? BranchId { get; private set; }

    /// <summary>"{branchId}|{permission}" for branch-targeted notifications (indexable visibility check).</summary>
    public string? ScopeKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static string ScopeKeyFor(Guid branchId, string permission) => $"{branchId:N}|{permission}";

    internal static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

internal sealed class NotificationRead
{
    private NotificationRead()
    {
    }

    public NotificationRead(Guid notificationId, Guid userId, DateTimeOffset now)
    {
        NotificationId = notificationId;
        UserId = userId;
        ReadAt = now;
    }

    public Guid NotificationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ReadAt { get; private set; }
}

internal enum DeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
}

/// <summary>
/// One email to send (SPEC §29). Written in the same transaction as the in-app notification; sent by the worker with retries and
/// backoff. When retries are exhausted it becomes a "Failed Notification" in the Exception Center with a manual retry.
/// </summary>
internal sealed class EmailDelivery : Entity
{
    public const int MaxAttempts = 6;

    private EmailDelivery()
    {
    }

    public EmailDelivery(string dedupeKey, string eventType, string category, string? reference, string toAddress, string? toName, string recipientKind, string subject,
        string htmlBody, string textBody, DateTimeOffset now)
    {
        DedupeKey = dedupeKey;
        EventType = eventType;
        Category = category;
        Reference = reference;
        ToAddress = toAddress;
        ToName = toName;
        RecipientKind = recipientKind;
        Subject = Notification.Clip(subject, 300);
        HtmlBody = htmlBody;
        TextBody = textBody;
        Status = DeliveryStatus.Pending;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    public string DedupeKey { get; private set; } = default!;

    public string EventType { get; private set; } = default!;

    public string Category { get; private set; } = default!;

    public string? Reference { get; private set; }

    public string ToAddress { get; private set; } = default!;

    public string? ToName { get; private set; }

    /// <summary>OWNER, CUSTOMER or RESELLER.</summary>
    public string RecipientKind { get; private set; } = default!;

    public string Subject { get; private set; } = default!;

    public string HtmlBody { get; private set; } = default!;

    public string TextBody { get; private set; } = default!;

    public DeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = DeliveryStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    /// <returns>True when retries are exhausted (now FAILED).</returns>
    public bool RecordFailure(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = Notification.Clip(error, 1000);
        if (Attempts >= MaxAttempts)
        {
            Status = DeliveryStatus.Failed;
            return true;
        }
        // 1, 5, 15, 60, 180 minutes.
        NextAttemptAt = now + TimeSpan.FromMinutes(Attempts switch { 1 => 1, 2 => 5, 3 => 15, 4 => 60, _ => 180 });
        return false;
    }

    public void Retry(DateTimeOffset now)
    {
        if (Status != DeliveryStatus.Failed)
        {
            throw new BusinessRuleException("EMAIL_NOT_FAILED", "Only failed emails can be retried.", 409);
        }
        Status = DeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }
}

internal enum AlertStatus
{
    Open = 1,
    Resolved = 2,
}

/// <summary>A sensitive alert that needs the Owner's follow-up (SPEC §28 "Sensitive Alert"): security lockouts, wallet integrity.</summary>
internal sealed class OperationalAlert : Entity
{
    private OperationalAlert()
    {
    }

    public OperationalAlert(string dedupeKey, string kind, string severity, string title, string detail, string? reference, Guid? subjectUserId, DateTimeOffset now)
    {
        DedupeKey = dedupeKey;
        Kind = kind;
        Severity = severity;
        Title = Notification.Clip(title, 200);
        Detail = Notification.Clip(detail, 1000);
        Reference = reference;
        SubjectUserId = subjectUserId;
        Status = AlertStatus.Open;
        CreatedAt = now;
    }

    public string DedupeKey { get; private set; } = default!;

    public string Kind { get; private set; } = default!;

    public string Severity { get; private set; } = default!;

    public string Title { get; private set; } = default!;

    public string Detail { get; private set; } = default!;

    public string? Reference { get; private set; }

    public Guid? SubjectUserId { get; private set; }

    public AlertStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolutionNote { get; private set; }

    public uint RowVersion { get; private set; }

    public void Resolve(Guid userId, string note, DateTimeOffset now)
    {
        if (Status != AlertStatus.Open)
        {
            throw new BusinessRuleException("ALERT_RESOLVED", "This alert is already resolved.", 409);
        }
        Status = AlertStatus.Resolved;
        ResolvedBy = userId;
        ResolvedAt = now;
        ResolutionNote = Notification.Clip(note, 1000);
    }
}
