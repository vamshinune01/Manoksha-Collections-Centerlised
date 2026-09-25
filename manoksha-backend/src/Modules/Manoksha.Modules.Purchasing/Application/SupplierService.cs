using Manoksha.Application.Abstractions;
using Manoksha.Modules.Purchasing.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Purchasing.Application;

internal sealed class SupplierService(ManokshaDbContext db, IUnitOfWork unitOfWork, IAuditWriter audit, IClock clock)
{
    public async Task<IReadOnlyList<SupplierDto>> ListAsync(CancellationToken ct) =>
        (await db.Set<Supplier>().AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw NotFound());

    public Task<SupplierDto> CreateAsync(SaveSupplierRequest r, CancellationToken ct)
    {
        Validate(r, out var mobile);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var seq = await db.Database.SqlQuery<long>($"SELECT nextval('purchasing.supplier_seq') AS \"Value\"").SingleAsync(innerCt);
            var s = new Supplier($"SUP-{seq:D4}", r.Name, clock.UtcNow);
            s.Update(r.Name, r.ContactName, mobile, r.Email, r.Gstin, r.Address, r.IsActive);
            db.Add(s);
            await audit.RecordAsync(new AuditRecord("purchasing.supplier.created", "Supplier", s.Id.ToString(), After: ToDto(s), Reason: r.Reason), innerCt);
            return ToDto(s);
        }, ct);
    }

    public Task<SupplierDto> UpdateAsync(Guid id, SaveSupplierRequest r, CancellationToken ct)
    {
        Validate(r, out var mobile);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var s = await db.Set<Supplier>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            var before = ToDto(s);
            s.Update(r.Name, r.ContactName, mobile, r.Email, r.Gstin, r.Address, r.IsActive);
            await audit.RecordAsync(new AuditRecord("purchasing.supplier.updated", "Supplier", id.ToString(), before, ToDto(s), r.Reason), innerCt);
            return ToDto(s);
        }, ct);
    }

    private static void Validate(SaveSupplierRequest r, out string? mobile)
    {
        if (string.IsNullOrWhiteSpace(r.Reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            throw new BusinessRuleException("SUPPLIER_NAME_REQUIRED", "Supplier name is required.", 400);
        }
        if (!string.IsNullOrWhiteSpace(r.Email) && !EmailAddress.IsValid(r.Email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address.", 400);
        }
        mobile = string.IsNullOrWhiteSpace(r.Mobile) ? null : MobileNumber.Normalize(r.Mobile);
    }

    internal static SupplierDto ToDto(Supplier s) => new(s.Id, s.Code, s.Name, s.ContactName, s.MobileE164, s.Email, s.Gstin, s.Address, s.IsActive);

    private static NotFoundException NotFound() => new("SUPPLIER_NOT_FOUND", "Supplier not found.");
}
