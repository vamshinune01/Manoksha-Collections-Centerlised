using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Branches.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Manoksha.Modules.Branches.Application;

public sealed record BranchAddressDto(string? Line1, string? City, string? State, string? Pin, string? Phone);

public sealed record BranchDto(Guid Id, string Code, string Name, BranchAddressDto Address, bool IsActive, DateTimeOffset CreatedAt);

public sealed record CreateBranchRequest(string Code, string Name, BranchAddressDto? Address, string Reason);

public sealed record UpdateBranchRequest(string Name, BranchAddressDto? Address, string Reason);

public sealed record ChangeBranchStatusRequest(bool IsActive, string Reason);

public sealed record PriorityDto(int Version, DateTimeOffset? ChangedAt, Guid? ChangedBy, string? Reason, IReadOnlyList<FulfillmentPriorityEntry> Entries);

public sealed record SetPriorityRequest(IReadOnlyList<Guid> BranchIds, int ExpectedVersion, string Reason);

public sealed record BranchCreated(Guid BranchId, string Code) : IIntegrationEvent
{
    public static string EventType => "branches.branch_created";
}

public sealed record FulfillmentPriorityChanged(int Version) : IIntegrationEvent
{
    public static string EventType => "branches.fulfillment_priority_changed";
}

internal sealed class BranchService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock) : IBranchDirectory, IFulfillmentPriorityProvider
{
    public async Task<IReadOnlyList<BranchDto>> ListDtosAsync(CancellationToken ct) =>
        (await db.Set<Branch>().AsNoTracking().OrderBy(b => b.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<BranchDto> GetDtoAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.Set<Branch>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw NotFound());

    public Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken ct) =>
        CreateAsync(Uuid7.NewGuid(), request, ct);

    public Task<BranchDto> CreateAsync(Guid id, CreateBranchRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        var code = Branch.NormalizeCode(request.Code ?? string.Empty);
        if (code.Length is < 2 or > 10 || !code.All(char.IsAsciiLetterOrDigit))
        {
            throw new BusinessRuleException("BRANCH_CODE_INVALID", "Branch code must be 2–10 letters or digits (e.g. KNR).", 400);
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new BusinessRuleException("BRANCH_NAME_REQUIRED", "Branch name is required.", 400);
        }

        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var branch = new Branch(id, code, request.Name, ToAddress(request.Address), clock.UtcNow);
            db.Add(branch);
            await audit.RecordAsync(new AuditRecord("branches.branch.created", "Branch", branch.Id.ToString(), After: ToDto(branch), Reason: request.Reason, BranchId: branch.Id), innerCt);
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("BRANCH_CODE_EXISTS", $"A branch with code {code} already exists.");
            }

            // Every branch is always in the priority list: a new branch joins at the lowest priority until the Owner reorders.
            var (version, entries) = await GetCurrentAsync(innerCt);
            await WritePriorityVersionAsync(version, entries.Select(e => e.BranchId).Append(branch.Id).ToList(),
                $"Branch {branch.Code} created (added at lowest priority)", innerCt);
            outbox.Enqueue(new BranchCreated(branch.Id, branch.Code));
            return ToDto(branch);
        }, ct);
    }

    public Task<BranchDto> UpdateAsync(Guid id, UpdateBranchRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var branch = await db.Set<Branch>().SingleOrDefaultAsync(b => b.Id == id, innerCt) ?? throw NotFound();
            var before = ToDto(branch);
            branch.Update(request.Name, ToAddress(request.Address));
            await audit.RecordAsync(new AuditRecord("branches.branch.updated", "Branch", id.ToString(), before, ToDto(branch), request.Reason, id), innerCt);
            return ToDto(branch);
        }, ct);
    }

    public Task<BranchDto> ChangeStatusAsync(Guid id, ChangeBranchStatusRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var branch = await db.Set<Branch>().SingleOrDefaultAsync(b => b.Id == id, innerCt) ?? throw NotFound();
            if (branch.IsActive == request.IsActive)
            {
                return ToDto(branch);
            }
            branch.SetActive(request.IsActive);
            await audit.RecordAsync(new AuditRecord(request.IsActive ? "branches.branch.activated" : "branches.branch.deactivated", "Branch", id.ToString(),
                new { isActive = !request.IsActive }, new { isActive = request.IsActive }, request.Reason, id), innerCt);
            return ToDto(branch);
        }, ct);
    }

    public async Task<PriorityDto> GetPriorityDtoAsync(CancellationToken ct)
    {
        var current = await db.Set<FulfillmentPriorityVersion>().AsNoTracking().OrderByDescending(v => v.VersionNo).FirstOrDefaultAsync(ct);
        var (version, entries) = await GetCurrentAsync(ct);
        return new PriorityDto(version, current?.CreatedAt, current?.CreatedBy, current?.Reason, entries);
    }

    public async Task<IReadOnlyList<PriorityDto>> GetPriorityHistoryAsync(CancellationToken ct)
    {
        var versions = await db.Set<FulfillmentPriorityVersion>().AsNoTracking().OrderByDescending(v => v.VersionNo).Take(50).ToListAsync(ct);
        var result = new List<PriorityDto>();
        foreach (var v in versions)
        {
            result.Add(new PriorityDto(v.VersionNo, v.CreatedAt, v.CreatedBy, v.Reason, await LoadEntriesAsync(v.Id, ct)));
        }
        return result;
    }

    /// <summary>Owner reorders the complete list. Affects new checkout/reservation attempts only (SPEC §11).</summary>
    public Task<PriorityDto> SetPriorityAsync(SetPriorityRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            // Serialize concurrent priority edits.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE branches.fulfillment_priority_versions IN SHARE ROW EXCLUSIVE MODE", innerCt);
            var (version, _) = await GetCurrentAsync(innerCt);
            if (version != request.ExpectedVersion)
            {
                throw new ConflictException(ErrorCodes.ConcurrencyConflict, $"The priority list was changed by someone else (now version {version}). Reload and try again.");
            }
            var allBranches = await db.Set<Branch>().Select(b => b.Id).ToListAsync(innerCt);
            var requested = request.BranchIds ?? [];
            if (requested.Count != allBranches.Count || !allBranches.All(requested.Contains))
            {
                throw new BusinessRuleException("PRIORITY_MUST_LIST_ALL_BRANCHES",
                    "The priority list must contain every branch exactly once (inactive branches are skipped during routing).", 400);
            }
            await WritePriorityVersionAsync(version, requested, request.Reason, innerCt);
            outbox.Enqueue(new FulfillmentPriorityChanged(version + 1));
            return await GetPriorityDtoAsync(innerCt);
        }, ct);
    }

    public async Task<BranchInfo?> FindAsync(Guid branchId, CancellationToken cancellationToken = default) =>
        await db.Set<Branch>().AsNoTracking().Where(b => b.Id == branchId)
            .Select(b => new BranchInfo(b.Id, b.Code, b.Name, b.IsActive)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BranchInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Set<Branch>().AsNoTracking().OrderBy(b => b.Name)
            .Select(b => new BranchInfo(b.Id, b.Code, b.Name, b.IsActive)).ToListAsync(cancellationToken);

    public async Task<(int Version, IReadOnlyList<FulfillmentPriorityEntry> Entries)> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var current = await db.Set<FulfillmentPriorityVersion>().AsNoTracking().OrderByDescending(v => v.VersionNo).FirstOrDefaultAsync(cancellationToken);
        return current is null ? (0, []) : (current.VersionNo, await LoadEntriesAsync(current.Id, cancellationToken));
    }

    private async Task<IReadOnlyList<FulfillmentPriorityEntry>> LoadEntriesAsync(Guid versionId, CancellationToken ct) =>
        await (from e in db.Set<FulfillmentPriorityVersionEntry>()
               join b in db.Set<Branch>() on e.BranchId equals b.Id
               where e.VersionId == versionId
               orderby e.Priority
               select new FulfillmentPriorityEntry(e.Priority, b.Id, b.Code, b.Name, b.IsActive)).AsNoTracking().ToListAsync(ct);

    private async Task WritePriorityVersionAsync(int currentVersion, IReadOnlyList<Guid> ordered, string reason, CancellationToken ct)
    {
        var (_, oldEntries) = await GetCurrentAsync(ct);
        var version = new FulfillmentPriorityVersion(currentVersion + 1, ordered, currentUser.UserIdOrNull, reason, clock.UtcNow);
        db.Add(version);
        await db.SaveChangesAsync(ct);
        var codes = await db.Set<Branch>().Where(b => ordered.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Code, ct);
        await audit.RecordAsync(new AuditRecord("branches.fulfillment_priority.changed", "FulfillmentPriority", (currentVersion + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Before: new { version = currentVersion, order = oldEntries.Select(e => e.BranchCode) },
            After: new { version = currentVersion + 1, order = ordered.Select(id => codes[id]) },
            Reason: reason), ct);
    }

    private static BranchAddress ToAddress(BranchAddressDto? a) =>
        new(a?.Line1?.Trim(), a?.City?.Trim(), a?.State?.Trim(), a?.Pin?.Trim(), a?.Phone?.Trim());

    private static BranchDto ToDto(Branch b) =>
        new(b.Id, b.Code, b.Name, new BranchAddressDto(b.Address.Line1, b.Address.City, b.Address.State, b.Address.Pin, b.Address.Phone), b.IsActive, b.CreatedAt);

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    private static NotFoundException NotFound() => new("BRANCH_NOT_FOUND", "Branch not found.");
}
