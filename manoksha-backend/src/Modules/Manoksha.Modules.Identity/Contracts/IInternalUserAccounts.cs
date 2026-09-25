namespace Manoksha.Modules.Identity.Contracts;

public sealed record NewInternalAccount(Guid UserId, string TemporaryPassword);

public sealed record InternalAccountInfo(Guid UserId, string DisplayName, string? Email, string Status);

/// <summary>
/// Lets other modules (Employees) create and look up internal login accounts. Creating an account never grants a role:
/// role and permission changes remain Owner-only (SPEC §26).
/// </summary>
public interface IInternalUserAccounts
{
    /// <summary>Creates an internal account with a one-time temporary password (must be changed at first sign-in).</summary>
    Task<NewInternalAccount> CreateAsync(string email, string displayName, string? mobile, string reason, CancellationToken cancellationToken = default);

    Task<InternalAccountInfo?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<InternalAccountInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
}
