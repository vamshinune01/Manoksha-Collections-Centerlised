namespace Manoksha.Modules.Identity.Contracts;

/// <param name="AccountType">Internal, Customer or Reseller.</param>
public sealed record UserContact(Guid UserId, string DisplayName, string? Email, string AccountType, bool IsActive, bool IsOwner);

/// <summary>Who to notify and how to reach them (Notifications module).</summary>
public interface IUserDirectory
{
    Task<UserContact?> GetContactAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Active Owner accounts (CRITICAL alerts and the daily summary are emailed to them).</summary>
    Task<IReadOnlyList<UserContact>> ListOwnersAsync(CancellationToken cancellationToken = default);
}
