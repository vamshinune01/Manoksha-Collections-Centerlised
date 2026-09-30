using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Notifications.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Notifications.Endpoints;

internal static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Notifications").RequireAudience(Audiences.Admin);
        admin.MapGet("/notifications", (bool? unreadOnly, NotificationService s, CancellationToken ct) => s.InboxAsync(unreadOnly ?? false, ct))
            .RequirePermission(Permissions.Notifications.View).WithName("MyNotifications");
        admin.MapGet("/notifications/unread-count", async (NotificationService s, CancellationToken ct) => new { unread = await s.UnreadCountAsync(ct) })
            .RequirePermission(Permissions.Notifications.View).WithName("MyUnreadNotificationCount");
        admin.MapPost("/notifications/{id:guid}/read", async (Guid id, NotificationService s, CancellationToken ct) =>
            {
                await s.MarkReadAsync(id, ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.Notifications.View).WithName("MarkNotificationRead");
        admin.MapPost("/notifications/read-all", async (NotificationService s, CancellationToken ct) =>
            {
                await s.MarkReadAsync(null, ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.Notifications.View).WithName("MarkAllNotificationsRead");

        admin.MapGet("/notifications/emails", (string? status, NotificationService s, CancellationToken ct) => s.EmailsAsync(status, ct))
            .RequirePermission(Permissions.Exceptions.Manage).WithName("ListEmailDeliveries");
        admin.MapGet("/notifications/emails/{id:guid}", (Guid id, NotificationService s, CancellationToken ct) => s.EmailPreviewAsync(id, ct))
            .RequirePermission(Permissions.Exceptions.Manage).WithName("GetEmailDelivery");
        admin.MapPost("/notifications/emails/{id:guid}/retry", (Guid id, NotificationService s, CancellationToken ct) => s.RetryEmailAsync(id, ct))
            .RequirePermission(Permissions.Exceptions.Manage).WithName("RetryEmailDelivery");

        admin.MapGet("/alerts", (string? status, NotificationService s, CancellationToken ct) => s.AlertsAsync(status, ct))
            .RequirePermission(Permissions.Exceptions.Manage).WithName("ListAlerts");
        admin.MapPost("/alerts/{id:guid}/resolve", (Guid id, ResolveAlertRequest r, NotificationService s, CancellationToken ct) => s.ResolveAlertAsync(id, r, ct))
            .RequirePermission(Permissions.Exceptions.Manage).WithName("ResolveAlert");
    }
}
