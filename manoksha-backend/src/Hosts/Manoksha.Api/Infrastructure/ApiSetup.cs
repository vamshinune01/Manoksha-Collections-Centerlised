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
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Correlation-Id"] = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? context.TraceIdentifier;
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
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

    private static RateLimitPartition<string> PerIp(HttpContext ctx, int permitPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
}
