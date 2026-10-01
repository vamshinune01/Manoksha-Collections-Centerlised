using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Manoksha.Api.Infrastructure;

/// <summary>
/// Browsers reach the API through the admin and customer web servers, so the TCP peer is the web server. The web servers send the
/// real client IP in <see cref="ClientIpHeader"/> together with a shared secret (<c>Security:ProxyKey</c>, Secret Manager). Only when
/// the secret matches is the claimed IP used for rate limits, login history and audit records; otherwise both headers are dropped,
/// so a caller can never choose its own IP.
/// </summary>
internal static class TrustedClientIp
{
    public const string ClientIpHeader = "X-Manoksha-Client-IP";
    public const string ProxyKeyHeader = "X-Manoksha-Proxy-Key";

    public static WebApplication UseTrustedClientIp(this WebApplication app, string? proxyKey)
    {
        var expected = string.IsNullOrEmpty(proxyKey) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(proxyKey));
        app.Use((context, next) =>
        {
            var headers = context.Request.Headers;
            var claimed = headers[ClientIpHeader].ToString();
            var presented = headers[ProxyKeyHeader].ToString();
            headers.Remove(ClientIpHeader);
            headers.Remove(ProxyKeyHeader);
            if (expected is not null && claimed.Length > 0 && presented.Length > 0
                && CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(presented)))
                && IPAddress.TryParse(claimed, out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }
            return next(context);
        });
        return app;
    }
}
