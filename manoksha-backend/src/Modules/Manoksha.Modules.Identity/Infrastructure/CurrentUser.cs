using Manoksha.Application.Security;
using Microsoft.AspNetCore.Http;

namespace Manoksha.Modules.Identity.Infrastructure;

/// <summary>
/// Reads the validated principal. Outside an HTTP request (Worker, CLI) the actor is the SYSTEM.
/// Account id, audience and session always come from the validated token — never from route or body.
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true && UserIdOrNull is not null;

    public Guid UserId => UserIdOrNull ?? throw new InvalidOperationException("No authenticated user.");

    public Guid? UserIdOrNull =>
        Context?.User.Identity?.IsAuthenticated == true && Guid.TryParse(Context.User.FindFirst(ManokshaClaimTypes.Subject)?.Value, out var id)
            ? id
            : null;

    public AccountType? AccountType =>
        IsAuthenticated && Enum.TryParse<AccountType>(Context!.User.FindFirst(ManokshaClaimTypes.AccountType)?.Value, out var t) ? t : null;

    public string? Audience => IsAuthenticated ? Context!.User.FindFirst(ManokshaClaimTypes.Audience)?.Value : null;

    public Guid? SessionId =>
        IsAuthenticated && Guid.TryParse(Context!.User.FindFirst(ManokshaClaimTypes.SessionId)?.Value, out var sid) ? sid : null;

    public bool IsSystem => Context is null;
}
