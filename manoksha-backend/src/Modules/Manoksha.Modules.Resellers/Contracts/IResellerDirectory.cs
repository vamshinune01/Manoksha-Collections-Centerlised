namespace Manoksha.Modules.Resellers.Contracts;

/// <param name="CanTransact">True only for ACTIVE resellers (orders, deposits). FROZEN/CLOSED are read-only (ADR-001 §17).</param>
public sealed record ResellerInfo(Guid ResellerId, string ResellerNumber, Guid UserId, string Status, string ContactName, string? BusinessName, bool CanTransact);

/// <param name="TermId">Immutable commercial-term version id — snapshotted on order lines.</param>
/// <param name="VendorDiscounts">The reseller's % per vendor (ADR-001 §45); a vendor that is absent means 0%.</param>
public sealed record ResellerTerms(Guid ResellerId, Guid TermId, int Version, decimal DiscountPct, IReadOnlyDictionary<Guid, decimal>? VendorDiscounts = null);

public interface IResellerDirectory
{
    Task<ResellerInfo?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ResellerInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default);

    Task<ResellerTerms> GetCurrentTermsAsync(Guid resellerId, CancellationToken cancellationToken = default);

    /// <summary>Contact details for notifications (never shown to other resellers).</summary>
    Task<ResellerContact?> GetContactAsync(Guid resellerId, CancellationToken cancellationToken = default);
}

public sealed record ResellerContact(Guid ResellerId, string ResellerNumber, string ContactName, string? BusinessName, string? Email, string Status);

/// <summary>
/// Modules that must set something up for every new reseller (the Wallet module opens the ₹0 wallet). Called inside the
/// onboarding transaction; <see cref="IsReadyAsync"/> is part of the activation eligibility check.
/// </summary>
public interface IResellerOnboardingParticipant
{
    Task OnResellerCreatedAsync(Guid resellerId, CancellationToken cancellationToken = default);

    Task<bool> IsReadyAsync(Guid resellerId, CancellationToken cancellationToken = default);
}

/// <summary>Read-only wallet balance for reseller screens (implemented by the Wallet module).</summary>
public interface IResellerBalanceView
{
    Task<decimal?> GetBalanceAsync(Guid resellerId, CancellationToken cancellationToken = default);
}
