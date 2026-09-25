using System.Text.RegularExpressions;
using Manoksha.Application.Abstractions;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Catalog.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Manoksha.Modules.Catalog.Application.CatalogSetupService;

namespace Manoksha.Modules.Catalog.Application;

internal sealed partial class ProductService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IAuditWriter audit,
    IClock clock) : ICatalogLookup
{
    public async Task<ProductPage> SearchAsync(string? q, Guid? categoryId, string? status, int? page, int? pageSize, CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var number = Math.Max(1, page ?? 1);
        var products = db.Set<Product>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var pattern = $"%{term.Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            var skuTerm = term.ToUpperInvariant();
            var bySku = db.Set<Sku>().Where(s => s.Code == skuTerm).Select(s => s.ProductId);
            var byBarcode = from b in db.Set<Barcode>() join s in db.Set<Sku>() on b.SkuId equals s.Id where b.Code == term select s.ProductId;
            products = products.Where(p => EF.Functions.ILike(p.Name, pattern) || bySku.Contains(p.Id) || byBarcode.Contains(p.Id));
        }
        if (categoryId is { } c)
        {
            products = products.Where(p => p.CategoryId == c);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ProductStatus>(status, true, out var st))
        {
            products = products.Where(p => p.Status == st);
        }

        var total = await products.CountAsync(ct);
        var rows = await (
            from p in products
            join cat in db.Set<Category>() on p.CategoryId equals cat.Id
            orderby p.CreatedAt descending
            select new
            {
                p.Id, p.Name, p.Slug, p.CategoryId, CategoryName = cat.Name, p.TrackingMode, p.Status, p.AvailableForRetail, p.AvailableForReseller, p.CreatedAt,
                VariantCount = db.Set<Variant>().Count(v => v.ProductId == p.Id),
            })
            .Skip((number - 1) * size).Take(size).ToListAsync(ct);

        return new ProductPage(
            rows.Select(r => new ProductSummaryDto(r.Id, r.Name, r.Slug, r.CategoryId, r.CategoryName, r.TrackingMode.ToString(), r.Status.ToString(),
                r.AvailableForRetail, r.AvailableForReseller, r.VariantCount, r.CreatedAt)).ToList(),
            total, number, size);
    }

    public async Task<ProductDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Set<Product>().AsNoTracking().Include("_variantAttributes").SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var category = await db.Set<Category>().AsNoTracking().SingleAsync(c => c.Id == p.CategoryId, ct);
        var attributeIds = p.VariantAttributes.Select(a => a.AttributeId).ToList();
        var attributes = await db.Set<AttributeDefinition>().AsNoTracking().Include(a => a.Options).Where(a => attributeIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var variants = await db.Set<Variant>().AsNoTracking().Include(v => v.Values).Where(v => v.ProductId == id).OrderBy(v => v.CreatedAt).ToListAsync(ct);
        var skus = await db.Set<Sku>().AsNoTracking().Where(s => s.ProductId == id).ToDictionaryAsync(s => s.VariantId, ct);
        var skuIds = skus.Values.Select(s => s.Id).ToList();
        var barcodes = await db.Set<Barcode>().AsNoTracking().Where(b => skuIds.Contains(b.SkuId)).OrderBy(b => b.CreatedAt).ToListAsync(ct);
        var barcodeIds = barcodes.Select(b => b.Id).ToList();
        var prints = await db.Set<BarcodePrint>().AsNoTracking().Where(x => barcodeIds.Contains(x.BarcodeId))
            .GroupBy(x => x.BarcodeId).Select(g => new { g.Key, Count = g.Count(), Last = g.Max(x => x.PrintedAt) }).ToDictionaryAsync(x => x.Key, ct);
        var optionLookup = attributes.Values.SelectMany(a => a.Options.Select(o => (a, o))).ToDictionary(x => x.o.Id);

        return new ProductDetailDto(
            p.Id, p.Name, p.Slug, p.Description, p.CategoryId, category.Name, p.TrackingMode.ToString(), p.Status.ToString(),
            p.AvailableForRetail, p.AvailableForReseller,
            p.VariantAttributes.Select(va => ToDto(attributes[va.AttributeId])).ToList(),
            variants.Select(v =>
            {
                var sku = skus[v.Id];
                return new VariantDto(v.Id, v.Name, v.Status.ToString(),
                    v.Values.Select(x => new VariantValueDto(x.AttributeId, optionLookup[x.OptionId].a.Name, x.OptionId, optionLookup[x.OptionId].o.Value)).ToList(),
                    sku.Id, sku.Code,
                    barcodes.Where(b => b.SkuId == sku.Id).Select(b => new BarcodeDto(b.Id, b.Code, b.Kind.ToString(), b.Status.ToString(), b.InventoryItemId, b.CreatedAt,
                        prints.TryGetValue(b.Id, out var pr) ? pr.Count : 0, prints.TryGetValue(b.Id, out var pr2) ? pr2.Last : null, b.RetireReason)).ToList());
            }).ToList(),
            p.CreatedAt);
    }

    public Task<ProductDetailDto> CreateAsync(CreateProductRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        RequireName(r.Name);
        var mode = ParseTrackingMode(r.TrackingMode);
        var attributeIds = r.VariantAttributeIds ?? [];
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureActiveCategoryAsync(r.CategoryId, innerCt);
            var active = await db.Set<AttributeDefinition>().Where(a => attributeIds.Contains(a.Id) && a.IsActive).CountAsync(innerCt);
            if (active != attributeIds.Distinct().Count())
            {
                throw new BusinessRuleException("VARIANT_ATTRIBUTE_INVALID", "Every variant attribute must exist and be active.", 400);
            }
            var product = new Product(r.CategoryId, r.Name, r.Description, mode, attributeIds, r.AvailableForRetail, r.AvailableForReseller, clock.UtcNow);
            db.Add(product);
            await audit.RecordAsync(new AuditRecord("catalog.product.created", "Product", product.Id.ToString(),
                After: new { product.Name, product.CategoryId, trackingMode = mode.ToString(), attributeIds, product.AvailableForRetail, product.AvailableForReseller },
                Reason: r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await GetAsync(product.Id, innerCt);
        }, ct);
    }

    public Task<ProductDetailDto> UpdateAsync(Guid id, UpdateProductRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        RequireName(r.Name);
        var mode = ParseTrackingMode(r.TrackingMode);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var p = await db.Set<Product>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            await EnsureActiveCategoryAsync(r.CategoryId, innerCt, allowInactive: r.CategoryId == p.CategoryId);
            var before = Snapshot(p);
            if (mode != p.TrackingMode)
            {
                p.ChangeTrackingMode(mode);
            }
            p.Update(r.CategoryId, r.Name, r.Description, r.AvailableForRetail, r.AvailableForReseller);
            await audit.RecordAsync(new AuditRecord("catalog.product.updated", "Product", id.ToString(), before, Snapshot(p), r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await GetAsync(id, innerCt);
        }, ct);
    }

    public Task<ProductDetailDto> ChangeStatusAsync(Guid id, ChangeStatusRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        if (!Enum.TryParse<ProductStatus>(r.Status, true, out var status))
        {
            throw new BusinessRuleException("PRODUCT_STATUS_INVALID", "Status must be Draft, Active or Inactive.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var p = await db.Set<Product>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            var activeVariants = await db.Set<Variant>().CountAsync(v => v.ProductId == id && v.Status == RecordStatus.Active, innerCt);
            var before = p.Status;
            p.ChangeStatus(status, activeVariants);
            await audit.RecordAsync(new AuditRecord("catalog.product.status_changed", "Product", id.ToString(),
                new { status = before.ToString() }, new { status = status.ToString() }, r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await GetAsync(id, innerCt);
        }, ct);
    }

    public Task<ProductDetailDto> AddVariantAsync(Guid productId, CreateVariantRequest r, BarcodeService barcodes, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var p = await db.Set<Product>().Include("_variantAttributes").SingleOrDefaultAsync(x => x.Id == productId, innerCt) ?? throw NotFound();
            var required = p.VariantAttributes.Select(a => a.AttributeId).ToList();
            var optionIds = (r.OptionIds ?? []).Distinct().ToList();
            var options = await db.Set<AttributeOption>().AsNoTracking().Where(o => optionIds.Contains(o.Id)).ToListAsync(innerCt);
            if (options.Count != optionIds.Count || options.Any(o => !o.IsActive))
            {
                throw new BusinessRuleException("VARIANT_OPTION_INVALID", "Every option must exist and be active.", 400);
            }
            if (options.Count != required.Count || !required.All(a => options.Count(o => o.AttributeId == a) == 1))
            {
                throw new BusinessRuleException("VARIANT_OPTIONS_MISMATCH",
                    required.Count == 0
                        ? "This product has no variant attributes; add a single standard variant without options."
                        : "Choose exactly one option for each of the product's variant attributes.", 400);
            }

            var ordered = required.Select(a => options.Single(o => o.AttributeId == a)).ToList();
            var name = ordered.Count == 0 ? "Standard" : string.Join(" / ", ordered.Select(o => o.Value));
            var variant = new Variant(p.Id, name, ordered.Select(o => (o.AttributeId, o.Id)).ToList(), clock.UtcNow);
            var skuCode = await ResolveSkuCodeAsync(r.SkuCode, innerCt);
            var sku = new Sku(variant.Id, p.Id, skuCode, clock.UtcNow);
            db.AddRange(variant, sku);
            await audit.RecordAsync(new AuditRecord("catalog.variant.created", "Product", p.Id.ToString(),
                After: new { variantId = variant.Id, variant.Name, skuId = sku.Id, skuCode }, Reason: r.Reason), innerCt);
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
            {
                throw pg.ConstraintName?.Contains("combination", StringComparison.Ordinal) == true
                    ? new ConflictException("VARIANT_ALREADY_EXISTS", $"Variant '{name}' already exists for this product.")
                    : new ConflictException("SKU_CODE_EXISTS", $"SKU code {skuCode} is already in use.");
            }
            if (r.GenerateBarcode)
            {
                await barcodes.GenerateInternalAsync(sku.Id, r.Reason, innerCt);
            }
            return await GetAsync(p.Id, innerCt);
        }, ct);
    }

    public Task<ProductDetailDto> SetVariantStatusAsync(Guid variantId, ChangeStatusRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        if (!Enum.TryParse<RecordStatus>(r.Status, true, out var status))
        {
            throw new BusinessRuleException("VARIANT_STATUS_INVALID", "Status must be Active or Inactive.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var v = await db.Set<Variant>().SingleOrDefaultAsync(x => x.Id == variantId, innerCt) ?? throw new NotFoundException("VARIANT_NOT_FOUND", "Variant not found.");
            var before = v.Status;
            v.SetStatus(status);
            await audit.RecordAsync(new AuditRecord("catalog.variant.status_changed", "Product", v.ProductId.ToString(),
                new { variantId, status = before.ToString() }, new { variantId, status = status.ToString() }, r.Reason), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await GetAsync(v.ProductId, innerCt);
        }, ct);
    }

    public async Task<SkuInfo?> FindSkuAsync(Guid skuId, CancellationToken cancellationToken = default) =>
        (await FindSkusAsync([skuId], cancellationToken)).GetValueOrDefault(skuId);

    public async Task<IReadOnlyDictionary<Guid, SkuInfo>> FindSkusAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken cancellationToken = default) =>
        await (from s in db.Set<Sku>()
               join v in db.Set<Variant>() on s.VariantId equals v.Id
               join p in db.Set<Product>() on s.ProductId equals p.Id
               where skuIds.Contains(s.Id)
               select new SkuInfo(s.Id, s.Code, v.Id, v.Name, v.Status == RecordStatus.Active, p.Id, p.Name, p.TrackingMode.ToString(), p.Status.ToString(),
                   p.AvailableForRetail, p.AvailableForReseller))
            .AsNoTracking().ToDictionaryAsync(x => x.SkuId, cancellationToken);

    private async Task<string> ResolveSkuCodeAsync(string? requested, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var code = requested.Trim().ToUpperInvariant();
            if (!SkuCodePattern().IsMatch(code))
            {
                throw new BusinessRuleException("SKU_CODE_INVALID", "SKU code must be 3–40 characters: A–Z, 0–9, '-' or '_'.", 400);
            }
            return code;
        }
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('catalog.sku_code_seq') AS \"Value\"").SingleAsync(ct);
        return $"MC-{seq:D6}";
    }

    private async Task EnsureActiveCategoryAsync(Guid categoryId, CancellationToken ct, bool allowInactive = false)
    {
        var category = await db.Set<Category>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Category not found.");
        if (!category.IsActive && !allowInactive)
        {
            throw new BusinessRuleException("CATEGORY_INACTIVE", "The category is inactive.");
        }
    }

    private static object Snapshot(Product p) =>
        new { p.Name, p.CategoryId, p.Description, trackingMode = p.TrackingMode.ToString(), p.AvailableForRetail, p.AvailableForReseller };

    private static TrackingMode ParseTrackingMode(string? value) =>
        Enum.TryParse<TrackingMode>(value, true, out var mode)
            ? mode
            : throw new BusinessRuleException("TRACKING_MODE_INVALID", "Tracking mode must be Serialized or Quantity.", 400);

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("PRODUCT_NAME_REQUIRED", "Product name is required.", 400);
        }
    }

    private static NotFoundException NotFound() => new("PRODUCT_NOT_FOUND", "Product not found.");

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{2,39}$")]
    private static partial Regex SkuCodePattern();
}
