using System.Threading.RateLimiting;
using Manoksha.Application.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

namespace Manoksha.Api.Infrastructure;

internal static class ApiSetup
{
    public static IServiceCollection AddManokshaApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // Cloud Run / Google load balancer terminate TLS and set X-Forwarded-*.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
        });

        var authPerMinute = configuration.GetValue("RateLimiting:AuthPermitPerMinute", 20);
        var otpPerMinute = configuration.GetValue("RateLimiting:OtpPermitPerMinute", 10);
        var userPerMinute = configuration.GetValue("RateLimiting:UserPermitPerMinute", 600);
        var anonymousPerMinute = configuration.GetValue("RateLimiting:AnonymousPermitPerMinute", 300);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Every request: per signed-in user (sub), else per client IP. Health checks and provider webhooks are exempt.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var path = ctx.Request.Path;
                if (path.StartsWithSegments("/health") || path.StartsWithSegments("/api/v1/webhooks"))
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }
                var sub = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.FindFirst("sub")?.Value : null;
                return sub is not null
                    ? FixedWindow("u:" + sub, userPerMinute)
                    : FixedWindow("ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"), anonymousPerMinute);
            });
            o.AddPolicy(RateLimitPolicies.Auth, ctx => PerIp(ctx, authPerMinute));
            o.AddPolicy(RateLimitPolicies.Otp, ctx => PerIp(ctx, otpPerMinute));
            o.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/429",
                    title = "Too many requests. Please slow down.",
                    status = 429,
                    code = "RATE_LIMITED",
                }, ct);
            };
        });

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Large media goes straight to Cloud Storage; API bodies are small (deposit proofs ≤ 5 MB).
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(k => k.Limits.MaxRequestBodySize = 10L * 1024 * 1024);
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(f => f.MultipartBodyLengthLimit = 10L * 1024 * 1024);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = "Manoksha Collections API", Version = "v1" });
            o.CustomSchemaIds(t => t.FullName!.Replace("+", ".", StringComparison.Ordinal).Split('.').Last());
            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [],
            });
        });
        return services;
    }

    public static WebApplication UseManokshaApi(this WebApplication app)
    {
        var proxyKey = app.Configuration["Security:ProxyKey"];
        if (app.Environment.IsProduction() && (proxyKey is null || proxyKey.Length < 32))
        {
            throw new InvalidOperationException("Security:ProxyKey (≥ 32 characters, shared with the web apps) is required in Production.");
        }
        app.UseForwardedHeaders();
        app.UseTrustedClientIp(proxyKey);
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Correlation-Id"] = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? context.TraceIdentifier;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";
            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/v1/payment-simulator"))
            {
                // JSON only: nothing here may be framed, run scripts or be cached by shared caches (personal data, tokens).
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                if (!(HttpMethods.IsGet(context.Request.Method) && path.StartsWithSegments("/api/v1/catalog")))
                {
                    headers.CacheControl = "no-store";
                }
            }
            await next();
        });

        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
        if (!app.Environment.IsProduction())
        {
            // Swagger endpoints are served by middleware; nothing else is anonymous by default.
        }
        return app;
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
