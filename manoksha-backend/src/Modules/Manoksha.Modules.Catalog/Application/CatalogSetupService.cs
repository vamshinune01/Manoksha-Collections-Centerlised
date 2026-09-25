using System.Text.RegularExpressions;
using Manoksha.Application.Abstractions;
using Manoksha.Modules.Catalog.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Manoksha.Modules.Catalog.Application;

/// <summary>Categories and configurable variant attributes.</summary>
internal sealed partial class CatalogSetupService(ManokshaDbContext db, IUnitOfWork unitOfWork, IAuditWriter audit)
{
    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct) =>
        (await db.Set<Category>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest r, CancellationToken ct)
    {
        Validate(r);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureParentAsync(r.ParentId, null, innerCt);
            var c = new Category(r.ParentId, r.Name, r.SortOrder);
            if (!r.IsActive)
            {
                c.Update(r.ParentId, r.Name, r.SortOrder, false);
            }
            db.Add(c);
            await audit.RecordAsync(new AuditRecord("catalog.category.created", "Category", c.Id.ToString(), After: ToDto(c), Reason: r.Reason), innerCt);
            return ToDto(c);
        }, ct);
    }

    public Task<CategoryDto> UpdateCategoryAsync(Guid id, SaveCategoryRequest r, CancellationToken ct)
    {
        Validate(r);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var c = await db.Set<Category>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Category not found.");
            await EnsureParentAsync(r.ParentId, id, innerCt);
            var before = ToDto(c);
            c.Update(r.ParentId, r.Name, r.SortOrder, r.IsActive);
            await audit.RecordAsync(new AuditRecord("catalog.category.updated", "Category", id.ToString(), before, ToDto(c), r.Reason), innerCt);
            return ToDto(c);
        }, ct);
    }

    public async Task<IReadOnlyList<AttributeDto>> ListAttributesAsync(CancellationToken ct) =>
        (await db.Set<AttributeDefinition>().AsNoTracking().Include(a => a.Options).OrderBy(a => a.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public Task<AttributeDto> CreateAttributeAsync(CreateAttributeRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        var code = (r.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!AttributeCode().IsMatch(code))
        {
            throw new BusinessRuleException("ATTRIBUTE_CODE_INVALID", "Attribute code must be 2–40 characters: A–Z, 0–9, underscore (e.g. COLOUR).", 400);
        }
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            throw new BusinessRuleException("ATTRIBUTE_NAME_REQUIRED", "Attribute name is required.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var a = new AttributeDefinition(code, r.Name);
            foreach (var value in r.Options ?? [])
            {
                a.AddOption(value);
            }
            db.Add(a);
            await audit.RecordAsync(new AuditRecord("catalog.attribute.created", "Attribute", a.Id.ToString(), After: ToDto(a), Reason: r.Reason), innerCt);
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("ATTRIBUTE_CODE_EXISTS", $"An attribute with code {code} already exists.");
            }
            return ToDto(a);
        }, ct);
    }

    public Task<AttributeDto> UpdateAttributeAsync(Guid id, UpdateAttributeRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var a = await LoadAttributeAsync(id, innerCt);
            var before = ToDto(a);
            a.Update(r.Name, r.IsActive);
            await audit.RecordAsync(new AuditRecord("catalog.attribute.updated", "Attribute", id.ToString(), before, ToDto(a), r.Reason), innerCt);
            return ToDto(a);
        }, ct);
    }

    public Task<AttributeDto> AddOptionAsync(Guid id, AddOptionRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var a = await LoadAttributeAsync(id, innerCt);
            var option = a.AddOption(r.Value);
            db.Add(option);
            await audit.RecordAsync(new AuditRecord("catalog.attribute.option_added", "Attribute", id.ToString(), After: new { option.Id, option.Value }, Reason: r.Reason), innerCt);
            return ToDto(a);
        }, ct);
    }

    public Task<AttributeDto> SetOptionStatusAsync(Guid attributeId, Guid optionId, SetOptionStatusRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var a = await LoadAttributeAsync(attributeId, innerCt);
            var option = a.Options.SingleOrDefault(o => o.Id == optionId) ?? throw new NotFoundException("ATTRIBUTE_OPTION_NOT_FOUND", "Option not found.");
            option.SetActive(r.IsActive);
            await audit.RecordAsync(new AuditRecord("catalog.attribute.option_status_changed", "Attribute", attributeId.ToString(),
                After: new { optionId, option.Value, r.IsActive }, Reason: r.Reason), innerCt);
            return ToDto(a);
        }, ct);
    }

    private async Task<AttributeDefinition> LoadAttributeAsync(Guid id, CancellationToken ct) =>
        await db.Set<AttributeDefinition>().Include(a => a.Options).SingleOrDefaultAsync(a => a.Id == id, ct)
        ?? throw new NotFoundException("ATTRIBUTE_NOT_FOUND", "Attribute not found.");

    private async Task EnsureParentAsync(Guid? parentId, Guid? selfId, CancellationToken ct)
    {
        if (parentId is null)
        {
            return;
        }
        // Walk up the tree: the parent must exist and must not be a descendant of this category.
        var all = await db.Set<Category>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);
        if (!all.ContainsKey(parentId.Value))
        {
            throw new NotFoundException("CATEGORY_NOT_FOUND", "Parent category not found.");
        }
        for (Guid? cursor = parentId; cursor is { } id; cursor = all.GetValueOrDefault(id))
        {
            if (id == selfId)
            {
                throw new BusinessRuleException("CATEGORY_PARENT_INVALID", "A category cannot be moved under itself or its own sub-category.", 400);
            }
        }
    }

    private static void Validate(SaveCategoryRequest r)
    {
        RequireReason(r.Reason);
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            throw new BusinessRuleException("CATEGORY_NAME_REQUIRED", "Category name is required.", 400);
        }
    }

    internal static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    internal static CategoryDto ToDto(Category c) => new(c.Id, c.ParentId, c.Name, c.Slug, c.SortOrder, c.IsActive);

    internal static AttributeDto ToDto(AttributeDefinition a) =>
        new(a.Id, a.Code, a.Name, a.IsActive, a.Options.OrderBy(o => o.SortOrder).Select(o => new AttributeOptionDto(o.Id, o.Value, o.SortOrder, o.IsActive)).ToList());

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,39}$")]
    private static partial Regex AttributeCode();
}
