namespace Manoksha.Modules.Catalog.Contracts;

public sealed record SkuInfo(
    Guid SkuId,
    string SkuCode,
    Guid VariantId,
    string VariantName,
    bool VariantActive,
    Guid ProductId,
    string ProductName,
    string TrackingMode,
    string ProductStatus,
    bool AvailableForRetail,
    bool AvailableForReseller);

/// <summary>Catalog facts other modules need (inventory, pricing, orders).</summary>
public interface ICatalogLookup
{
    Task<SkuInfo?> FindSkuAsync(Guid skuId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, SkuInfo>> FindSkusAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default);
}
