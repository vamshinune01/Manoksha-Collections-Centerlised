namespace Manoksha.Modules.Catalog.Application;

public sealed record CategoryDto(Guid Id, Guid? ParentId, string Name, string Slug, int SortOrder, bool IsActive);

public sealed record PublicCategoryDto(Guid Id, Guid? ParentId, string Name, string Slug);

public sealed record SaveCategoryRequest(Guid? ParentId, string Name, int SortOrder, bool IsActive, string Reason);

public sealed record AttributeOptionDto(Guid Id, string Value, int SortOrder, bool IsActive);

public sealed record AttributeDto(Guid Id, string Code, string Name, bool IsActive, IReadOnlyList<AttributeOptionDto> Options);

public sealed record CreateAttributeRequest(string Code, string Name, IReadOnlyList<string>? Options, string Reason);

public sealed record UpdateAttributeRequest(string Name, bool IsActive, string Reason);

public sealed record AddOptionRequest(string Value, string Reason);

public sealed record SetOptionStatusRequest(bool IsActive, string Reason);

public sealed record CreateProductRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    string TrackingMode,
    IReadOnlyList<Guid>? VariantAttributeIds,
    bool AvailableForRetail,
    bool AvailableForReseller,
    string Reason);

public sealed record UpdateProductRequest(
    Guid CategoryId,
    string Name,
    string? Description,
    string TrackingMode,
    bool AvailableForRetail,
    bool AvailableForReseller,
    string Reason);

public sealed record ChangeStatusRequest(string Status, string Reason);

public sealed record CreateVariantRequest(IReadOnlyList<Guid>? OptionIds, string? SkuCode, bool GenerateBarcode, string Reason);

public sealed record ProductSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    Guid CategoryId,
    string CategoryName,
    string TrackingMode,
    string Status,
    bool AvailableForRetail,
    bool AvailableForReseller,
    int VariantCount,
    DateTimeOffset CreatedAt);

public sealed record ProductPage(IReadOnlyList<ProductSummaryDto> Items, int Total, int Page, int PageSize);

public sealed record VariantValueDto(Guid AttributeId, string AttributeName, Guid OptionId, string Value);

public sealed record BarcodeDto(Guid Id, string Code, string Kind, string Status, Guid? InventoryItemId, DateTimeOffset CreatedAt, int PrintCount, DateTimeOffset? LastPrintedAt, string? RetireReason);

public sealed record VariantDto(Guid Id, string Name, string Status, IReadOnlyList<VariantValueDto> Values, Guid SkuId, string SkuCode, IReadOnlyList<BarcodeDto> Barcodes);

public sealed record ProductDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    string TrackingMode,
    string Status,
    bool AvailableForRetail,
    bool AvailableForReseller,
    IReadOnlyList<AttributeDto> VariantAttributes,
    IReadOnlyList<VariantDto> Variants,
    DateTimeOffset CreatedAt);

public sealed record RegisterBarcodeRequest(string Code, string Reason);

public sealed record RetireBarcodeRequest(string Reason);

public sealed record GenerateBarcodeRequest(string Reason);

public sealed record PrintLabelsRequest(IReadOnlyList<Guid> BarcodeIds, int Copies);

public sealed record LabelDto(Guid BarcodeId, string Code, string Kind, string ProductName, string VariantName, string SkuCode, int Copies, bool IsReprint);

/// <summary>
/// Result of scanning a barcode. Location/status (inventory, Phase 3) and applicable price (pricing, Phase 4) are added by
/// those modules; this module answers "what is this?".
/// </summary>
public sealed record ScanResultDto(
    string Barcode,
    string BarcodeKind,
    Guid? InventoryItemId,
    Guid SkuId,
    string SkuCode,
    Guid VariantId,
    string VariantName,
    IReadOnlyList<VariantValueDto> Attributes,
    Guid ProductId,
    string ProductName,
    string ProductStatus,
    string TrackingMode,
    bool AvailableForRetail,
    bool AvailableForReseller);
