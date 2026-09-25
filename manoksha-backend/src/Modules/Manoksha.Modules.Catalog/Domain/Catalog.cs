using System.Text;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Catalog.Domain;

internal enum TrackingMode
{
    /// <summary>Each piece has its own identity/barcode (unique or high-value items).</summary>
    Serialized = 1,

    /// <summary>Counted by quantity (high-volume SKUs).</summary>
    Quantity = 2,
}

internal enum ProductStatus
{
    Draft = 1,
    Active = 2,
    Inactive = 3,
}

internal enum RecordStatus
{
    Active = 1,
    Inactive = 2,
}

internal sealed class Category : Entity
{
    private Category()
    {
    }

    public Category(Guid? parentId, string name, int sortOrder)
    {
        ParentId = parentId;
        Name = name.Trim();
        Slug = Slugs.Create(Name, Id);
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid? ParentId { get; private set; }

    public string Name { get; private set; } = default!;

    public string Slug { get; private set; } = default!;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(Guid? parentId, string name, int sortOrder, bool isActive)
    {
        if (parentId == Id)
        {
            throw new BusinessRuleException("CATEGORY_PARENT_INVALID", "A category cannot be its own parent.", 400);
        }
        ParentId = parentId;
        Name = name.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>A configurable variant attribute (Colour, Size, Design, Fabric, …) — not hard-coded (SPEC §7).</summary>
internal sealed class AttributeDefinition : Entity
{
    private readonly List<AttributeOption> _options = [];

    private AttributeDefinition()
    {
    }

    public AttributeDefinition(string code, string name)
    {
        Code = code;
        Name = name.Trim();
        IsActive = true;
    }

    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<AttributeOption> Options => _options;

    public AttributeOption AddOption(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new BusinessRuleException("ATTRIBUTE_OPTION_REQUIRED", "Option value is required.", 400);
        }
        if (_options.Exists(o => string.Equals(o.Value, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("ATTRIBUTE_OPTION_EXISTS", $"'{trimmed}' already exists for {Name}.");
        }
        var option = new AttributeOption(Id, trimmed, _options.Count + 1);
        _options.Add(option);
        return option;
    }

    public void Update(string name, bool isActive)
    {
        Name = name.Trim();
        IsActive = isActive;
    }
}

internal sealed class AttributeOption : Entity
{
    private AttributeOption()
    {
    }

    public AttributeOption(Guid attributeId, string value, int sortOrder)
    {
        AttributeId = attributeId;
        Value = value;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid AttributeId { get; private set; }

    public string Value { get; private set; } = default!;

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public void SetActive(bool active) => IsActive = active;
}

internal sealed class Product : Entity
{
    private readonly List<ProductVariantAttribute> _variantAttributes = [];

    private Product()
    {
    }

    public Product(Guid categoryId, string name, string? description, TrackingMode trackingMode, IReadOnlyList<Guid> variantAttributeIds,
        bool availableForRetail, bool availableForReseller, DateTimeOffset now)
    {
        CategoryId = categoryId;
        Name = name.Trim();
        Slug = Slugs.Create(Name, Id);
        Description = description?.Trim();
        TrackingMode = trackingMode;
        Status = ProductStatus.Draft;
        AvailableForRetail = availableForRetail;
        AvailableForReseller = availableForReseller;
        CreatedAt = now;
        if (variantAttributeIds.Distinct().Count() != variantAttributeIds.Count)
        {
            throw new BusinessRuleException("VARIANT_ATTRIBUTE_DUPLICATE", "Each variant attribute may be used once.", 400);
        }
        for (var i = 0; i < variantAttributeIds.Count; i++)
        {
            _variantAttributes.Add(new ProductVariantAttribute(Id, variantAttributeIds[i], i + 1));
        }
    }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = default!;

    public string Slug { get; private set; } = default!;

    public string? Description { get; private set; }

    public TrackingMode TrackingMode { get; private set; }

    public ProductStatus Status { get; private set; }

    /// <summary>May be sold to direct retail customers (online storefront / POS).</summary>
    public bool AvailableForRetail { get; private set; }

    /// <summary>May appear in and be ordered from the reseller catalog (ADR-001 §6).</summary>
    public bool AvailableForReseller { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyList<ProductVariantAttribute> VariantAttributes => _variantAttributes.OrderBy(a => a.Position).ToList();

    public void Update(Guid categoryId, string name, string? description, bool availableForRetail, bool availableForReseller)
    {
        CategoryId = categoryId;
        Name = name.Trim();
        Description = description?.Trim();
        AvailableForRetail = availableForRetail;
        AvailableForReseller = availableForReseller;
    }

    /// <summary>Tracking mode is fixed once the product has been activated (stock may exist from then on).</summary>
    public void ChangeTrackingMode(TrackingMode mode)
    {
        if (Status != ProductStatus.Draft)
        {
            throw new BusinessRuleException("TRACKING_MODE_LOCKED", "Tracking mode can only be changed while the product is a draft.");
        }
        TrackingMode = mode;
    }

    public void ChangeStatus(ProductStatus status, int activeVariantCount)
    {
        if (status == ProductStatus.Draft && Status != ProductStatus.Draft)
        {
            throw new BusinessRuleException("PRODUCT_STATUS_INVALID", "An activated product cannot return to draft; make it inactive instead.");
        }
        if (status == ProductStatus.Active && activeVariantCount == 0)
        {
            throw new BusinessRuleException("PRODUCT_HAS_NO_VARIANTS", "Add at least one active variant (SKU) before activating the product.");
        }
        Status = status;
    }
}

internal sealed class ProductVariantAttribute
{
    private ProductVariantAttribute()
    {
    }

    public ProductVariantAttribute(Guid productId, Guid attributeId, int position)
    {
        ProductId = productId;
        AttributeId = attributeId;
        Position = position;
    }

    public Guid ProductId { get; private set; }

    public Guid AttributeId { get; private set; }

    public int Position { get; private set; }
}

/// <summary>A sellable attribute combination of a product (e.g. Red / M). Exactly one SKU per variant.</summary>
internal sealed class Variant : Entity
{
    private readonly List<VariantAttributeValue> _values = [];

    private Variant()
    {
    }

    public Variant(Guid productId, string name, IReadOnlyList<(Guid AttributeId, Guid OptionId)> values, DateTimeOffset now)
    {
        ProductId = productId;
        Name = name;
        CombinationKey = values.Count == 0 ? "standard" : string.Join('|', values.Select(v => v.OptionId.ToString("N")).Order(StringComparer.Ordinal));
        Status = RecordStatus.Active;
        CreatedAt = now;
        foreach (var (attributeId, optionId) in values)
        {
            _values.Add(new VariantAttributeValue(Id, attributeId, optionId));
        }
    }

    public Guid ProductId { get; private set; }

    public string Name { get; private set; } = default!;

    /// <summary>Sorted option ids; unique per product so the same combination cannot be created twice.</summary>
    public string CombinationKey { get; private set; } = default!;

    public RecordStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<VariantAttributeValue> Values => _values;

    public void SetStatus(RecordStatus status) => Status = status;
}

internal sealed class VariantAttributeValue
{
    private VariantAttributeValue()
    {
    }

    public VariantAttributeValue(Guid variantId, Guid attributeId, Guid optionId)
    {
        VariantId = variantId;
        AttributeId = attributeId;
        OptionId = optionId;
    }

    public Guid VariantId { get; private set; }

    public Guid AttributeId { get; private set; }

    public Guid OptionId { get; private set; }
}

/// <summary>Business identifier of a sellable variant. Pricing and inventory attach to the SKU.</summary>
internal sealed class Sku : Entity
{
    private Sku()
    {
    }

    public Sku(Guid variantId, Guid productId, string code, DateTimeOffset now)
    {
        VariantId = variantId;
        ProductId = productId;
        Code = code;
        CreatedAt = now;
    }

    public Guid VariantId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Code { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }
}

internal enum BarcodeKind
{
    /// <summary>System-generated EAN-13 in the in-store 29 range.</summary>
    InternalEan13 = 1,

    /// <summary>Supplier/manufacturer code registered against the SKU.</summary>
    External = 2,
}

/// <summary>
/// A scannable code mapped to a SKU and, for serialized stock (Phase 3), to one inventory item. Codes are never reused,
/// and reprinting a label never creates a new barcode or inventory identity (SPEC §7).
/// </summary>
internal sealed class Barcode : Entity
{
    private Barcode()
    {
    }

    public Barcode(string code, Guid skuId, Guid? inventoryItemId, BarcodeKind kind, Guid? createdBy, DateTimeOffset now)
    {
        Code = code;
        SkuId = skuId;
        InventoryItemId = inventoryItemId;
        Kind = kind;
        Status = RecordStatus.Active;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public string Code { get; private set; } = default!;

    public Guid SkuId { get; private set; }

    public Guid? InventoryItemId { get; private set; }

    public BarcodeKind Kind { get; private set; }

    public RecordStatus Status { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public Guid? RetiredBy { get; private set; }

    public string? RetireReason { get; private set; }

    public void Retire(Guid by, string reason, DateTimeOffset now)
    {
        if (Status == RecordStatus.Inactive)
        {
            throw new BusinessRuleException("BARCODE_ALREADY_RETIRED", "This barcode is already retired.");
        }
        Status = RecordStatus.Inactive;
        RetiredBy = by;
        RetiredAt = now;
        RetireReason = reason;
    }
}

/// <summary>Append-only label print log. A second print of the same barcode is a reprint — same identity.</summary>
internal sealed class BarcodePrint : Entity
{
    private BarcodePrint()
    {
    }

    public BarcodePrint(Guid barcodeId, int copies, bool isReprint, Guid? printedBy, DateTimeOffset now)
    {
        BarcodeId = barcodeId;
        Copies = copies;
        IsReprint = isReprint;
        PrintedBy = printedBy;
        PrintedAt = now;
    }

    public Guid BarcodeId { get; private set; }

    public int Copies { get; private set; }

    public bool IsReprint { get; private set; }

    public Guid? PrintedBy { get; private set; }

    public DateTimeOffset PrintedAt { get; private set; }
}

internal static class Slugs
{
    public static string Create(string name, Guid id)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }
        var baseSlug = sb.ToString().Trim('-');
        if (baseSlug.Length > 80)
        {
            baseSlug = baseSlug[..80].Trim('-');
        }
        var suffix = id.ToString("N")[^6..];
        return baseSlug.Length == 0 ? suffix : $"{baseSlug}-{suffix}";
    }
}
