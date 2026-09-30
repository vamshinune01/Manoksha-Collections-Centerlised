using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Notifications.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Manoksha.Modules.Notifications.Application;

public sealed class NotificationOptions
{
    public string ShopName { get; set; } = "Manoksha Collections";

    /// <summary>Customer/reseller website, for links in emails (e.g. https://shop.example.com). Empty = no links.</summary>
    public string CustomerWebUrl { get; set; } = string.Empty;

    /// <summary>Admin website, for links in staff emails and in-app notifications' email copies. Empty = no links.</summary>
    public string AdminWebUrl { get; set; } = string.Empty;
}

/// <summary>
/// Writes notifications, emails and alerts idempotently (dedupe keys), inside the outbox dispatcher's transaction. Handlers only add
/// rows; nothing is sent here, so a slow or failing email provider never affects the event or the business transaction (SPEC §29).
/// </summary>
internal sealed class NotificationPublisher(ManokshaDbContext db, IUserDirectory users, IOptions<NotificationOptions> options, IClock clock)
{
    public NotificationOptions Options => options.Value;

    public async Task InAppAsync(string dedupeKey, string eventType, string category, string title, string body, string? link, string? permission, Guid? branchId,
        Guid? recipientUserId, CancellationToken ct)
    {
        if (db.Set<Notification>().Local.Any(n => n.DedupeKey == dedupeKey) || await db.Set<Notification>().AnyAsync(n => n.DedupeKey == dedupeKey, ct))
        {
            return;
        }
        db.Add(new Notification(dedupeKey, eventType, category, title, body, link, recipientUserId, permission, branchId, clock.UtcNow));
    }

    /// <param name="to">Skipped when empty or not a valid address (the person has no usable email; everything is in their account).</param>
    public async Task EmailAsync(string dedupeKey, string eventType, string category, string? reference, string? to, string? toName, string recipientKind,
        EmailContent content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to) || !EmailAddress.IsValid(to.Trim()))
        {
            return;
        }
        if (db.Set<EmailDelivery>().Local.Any(n => n.DedupeKey == dedupeKey) || await db.Set<EmailDelivery>().AnyAsync(n => n.DedupeKey == dedupeKey, ct))
        {
            return;
        }
        db.Add(new EmailDelivery(dedupeKey, eventType, category, reference, to.Trim(), toName, recipientKind, content.Subject, content.Html, content.Text, clock.UtcNow));
    }

    /// <summary>CRITICAL events and the daily summary are emailed to every active Owner (ADR-001 §39).</summary>
    public async Task EmailOwnersAsync(string dedupeKey, string eventType, string category, string? reference, EmailContent content, CancellationToken ct)
    {
        foreach (var owner in await users.ListOwnersAsync(ct))
        {
            await EmailAsync($"{dedupeKey}:{owner.UserId:N}", eventType, category, reference, owner.Email, owner.DisplayName, "OWNER", content, ct);
        }
    }

    public async Task AlertAsync(string dedupeKey, string kind, string severity, string title, string detail, string? reference, Guid? subjectUserId, CancellationToken ct)
    {
        if (db.Set<OperationalAlert>().Local.Any(n => n.DedupeKey == dedupeKey) || await db.Set<OperationalAlert>().AnyAsync(n => n.DedupeKey == dedupeKey, ct))
        {
            return;
        }
        db.Add(new OperationalAlert(dedupeKey, kind, severity, title, detail, reference, subjectUserId, clock.UtcNow));
    }

    public string? AdminLink(string? path) => Link(options.Value.AdminWebUrl, path);

    public string? CustomerLink(string? path) => Link(options.Value.CustomerWebUrl, path);

    private static string? Link(string baseUrl, string? path) =>
        string.IsNullOrWhiteSpace(baseUrl) || path is null ? null : baseUrl.TrimEnd('/') + path;
}
