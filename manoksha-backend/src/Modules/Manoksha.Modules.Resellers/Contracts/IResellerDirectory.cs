namespace Manoksha.Modules.Resellers.Contracts;

/// <param name="CanTransact">True only for ACTIVE resellers (orders, deposits). FROZEN/CLOSED are read-only (ADR-001 §17).</param>
public sealed record ResellerInfo(Guid ResellerId, string ResellerNumber, Guid UserId, string Status, string ContactName, string? BusinessName, bool CanTransact);

/// <param name="TermId">Immutable commercial-term version id — snapshotted on order lines.</param>
public sealed record ResellerTerms(Guid ResellerId, Guid TermId, int Version, decimal DiscountPct);

public interface IResellerDirectory
{
    Task<ResellerInfo?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<ResellerInfo?> FindAsync(Guid resellerId, CancellationToken cancellationToken = default);

    Task<ResellerTerms> GetCurrentTermsAsync(Guid resellerId, CancellationToken cancellationToken = default);
}
