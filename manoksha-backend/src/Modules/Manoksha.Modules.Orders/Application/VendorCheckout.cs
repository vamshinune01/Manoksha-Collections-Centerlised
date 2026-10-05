using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Orders.Application;

/// <summary>One vendor's share of a cart: its lines and its shipping fee (ADR-001 §46).</summary>
internal sealed record VendorGroup(VendorInfo Vendor, IReadOnlyList<CheckoutLineRequest> Lines);

internal sealed record VendorBasket(IReadOnlyList<VendorGroup> Groups)
{
    public decimal Shipping => Groups.Sum(g => g.Vendor.ShippingFee);
}

/// <summary>Shared rules of vendor checkouts (online and reseller): grouping, shipping per vendor, parcels and cost snapshots.</summary>
internal static class VendorCheckout
{
    /// <summary>
    /// Null when the cart has only branch-stock products (paused flow). Throws when vendor and branch products are mixed, or a
    /// vendor is no longer active.
    /// </summary>
    public static async Task<VendorBasket?> GroupAsync(IReadOnlyList<CheckoutLineRequest> lines, IReadOnlyDictionary<Guid, SkuInfo> skus, ICatalogLookup catalog,
        CancellationToken ct)
    {
        var vendorLines = lines.Where(l => skus[l.SkuId].VendorId is not null).ToList();
        if (vendorLines.Count == 0)
        {
            return null;
        }
        if (vendorLines.Count != lines.Count)
        {
            throw new BusinessRuleException("CART_MIXED", "Some items in the cart are no longer sold online. Remove them and try again.", 422);
        }
        var vendors = await catalog.GetVendorsAsync(vendorLines.Select(l => skus[l.SkuId].VendorId!.Value).Distinct().ToList(), ct);
        var inactive = vendors.Values.FirstOrDefault(v => !v.IsActive);
        if (inactive is not null)
        {
            throw new BusinessRuleException("VENDOR_INACTIVE", $"{inactive.Name} products are not available right now. Remove them and try again.", 422);
        }
        return new VendorBasket(vendorLines.GroupBy(l => skus[l.SkuId].VendorId!.Value)
            .Select(g => new VendorGroup(vendors[g.Key], g.ToList()))
            .OrderBy(g => g.Vendor.Name).ToList());
    }

    /// <summary>Adds one parcel per vendor and places each line in its vendor's parcel, with the vendor-price cost snapshot.</summary>
    public static void AddParcels(ManokshaDbContext db, Order order, VendorBasket basket, IReadOnlyDictionary<Guid, SkuInfo> skus, IReadOnlyDictionary<Guid, OrderLine> lines,
        DateTimeOffset now)
    {
        foreach (var group in basket.Groups)
        {
            var parcel = new OrderParcel(order.Id, group.Vendor.Id, group.Vendor.Code, group.Vendor.Name, group.Vendor.ShippingFee, now);
            db.Add(parcel);
            foreach (var l in group.Lines)
            {
                var line = lines[l.SkuId];
                decimal? cost = group.Vendor.OwnerMarginPct is { } margin ? Money.ApplyPercentDiscount(line.RetailUnitPrice, margin) * line.Quantity : null;
                line.PlaceInParcel(parcel.Id, group.Vendor.Id, skus[l.SkuId].ProductCode, cost);
            }
        }
    }
}
