using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

public sealed record CustomerProfileDto(string FullName, string? Email, string? Mobile, DateTimeOffset MemberSince);

public sealed record UpdateCustomerProfileRequest(string FullName, string Email);

/// <summary>The signed-in customer's own profile (SPEC §4, §24). The mobile number is the sign-in identity and stays read-only.</summary>
internal sealed class CustomerProfileService(ManokshaDbContext db, IAuditWriter audit, ICurrentUser currentUser)
{
    public async Task<CustomerProfileDto> GetAsync(CancellationToken ct) => ToDto(await SelfAsync(ct));

    public async Task<CustomerProfileDto> UpdateAsync(UpdateCustomerProfileRequest request, CancellationToken ct)
    {
        var name = request.FullName?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 200)
        {
            throw new BusinessRuleException("NAME_INVALID", "Enter your full name (2–200 characters).", 400);
        }
        if (!EmailAddress.IsValid(request.Email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address.", 400);
        }
        var user = await SelfAsync(ct);
        var before = new { user.DisplayName, user.Email };
        user.UpdateCustomerProfile(name, request.Email);
        await audit.RecordAsync(new AuditRecord("identity.customer.profile_updated", "User", user.Id.ToString(), Before: before,
            After: new { user.DisplayName, user.Email }), ct);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    private async Task<User> SelfAsync(CancellationToken ct) =>
        await db.Set<User>().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.AccountType == AccountType.Customer, ct)
        ?? throw new ForbiddenException(ErrorCodes.Forbidden, "This sign-in has no customer profile.");

    private static CustomerProfileDto ToDto(User u) => new(u.DisplayName, u.Email, u.MobileE164, u.CreatedAt);
}
