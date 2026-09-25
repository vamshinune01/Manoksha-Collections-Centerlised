namespace Manoksha.Modules.Pricing.Application;

public sealed record SetRetailPriceRequest(decimal Price, string Reason);

public sealed record SetProductDiscountRequest(decimal DiscountPct, string Reason);

public sealed record PricingReasonRequest(string Reason);

public sealed record SkuPriceDto(
    Guid SkuId,
    string SkuCode,
    Guid ProductId,
    string ProductName,
    string VariantName,
    string ProductStatus,
    bool AvailableForRetail,
    bool AvailableForReseller,
    decimal? RetailPrice,
    DateTimeOffset? PriceSince,
    decimal? ProductResellerDiscountPct);

public sealed record RetailPriceHistoryDto(Guid Id, decimal Price, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid SetBy, string Reason);

public sealed record ProductDiscountHistoryDto(Guid Id, decimal DiscountPct, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid SetBy, string Reason, string? EndReason);

public sealed record ProductDiscountDto(Guid ProductId, decimal? CurrentDiscountPct, IReadOnlyList<ProductDiscountHistoryDto> History);

public sealed record ResellerCatalogItemDto(
    Guid SkuId,
    string SkuCode,
    Guid ProductId,
    string ProductName,
    string VariantName,
    string CategoryName,
    decimal RetailPrice,
    string DiscountSource,
    decimal DiscountPct,
    decimal ResellerPrice);

public sealed record ResellerCatalogPage(IReadOnlyList<ResellerCatalogItemDto> Items, int Total, int Page, int PageSize);

public sealed record QuoteRequest(IReadOnlyList<Guid> SkuIds);

public sealed record PricePreviewDto(Guid ResellerId, int TermsVersion, decimal ResellerDiscountPct, decimal? ProductDiscountPct, decimal RetailPrice,
    string AppliedSource, decimal AppliedPct, decimal FinalUnitPrice);
