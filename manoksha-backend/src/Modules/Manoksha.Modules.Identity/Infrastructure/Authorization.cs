using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Manoksha.Modules.Identity.Infrastructure;

internal sealed class AudienceRequirement(IReadOnlySet<string> audiences) : IAuthorizationRequirement
{
    public IReadOnlySet<string> Audiences { get; } = audiences;
}

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

internal sealed class AudienceHandler : AuthorizationHandler<AudienceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AudienceRequirement requirement)
    {
        var aud = context.User.FindFirst(ManokshaClaimTypes.Audience)?.Value;
        if (aud is not null && requirement.Audiences.Contains(aud))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Backend permission enforcement; UI visibility is never the security boundary (SPEC §2).</summary>
internal sealed class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.FindFirst(ManokshaClaimTypes.AccountType)?.Value != nameof(AccountType.Internal))
        {
            return;
        }
        if (await permissions.HasPermissionAsync(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Builds "perm:{code}" and "aud:{a,b}" policies on demand.</summary>
internal sealed class ManokshaPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(AuthorizationPolicyNames.PermissionPrefix, StringComparison.Ordinal))
        {
            var code = policyName[AuthorizationPolicyNames.PermissionPrefix.Length..];
            if (!Manoksha.Application.Security.Permissions.Exists(code))
            {
                throw new InvalidOperationException($"Endpoint requires unknown permission '{code}'.");
            }
            return new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(code))
                .Build();
        }
        if (policyName.StartsWith(AuthorizationPolicyNames.AudiencePrefix, StringComparison.Ordinal))
        {
            var audiences = policyName[AuthorizationPolicyNames.AudiencePrefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            return new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new AudienceRequirement(audiences))
                .Build();
        }
        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>
/// Runs after signature/lifetime validation on every request: the user must still be active, the security
/// stamp current and the session unrevoked — so freezes, role changes and logouts take effect immediately.
/// </summary>
internal static class SessionValidation
{
    public static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirst(ManokshaClaimTypes.Subject)?.Value, out var userId)
            || !Guid.TryParse(principal.FindFirst(ManokshaClaimTypes.SessionId)?.Value, out var sessionId))
        {
            context.Fail("invalid_claims");
            return;
        }
        var stamp = principal.FindFirst(ManokshaClaimTypes.SecurityStamp)?.Value;
        var accountType = principal.FindFirst(ManokshaClaimTypes.AccountType)?.Value;
        var audience = principal.FindFirst(ManokshaClaimTypes.Audience)?.Value;
        var now = context.HttpContext.RequestServices.GetRequiredService<IClock>().UtcNow;

        var db = context.HttpContext.RequestServices.GetRequiredService<ManokshaDbContext>();
        var state = await (
            from u in db.Set<User>()
            join s in db.Set<AuthSession>() on u.Id equals s.UserId
            where u.Id == userId && s.Id == sessionId
            select new { u.Status, u.SecurityStamp, u.AccountType, s.RevokedAt, s.ExpiresAt, s.Audience })
            .AsNoTracking()
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (state is null
            || state.Status != UserStatus.Active
            || !string.Equals(state.SecurityStamp, stamp, StringComparison.Ordinal)
            || state.AccountType.ToString() != accountType
            || state.RevokedAt is not null
            || state.ExpiresAt <= now
            || !string.Equals(state.Audience, audience, StringComparison.Ordinal))
        {
            context.Fail("session_invalid");
        }
    }
}
