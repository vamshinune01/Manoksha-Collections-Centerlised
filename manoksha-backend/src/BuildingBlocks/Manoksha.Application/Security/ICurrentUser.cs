namespace Manoksha.Application.Security;

/// <summary>The authenticated principal for the current request (or the system actor in background jobs).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Authenticated user id. Throws if unauthenticated.</summary>
    Guid UserId { get; }

    Guid? UserIdOrNull { get; }

    AccountType? AccountType { get; }

    string? Audience { get; }

    Guid? SessionId { get; }

    bool IsSystem { get; }
}

/// <summary>Where/how the request arrived; used by audit.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }

    string CorrelationId { get; }
}
