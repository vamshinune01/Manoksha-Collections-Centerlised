using Manoksha.Application.Abstractions;
using Manoksha.Modules.Notifications.Application;
using Manoksha.Modules.Notifications.Domain;
using Manoksha.Modules.Reporting.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Notifications;

/// <summary>Worker entry points (design §16): email sending with retries, and the daily low-stock alert and Owner summary.</summary>
public static class NotificationJobs
{
    private const int Batch = 20;
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    /// <summary>Sends due emails. A failure never affects anything else; after the last attempt the email becomes a Failed Notification.</summary>
    public static async Task<int> SendDueEmailsAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<ManokshaDbContext>();
        var sender = services.GetRequiredService<IEmailSender>();
        var publisher = services.GetRequiredService<NotificationPublisher>();
        var clock = services.GetRequiredService<IClock>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NotificationJobs));
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var due = await db.Set<EmailDelivery>()
            .FromSqlInterpolated($"""
                SELECT d.*, d.xmin FROM notifications.email_deliveries d
                WHERE d.status = 'Pending' AND d.next_attempt_at <= {now}
                ORDER BY d.created_at
                LIMIT {Batch}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        foreach (var d in due)
        {
            try
            {
                await sender.SendAsync(new EmailMessage(d.ToAddress, d.Subject, d.HtmlBody, d.TextBody), ct);
                d.MarkSent(clock.UtcNow);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Email {Id} ({EventType}) failed on attempt {Attempt}", d.Id, d.EventType, d.Attempts + 1);
                if (d.RecordFailure(ex.Message, clock.UtcNow))
                {
                    await publisher.InAppAsync($"email_failed:{d.Id:N}:{d.Attempts}", "notifications.email_failed", Category.Warning, $"Email could not be sent: {d.Subject}",
                        $"To {d.ToAddress} after {d.Attempts} attempts. Retry it from the Exception Center.", "/exceptions?type=FAILED_NOTIFICATION",
                        P.Exceptions.Manage, null, null, ct);
                }
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return due.Count;
    }

    /// <summary>Once a day: low-stock alert per branch (from 09:00 IST) and the Owner's summary email (at the configured hour).</summary>
    public static async Task<int> RunDailyAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<ManokshaDbContext>();
        var clock = services.GetRequiredService<IClock>();
        var settings = services.GetRequiredService<ISettingsReader>();
        var local = clock.UtcNow.ToOffset(Ist);
        var today = DateOnly.FromDateTime(local.DateTime);
        var created = 0;
        if (local.Hour >= 9)
        {
            created += await LowStockAlertsAsync(services, today, ct);
        }
        if (local.Hour >= await settings.GetAsync<int>(SettingKeys.DailySummaryHour, ct))
        {
            created += await DailySummaryAsync(services, today, ct);
        }
        await db.SaveChangesAsync(ct);
        return created;
    }

    internal static async Task<int> LowStockAlertsAsync(IServiceProvider services, DateOnly today, CancellationToken ct)
    {
        var reporting = services.GetRequiredService<IReportingQueries>();
        var publisher = services.GetRequiredService<NotificationPublisher>();
        var created = 0;
        foreach (var branch in (await reporting.LowStockAsync(null, ct)).GroupBy(r => (r.BranchId, r.BranchName)))
        {
            var sample = string.Join(", ", branch.Take(5).Select(r => $"{r.SkuCode} ({r.Available})"));
            await publisher.InAppAsync($"low_stock:{branch.Key.BranchId:N}:{today:yyyyMMdd}", "reporting.low_stock", Category.Warning,
                $"{branch.Count()} item(s) low on stock at {branch.Key.BranchName}", sample + (branch.Count() > 5 ? ", …" : string.Empty),
                $"/reports?tab=low-stock&branchId={branch.Key.BranchId}", P.Inventory.View, branch.Key.BranchId, null, ct);
            created++;
        }
        return created;
    }

    internal static async Task<int> DailySummaryAsync(IServiceProvider services, DateOnly today, CancellationToken ct)
    {
        var reporting = services.GetRequiredService<IReportingQueries>();
        var publisher = services.GetRequiredService<NotificationPublisher>();
        var sales = await reporting.SalesByChannelAsync(today, today, null, ct);
        var revenue = sales.Sum(s => s.Revenue);
        decimal? cost = sales.All(s => s.Cost is not null) ? sales.Sum(s => s.Cost!.Value) : null;
        var lowStock = await reporting.LowStockAsync(null, ct);
        var exceptions = (await reporting.OpenExceptionCountsAsync(ct)).Where(c => c.Open > 0).ToList();

        var facts = new List<(string, string)>
        {
            ("Orders", sales.Sum(s => s.Orders).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Sales (excl. shipping)", EmailTemplates.Rs(revenue)),
        };
        facts.AddRange(sales.Select(s => ($"  {s.Channel}", $"{s.Orders} · {EmailTemplates.Rs(s.Revenue)}")));
        if (cost is { } c)
        {
            facts.Add(("Gross profit (FIFO)", $"{EmailTemplates.Rs(revenue - c)}{(revenue > 0 ? $" ({(revenue - c) / revenue * 100m:0.0}%)" : string.Empty)}"));
        }
        facts.Add(("Low-stock items", lowStock.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var blocks = new List<EmailBlock> { new Facts(facts) };
        blocks.Add(exceptions.Count == 0
            ? new Para("Nothing is waiting in the Exception Center.")
            : new Facts(exceptions.Select(e => (e.Type.Replace('_', ' ').ToLowerInvariant(), e.Open.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList()));
        blocks.Add(new Para("Gross profit is sales minus FIFO product cost; it is not net profit (expenses are not recorded)."));

        await publisher.EmailOwnersAsync($"daily_summary:{today:yyyyMMdd}", "reporting.daily_summary", Category.Info, today.ToString("yyyy-MM-dd"),
            EmailTemplates.Build(publisher.Options.ShopName, $"Daily summary — {today:dd MMM yyyy}", $"Today at {publisher.Options.ShopName}", blocks,
                "Open dashboard", publisher.AdminLink("/")), ct);
        return 1;
    }
}
