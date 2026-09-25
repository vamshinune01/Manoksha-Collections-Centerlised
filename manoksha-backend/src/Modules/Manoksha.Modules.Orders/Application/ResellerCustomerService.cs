using Manoksha.Application.Security;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Orders.Application;

/// <summary>A reseller's own end-customers — every query is scoped to the reseller from the token (SPEC §24, ADR-001 §5).</summary>
internal sealed class ResellerCustomerService(ManokshaDbContext db, IResellerDirectory resellers, ICurrentUser currentUser, IClock clock)
{
    public async Task<IReadOnlyList<ResellerCustomerDto>> ListAsync(string? q, CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        var query = db.Set<ResellerCustomer>().AsNoTracking().Where(c => c.ResellerId == me.ResellerId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Details.Name, pattern) || EF.Functions.ILike(c.Details.Mobile, pattern));
        }
        return (await query.OrderBy(c => c.Details.Name).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<ResellerCustomerDto> CreateAsync(SaveResellerCustomerRequest r, CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        var customer = await UpsertAsync(me.ResellerId, Validate(r.Details), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    public async Task<ResellerCustomerDto> UpdateAsync(Guid id, SaveResellerCustomerRequest r, CancellationToken ct)
    {
        var me = await SelfAsync(ct);
        var customer = await db.Set<ResellerCustomer>().SingleOrDefaultAsync(c => c.Id == id && c.ResellerId == me.ResellerId, ct) ?? throw NotFound();
        customer.Update(Validate(r.Details), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    /// <summary>Owner/authorized staff: any reseller's customer list (global visibility, SPEC §24).</summary>
    public async Task<IReadOnlyList<ResellerCustomerDto>> ListForResellerAsync(Guid resellerId, string? q, CancellationToken ct)
    {
        _ = await resellers.FindAsync(resellerId, ct) ?? throw new NotFoundException("RESELLER_NOT_FOUND", "Reseller not found.");
        var query = db.Set<ResellerCustomer>().AsNoTracking().Where(c => c.ResellerId == resellerId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Details.Name, pattern) || EF.Functions.ILike(c.Details.Mobile, pattern));
        }
        return (await query.OrderBy(c => c.Details.Name).Take(1000).ToListAsync(ct)).Select(ToDto).ToList();
    }

    internal async Task<ResellerCustomer> GetOwnAsync(Guid resellerId, Guid id, CancellationToken ct) =>
        await db.Set<ResellerCustomer>().SingleOrDefaultAsync(c => c.Id == id && c.ResellerId == resellerId, ct) ?? throw NotFound();

    /// <summary>Same mobile within one reseller's list = same customer; details are refreshed.</summary>
    internal async Task<ResellerCustomer> UpsertAsync(Guid resellerId, DeliveryDetails details, CancellationToken ct)
    {
        var existing = await db.Set<ResellerCustomer>().SingleOrDefaultAsync(c => c.ResellerId == resellerId && c.Details.Mobile == details.Mobile, ct);
        if (existing is not null)
        {
            existing.Update(details, clock.UtcNow);
            return existing;
        }
        var created = new ResellerCustomer(resellerId, details, clock.UtcNow);
        db.Add(created);
        return created;
    }

    /// <summary>Name, mobile, full address required; email optional (ADR-001 §22).</summary>
    internal static DeliveryDetails Validate(DeliveryDto? d)
    {
        if (d is null || string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.AddressLine) || string.IsNullOrWhiteSpace(d.City) || string.IsNullOrWhiteSpace(d.State))
        {
            throw new BusinessRuleException("DELIVERY_DETAILS_REQUIRED", "Enter the name, mobile number and full delivery address.", 400);
        }
        var pin = (d.Pin ?? string.Empty).Trim();
        if (pin.Length != 6 || !pin.All(char.IsAsciiDigit) || pin[0] == '0')
        {
            throw new BusinessRuleException("PIN_INVALID", "Enter a valid 6-digit PIN code.", 400);
        }
        if (!string.IsNullOrWhiteSpace(d.Email) && !EmailAddress.IsValid(d.Email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address or leave it empty.", 400);
        }
        return new DeliveryDetails(d.Name.Trim(), MobileNumber.Normalize(d.Mobile), string.IsNullOrWhiteSpace(d.Email) ? null : d.Email.Trim(),
            d.AddressLine.Trim(), d.City.Trim(), d.State.Trim(), pin);
    }

    internal static DeliveryDto ToDto(DeliveryDetails d) => new(d.Name, d.Mobile, d.Email, d.AddressLine, d.City, d.State, d.Pin);

    private static ResellerCustomerDto ToDto(ResellerCustomer c) => new(c.Id, ToDto(c.Details), c.CreatedAt, c.UpdatedAt);

    private async Task<ResellerInfo> SelfAsync(CancellationToken ct) =>
        await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");

    private static NotFoundException NotFound() => new("CUSTOMER_NOT_FOUND", "Customer not found.");
}
