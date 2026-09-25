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

    /// <summary>Search by product name, SKU code or barcode; or list a product's SKUs.</summary>
    Task<IReadOnlyList<SkuInfo>> SearchSkusAsync(string? query, Guid? productId, int limit, CancellationToken cancellationToken = default);

    Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, SkuInfo>> FindSkusAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sellable SKUs for a channel: active product, active variant, and available for resellers (reseller channel) or for
    /// retail (retail channel). Paged, optionally filtered by name / SKU code / category.
    /// </summary>
    Task<SellableSkuPage> ListSellableSkusAsync(SalesChannel channel, string? query, Guid? categoryId, int page, int pageSize, CancellationToken cancellationToken = default);
}

public enum SalesChannel
{
    Retail = 1,
    Reseller = 2,
}

public sealed record SellableSku(SkuInfo Sku, Guid CategoryId, string CategoryName);

public sealed record SellableSkuPage(IReadOnlyList<SellableSku> Items, int Total, int Page, int PageSize);

public sealed record AttributeValue(string Attribute, string Value);

/// <summary>What a scanned code refers to (catalog facts only; inventory adds location/status).</summary>
public sealed record CatalogBarcode(string Code, string Kind, bool IsActive, Guid? InventoryItemId, SkuInfo Sku, IReadOnlyList<AttributeValue> Attributes);

public sealed record IssuedBarcode(Guid BarcodeId, string Code);

/// <summary>Issues the unique barcode of one serialized inventory item (called by Inventory at goods receipt).</summary>
public interface IItemBarcodeIssuer
{
    Task<IssuedBarcode> IssueForItemAsync(Guid skuId, Guid inventoryItemId, CancellationToken cancellationToken = default);
}

public interface ICatalogBarcodes
{
    Task<CatalogBarcode?> FindAsync(string code, CancellationToken cancellationToken = default);
}
