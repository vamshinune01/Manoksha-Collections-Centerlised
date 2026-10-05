using System.Text.RegularExpressions;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Catalog.Application;

/// <param name="OwnerMarginPct">Only returned to the Owner (pricing.manage); null for everyone else.</param>
public sealed record VendorDto(Guid Id, string Code, string Name, decimal ShippingFee, decimal? OwnerMarginPct, bool IsActive, int ProductCount, DateTimeOffset CreatedAt);

public sealed record PublicVendorDto(Guid Id, string Code, string Name);

public sealed record CreateVendorRequest(string Code, string Name, decimal ShippingFee, decimal? OwnerMarginPct, string Reason);

public sealed record UpdateVendorRequest(string Name, decimal ShippingFee, decimal? OwnerMarginPct, bool IsActive, string Reason);

/// <summary>Vendors who supply and ship products directly (ADR-001 §41–47). Managed by the Owner; changes are audited.</summary>
internal sealed partial class VendorService(ManokshaDbContext db, IPermissionService permissions, IAuditWriter audit, IClock clock)
{
    public async Task<IReadOnlyList<VendorDto>> ListAsync(CancellationToken ct)
    {
        var showMargin = await permissions.HasPermissionAsync(P.Pricing.Manage, ct);
        var counts = await db.Set<Product>().Where(p => p.VendorId != null).GroupBy(p => p.VendorId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await db.Set<Vendor>().AsNoTracking().OrderBy(v => v.Name).ToListAsync(ct))
            .Select(v => ToDto(v, showMargin, counts.GetValueOrDefault(v.Id))).ToList();
    }

    public async Task<IReadOnlyList<PublicVendorDto>> ListPublicAsync(CancellationToken ct) =>
        await db.Set<Vendor>().AsNoTracking().Where(v => v.IsActive).OrderBy(v => v.Name).Select(v => new PublicVendorDto(v.Id, v.Code, v.Name)).ToListAsync(ct);

    public async Task<VendorDto> CreateAsync(CreateVendorRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        var code = (r.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
        {
            throw new BusinessRuleException("VENDOR_CODE_INVALID", "The vendor code must be 2 to 4 capital letters, e.g. ZR for Zara (it starts every product ID).", 400);
        }
        var name = RequireName(r.Name);
        ValidateMoney(r.ShippingFee, r.OwnerMarginPct);
        if (await db.Set<Vendor>().AnyAsync(v => v.Code == code || v.Name == name, ct))
        {
            throw new BusinessRuleException("VENDOR_EXISTS", "A vendor with this code or name already exists.", 409);
        }
        var vendor = new Vendor(code, name, r.ShippingFee, r.OwnerMarginPct, clock.UtcNow);
        db.Add(vendor);
        await audit.RecordAsync(new AuditRecord("catalog.vendor.created", "Vendor", vendor.Id.ToString(),
            After: new { code, name, r.ShippingFee, r.OwnerMarginPct }, Reason: r.Reason), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(vendor, true, 0);
    }

    public async Task<VendorDto> UpdateAsync(Guid id, UpdateVendorRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        var name = RequireName(r.Name);
        ValidateMoney(r.ShippingFee, r.OwnerMarginPct);
        var vendor = await db.Set<Vendor>().SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("VENDOR_NOT_FOUND", "Vendor not found.");
        if (await db.Set<Vendor>().AnyAsync(v => v.Id != id && v.Name == name, ct))
        {
            throw new BusinessRuleException("VENDOR_EXISTS", "Another vendor already has this name.", 409);
        }
        var before = new { vendor.Name, vendor.ShippingFee, vendor.OwnerMarginPct, vendor.IsActive };
        vendor.Update(name, r.ShippingFee, r.OwnerMarginPct, r.IsActive);
        await audit.RecordAsync(new AuditRecord("catalog.vendor.updated", "Vendor", id.ToString(), before,
            new { vendor.Name, vendor.ShippingFee, vendor.OwnerMarginPct, vendor.IsActive }, r.Reason), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(vendor, true, await db.Set<Product>().CountAsync(p => p.VendorId == id, ct));
    }

    /// <summary>Next product ID for the vendor (e.g. ZR-000124). Atomic, so two uploads at once never share an ID.</summary>
    internal async Task<(Guid VendorId, string ProductCode)> NextProductCodeAsync(Guid vendorId, CancellationToken ct)
    {
        var code = (await db.Database.SqlQuery<string>($"""
            UPDATE catalog.vendors SET next_product_number = next_product_number + 1
            WHERE id = {vendorId} AND is_active
            RETURNING code || '-' || lpad((next_product_number - 1)::text, 6, '0') AS "Value"
            """).ToListAsync(ct)).SingleOrDefault()
            ?? throw new BusinessRuleException("VENDOR_INVALID", "Choose an active vendor for this product.", 400);
        return (vendorId, code);
    }

    private static VendorDto ToDto(Vendor v, bool showMargin, int products) =>
        new(v.Id, v.Code, v.Name, v.ShippingFee, showMargin ? v.OwnerMarginPct : null, v.IsActive, products, v.CreatedAt);

    private static void ValidateMoney(decimal shippingFee, decimal? margin)
    {
        if (shippingFee < 0 || !Money.HasValidScale(shippingFee))
        {
            throw new BusinessRuleException("SHIPPING_FEE_INVALID", "Shipping must be ₹0 or more, with at most 2 decimals.", 400);
        }
        if (margin is { } m && (m < 0 || m >= 100 || decimal.Round(m, 2) != m))
        {
            throw new BusinessRuleException("MARGIN_INVALID", "Your margin must be between 0 and 99.99 %, with at most 2 decimals.", 400);
        }
    }

    private static string RequireName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length is 0 or > 100)
        {
            throw new BusinessRuleException("VENDOR_NAME_REQUIRED", "Enter the vendor's name (up to 100 characters).", 400);
        }
        return n;
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required.", 400);
        }
    }

    [GeneratedRegex("^[A-Z]{2,4}$")]
    private static partial Regex CodePattern();
}
