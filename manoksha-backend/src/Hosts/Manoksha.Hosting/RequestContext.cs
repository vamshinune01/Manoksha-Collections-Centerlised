using System.Diagnostics;
using Manoksha.Application.Security;
using Microsoft.AspNetCore.Http;

namespace Manoksha.Hosting;

internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    public string CorrelationId =>
        accessor.HttpContext is { } ctx
            ? ctx.Request.Headers[CorrelationHeader].FirstOrDefault() is { Length: > 0 and <= 100 } header ? header : ctx.TraceIdentifier
            : Activity.Current?.TraceId.ToString() ?? "system";
}
