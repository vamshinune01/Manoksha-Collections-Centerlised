using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Manoksha.Application.Http;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Phase 10 penetration-style checks over EVERY endpoint (not samples): nothing is anonymous unless listed below, every route is
/// bound to its token audience, every admin route needs a permission, and real requests from the wrong caller are refused.
/// Also: security headers, trusted client IP and per-client rate limits.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class SecurityHardeningTests(ManokshaApiFactory factory)
{
    /// <summary>The only anonymous routes. Anything else anonymous fails the build.</summary>
    private static readonly string[] AnonymousAllowed =
    [
        "/health/live", "/health/ready",
        "/api/v1/auth/internal/login", "/api/v1/auth/internal/password/first-change", "/api/v1/auth/internal/mfa/verify",
        "/api/v1/auth/internal/mfa/enroll/start", "/api/v1/auth/internal/mfa/enroll/confirm",
        "/api/v1/auth/otp/request", "/api/v1/auth/otp/verify", "/api/v1/auth/customer/register", "/api/v1/auth/refresh", "/api/v1/auth/logout",
        "/api/v1/setup/", "/api/v1/invitations/{token}/", // first-run Owner setup (setup code) and invite links (single-use token)
        "/api/v1/catalog/", // public storefront (read-only)
        "/api/v1/webhooks/payments/{provider}", // signature-verified, then re-checked server-to-server
        "/api/v1/payment-simulator/", "/api/v1/dev-storage/", // development adapters only (refused in Production)
    ];

    /// <summary>Admin routes any signed-in staff member may use for themselves (no permission needed).</summary>
    private static readonly string[] AdminSelfService =
    [
        "/api/v1/admin/me/",
        // Requester OR a source-branch approver (two different permissions) — enforced in TransferService.CancelAsync.
        "/api/v1/admin/inventory/transfers/{id:guid}/cancel",
    ];

    private sealed record Route(string Method, string Pattern, bool Anonymous, IReadOnlyList<string> Policies, bool Form);

    private List<Route> Routes()
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        return source.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } t && (t.StartsWith("/api/", StringComparison.Ordinal) || t.StartsWith("/health", StringComparison.Ordinal)))
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => new Route(m, "/" + e.RoutePattern.RawText!.TrimStart('/'),
                e.Metadata.GetMetadata<IAllowAnonymous>() is not null,
                e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).OfType<string>().ToList(),
                e.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Any(c => c.Contains("form", StringComparison.Ordinal)) == true)))
            .ToList();
    }

    [Fact]
    public void Every_endpoint_is_either_explicitly_public_or_bound_to_an_audience_and_permission()
    {
        var routes = Routes();
        routes.Should().HaveCountGreaterThan(150, "the sweep must see the whole API");

        routes.Where(r => r.Anonymous && !AnonymousAllowed.Any(a => a.EndsWith('/') ? r.Pattern.StartsWith(a, StringComparison.Ordinal) : r.Pattern == a))
            .Select(r => $"{r.Method} {r.Pattern}").Aggregate(string.Empty, (a, b) => a + b + "; ").Should().BeEmpty("only the listed routes may be anonymous");

        foreach (var (prefix, audience) in new[] { ("/api/v1/admin/", Audiences.Admin), ("/api/v1/pos/", Audiences.Pos), ("/api/v1/reseller/", Audiences.Reseller), ("/api/v1/customer/", Audiences.Customer) })
        {
            routes.Where(r => r.Pattern.StartsWith(prefix, StringComparison.Ordinal) && !r.Policies.Any(p => p.StartsWith(AuthorizationPolicyNames.AudiencePrefix, StringComparison.Ordinal)
                    && p[AuthorizationPolicyNames.AudiencePrefix.Length..].Split(',').Contains(audience)))
                .Select(r => $"{r.Method} {r.Pattern}").Aggregate(string.Empty, (a, b) => a + b + "; ").Should().BeEmpty($"{prefix} routes must require a '{audience}' token");
        }

        routes.Where(r => r.Pattern.StartsWith("/api/v1/admin/", StringComparison.Ordinal) && !AdminSelfService.Any(s => r.Pattern.StartsWith(s, StringComparison.Ordinal))
                && !r.Policies.Any(p => p.StartsWith(AuthorizationPolicyNames.PermissionPrefix, StringComparison.Ordinal)))
            .Select(r => $"{r.Method} {r.Pattern}").Aggregate(string.Empty, (a, b) => a + b + "; ").Should().BeEmpty("every admin action needs a permission");
    }

    [Fact]
    public async Task Wrong_callers_are_refused_on_every_protected_endpoint()
    {
        var routes = Routes().Where(r => !r.Anonymous).ToList();
        var anonymous = factory.CreateClient();
        var customer = factory.Authorized((await factory.RegisterCustomerAsync(ApiClient.NewMobile())).AccessToken);
        var reseller = await factory.NewActiveResellerAsync();
        var sales = await factory.UserClientAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar);
        var salesPermissions = SystemRoles.Definitions.Single(d => d.Code == SystemRoles.SalesEmployee).DefaultPermissions.ToHashSet();
        var failures = new List<string>();

        foreach (var r in routes)
        {
            var url = Concrete(r.Pattern);
            await Expect(anonymous, r, url, [HttpStatusCode.Unauthorized], "anonymous", failures);
            if (r.Pattern.StartsWith("/api/v1/admin/", StringComparison.Ordinal) || r.Pattern.StartsWith("/api/v1/pos/", StringComparison.Ordinal))
            {
                await Expect(customer, r, url, [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], "customer token", failures);
                await Expect(reseller, r, url, [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], "reseller token", failures);
            }
            if (r.Pattern.StartsWith("/api/v1/reseller/", StringComparison.Ordinal))
            {
                await Expect(customer, r, url, [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], "customer token", failures);
                await Expect(sales, r, url, [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], "staff token", failures);
            }
            var permission = r.Policies.FirstOrDefault(p => p.StartsWith(AuthorizationPolicyNames.PermissionPrefix, StringComparison.Ordinal))?[AuthorizationPolicyNames.PermissionPrefix.Length..];
            if (r.Pattern.StartsWith("/api/v1/admin/", StringComparison.Ordinal) && permission is not null && !permission.Split('|').Any(salesPermissions.Contains))
            {
                await Expect(sales, r, url, [HttpStatusCode.Forbidden], $"sales employee without {permission}", failures);
            }
        }
        string.Join("; ", failures).Should().BeEmpty();
    }

    private static async Task Expect(HttpClient client, Route r, string url, HttpStatusCode[] allowed, string who, List<string> failures)
    {
        using var request = new HttpRequestMessage(new HttpMethod(r.Method), url);
        if (r.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = r.Form ? new MultipartFormDataContent { { new StringContent("1"), "amount" } } : new StringContent("{}", Encoding.UTF8, "application/json");
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }
        using var response = await client.SendAsync(request);
        if (!allowed.Contains(response.StatusCode))
        {
            failures.Add($"{who}: {r.Method} {r.Pattern} → {(int)response.StatusCode}");
        }
    }

    /// <summary>Fills route parameters with harmless values (random ids never match real data).</summary>
    private static string Concrete(string pattern) => RouteParameter().Replace(pattern, m =>
        m.Groups["catchAll"].Success ? "x/y" : m.Groups["constraint"].Value switch
        {
            "guid" => Guid.NewGuid().ToString(),
            "int" or "long" => "1",
            _ => "x",
        });

    [GeneratedRegex(@"\{(?<catchAll>\*\*)?(?<name>\w+)(:(?<constraint>\w+))?\}")]
    private static partial Regex RouteParameter();

    [Fact]
    public async Task Api_responses_carry_security_headers_and_personal_data_is_never_cached()
    {
        var anonymous = factory.CreateClient();
        var publicCatalog = await anonymous.GetAsync("/api/v1/catalog/categories");
        publicCatalog.StatusCode.Should().Be(HttpStatusCode.OK);
        publicCatalog.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        publicCatalog.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        publicCatalog.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");

        var owner = await factory.OwnerClientAsync();
        var me = await owner.GetAsync("/api/v1/auth/me");
        me.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Client_ip_from_the_web_servers_is_trusted_only_with_the_proxy_key()
    {
        var email = $"ip{Guid.NewGuid():N}"[..12] + "@test.manoksha";
        async Task LoginFrom(string ip, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/internal/login")
            {
                Content = JsonContent.Create(new { email, password = "not-the-password", client = "admin" }),
            };
            request.Headers.Add("X-Manoksha-Client-IP", ip);
            request.Headers.Add("X-Manoksha-Proxy-Key", key);
            await factory.CreateClient().SendAsync(request);
        }

        await LoginFrom("203.0.113.7", ManokshaApiFactory.ProxyKey);
        await LoginFrom("198.51.100.9", "guessed-key");
        var ips = await factory.ColumnAsync("SELECT ip_address FROM identity.login_events WHERE ip_address IN ('203.0.113.7', '198.51.100.9')");
        ips.Should().Contain("203.0.113.7").And.NotContain("198.51.100.9", "a caller without the proxy key cannot choose its IP");
    }

    [Fact]
    public async Task Anonymous_callers_are_rate_limited_per_client_and_get_429()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AnonymousPermitPerMinute", "3"));
        var client = limited.CreateClient();
        HttpRequestMessage From(string ip)
        {
            var m = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog/categories");
            m.Headers.Add("X-Manoksha-Client-IP", ip);
            m.Headers.Add("X-Manoksha-Proxy-Key", ManokshaApiFactory.ProxyKey);
            return m;
        }
        for (var i = 0; i < 3; i++)
        {
            (await client.SendAsync(From("192.0.2.10"))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        var limitedResponse = await client.SendAsync(From("192.0.2.10"));
        limitedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limitedResponse.ErrorCodeAsync()).Should().Be("RATE_LIMITED");
        (await client.SendAsync(From("192.0.2.11"))).StatusCode.Should().Be(HttpStatusCode.OK, "another shopper is not affected");
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK, "health checks are never limited");
    }
}
