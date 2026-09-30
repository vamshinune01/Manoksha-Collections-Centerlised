using System.Globalization;
using System.Net;
using System.Text;

namespace Manoksha.Modules.Notifications.Application;

internal sealed record EmailContent(string Subject, string Html, string Text);

/// <summary>A block of an email: a paragraph, a key/value table or a line-item table.</summary>
internal abstract record EmailBlock;

internal sealed record Para(string Text) : EmailBlock;

internal sealed record Facts(IReadOnlyList<(string Label, string Value)> Rows) : EmailBlock;

internal sealed record Items(IReadOnlyList<(string Name, int Quantity, decimal Amount)> Lines) : EmailBlock;

/// <summary>
/// Plain, mobile-friendly transactional emails (HTML + text). Every dynamic value is HTML-encoded. Templates live in code so the
/// wording is reviewed with the business rules; the provider only delivers them.
/// </summary>
internal static class EmailTemplates
{
    /// <summary>₹ with Indian digit grouping (1,23,456.50). Formatted by hand: the containers run in invariant-globalization mode.</summary>
    public static string Rs(decimal amount)
    {
        var fixedPoint = Math.Abs(Math.Round(amount, 2, MidpointRounding.AwayFromZero)).ToString("0.00", CultureInfo.InvariantCulture);
        var whole = fixedPoint[..^3];
        var grouped = whole.Length <= 3 ? whole : whole[^3..];
        for (var rest = whole.Length <= 3 ? string.Empty : whole[..^3]; rest.Length > 0; rest = rest.Length <= 2 ? string.Empty : rest[..^2])
        {
            grouped = (rest.Length <= 2 ? rest : rest[^2..]) + "," + grouped;
        }
        return (amount < 0 ? "-₹" : "₹") + grouped + fixedPoint[^3..];
    }

    public static EmailContent Build(string shopName, string subject, string heading, IReadOnlyList<EmailBlock> blocks, string? actionLabel = null,
        string? actionUrl = null, string? footer = null)
    {
        var html = new StringBuilder();
        var text = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;background:#f6f4f5;font-family:Arial,Helvetica,sans-serif;color:#1f2937\">");
        html.Append("<div style=\"max-width:560px;margin:0 auto;padding:24px\">");
        html.Append("<div style=\"font-size:18px;font-weight:bold;color:#8f1d3f;margin-bottom:16px\">").Append(E(shopName)).Append("</div>");
        html.Append("<div style=\"background:#ffffff;border-radius:8px;padding:24px\">");
        html.Append("<h1 style=\"font-size:20px;margin:0 0 16px\">").Append(E(heading)).Append("</h1>");
        text.AppendLine(heading).AppendLine();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Para p:
                    html.Append("<p style=\"font-size:14px;line-height:1.5;margin:0 0 12px\">").Append(E(p.Text)).Append("</p>");
                    text.AppendLine(p.Text).AppendLine();
                    break;
                case Facts f:
                    html.Append("<table style=\"width:100%;font-size:14px;border-collapse:collapse;margin:0 0 12px\">");
                    foreach (var (label, value) in f.Rows)
                    {
                        html.Append("<tr><td style=\"padding:4px 8px 4px 0;color:#6b7280;white-space:nowrap\">").Append(E(label))
                            .Append("</td><td style=\"padding:4px 0;font-weight:bold\">").Append(E(value)).Append("</td></tr>");
                        text.Append(label).Append(": ").AppendLine(value);
                    }
                    html.Append("</table>");
                    text.AppendLine();
                    break;
                case Items it:
                    html.Append("<table style=\"width:100%;font-size:14px;border-collapse:collapse;margin:0 0 12px\">");
                    foreach (var (name, qty, amount) in it.Lines)
                    {
                        html.Append("<tr><td style=\"padding:6px 0;border-bottom:1px solid #eee\">").Append(E(name)).Append(" × ").Append(qty)
                            .Append("</td><td style=\"padding:6px 0;border-bottom:1px solid #eee;text-align:right\">").Append(E(Rs(amount))).Append("</td></tr>");
                        text.Append("- ").Append(name).Append(" x ").Append(qty).Append("  ").AppendLine(Rs(amount));
                    }
                    html.Append("</table>");
                    text.AppendLine();
                    break;
            }
        }
        if (actionUrl is not null && actionLabel is not null)
        {
            html.Append("<p style=\"margin:20px 0 4px\"><a href=\"").Append(E(actionUrl))
                .Append("\" style=\"background:#8f1d3f;color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:6px;font-size:14px\">")
                .Append(E(actionLabel)).Append("</a></p>");
            text.Append(actionLabel).Append(": ").AppendLine(actionUrl).AppendLine();
        }
        html.Append("</div>");
        var foot = footer ?? $"This is an automatic message from {shopName}. Please do not reply to this email.";
        html.Append("<p style=\"font-size:12px;color:#6b7280;margin:16px 0 0\">").Append(E(foot)).Append("</p></div></body></html>");
        text.AppendLine("--").AppendLine(foot);
        return new EmailContent(subject, html.ToString(), text.ToString());
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
