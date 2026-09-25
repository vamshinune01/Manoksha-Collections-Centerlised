using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Inventory.Application;

/// <summary>
/// Blind stock counts: counters record what they physically find; the system quantity is captured and compared only at
/// submission. Every difference becomes a discrepancy (SPEC §28 "Inventory Discrepancy").
/// </summary>
internal sealed class CountService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    InventoryAccess access,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    /// <summary>Statuses that are physically present at the branch.</summary>
    private static readonly InventoryStatus[] Present =
        [InventoryStatus.Available, InventoryStatus.Reserved, InventoryStatus.TransferPending, InventoryStatus.Damaged, InventoryStatus.Blocked, InventoryStatus.Received];

    public async Task<IReadOnlyList<CountDto>> ListAsync(Guid? branchId, CancellationToken ct)
    {
        var visible = await access.VisibleBranchesAsync(P.Inventory.Count, ct);
        var q = db.Set<StockCount>().AsNoTracking();
        if (visible is not null)
        {
            q = q.Where(c => visible.Contains(c.BranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(c => c.BranchId == b);
        }
        var counts = await q.OrderByDescending(c => c.CreatedAt).Take(100).ToListAsync(ct);
        var result = new List<CountDto>();
        foreach (var c in counts)
        {
            result.Add(await ToDtoAsync(c, ct));
        }
        return result;
    }

    public async Task<CountDto> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Set<StockCount>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        await access.EnsureAsync(P.Inventory.Count, c.BranchId, ct);
        return await ToDtoAsync(c, ct);
    }

    public Task<CountDto> CreateAsync(CreateCountRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await access.EnsureAsync(P.Inventory.Count, r.BranchId, innerCt);
            await access.ActiveBranchAsync(r.BranchId, innerCt);
            var skuIds = r.SkuIds is { Count: > 0 } requested
                ? requested.Distinct().ToList()
                : await SkusWithStockAsync(r.BranchId, innerCt);
            await access.SkusAsync(skuIds, innerCt);
            var count = new StockCount(await Numbering.NextAsync(db, "count_seq", "CNT", innerCt), r.BranchId, r.Notes, currentUser.UserId, clock.UtcNow);
            db.Add(count);
            skuIds.ForEach(s => db.Add(new StockCountLine(count.Id, s)));
            await audit.RecordAsync(new AuditRecord("inventory.count.started", "StockCount", count.Id.ToString(), After: new { count.Number, skus = skuIds.Count }, BranchId: r.BranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await ToDtoAsync(count, innerCt);
        }, ct);

    public Task<CountDto> RecordAsync(Guid id, RecordCountsRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var c = await LoadOpenAsync(id, innerCt);
            await access.EnsureAsync(P.Inventory.Count, c.BranchId, innerCt);
            var lines = await db.Set<StockCountLine>().Where(l => l.CountId == id).ToDictionaryAsync(l => l.SkuId, innerCt);
            var newSkus = (r.Lines ?? []).Select(l => l.SkuId).Where(s => !lines.ContainsKey(s)).Distinct().ToList();
            await access.SkusAsync(newSkus, innerCt);
            foreach (var l in r.Lines ?? [])
            {
                if (l.CountedQty < 0)
                {
                    throw new BusinessRuleException("QUANTITY_INVALID", "Counted quantity cannot be negative.", 400);
                }
                if (!lines.TryGetValue(l.SkuId, out var line))
                {
                    line = new StockCountLine(id, l.SkuId);
                    db.Add(line);
                    lines[l.SkuId] = line;
                }
                line.CountedQty = l.CountedQty;
            }
            await db.SaveChangesAsync(innerCt);
            return await ToDtoAsync(c, innerCt);
        }, ct);

    public Task<CountDto> SubmitAsync(Guid id, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var c = await LoadOpenAsync(id, innerCt);
            await access.EnsureAsync(P.Inventory.Count, c.BranchId, innerCt);
            var lines = await db.Set<StockCountLine>().Where(l => l.CountId == id).ToListAsync(innerCt);
            if (lines.Exists(l => l.CountedQty is null))
            {
                throw new BusinessRuleException("COUNT_INCOMPLETE", "Enter a counted quantity (0 if none) for every SKU before submitting.", 400);
            }
            c.Submit(currentUser.UserId, clock.UtcNow);
            var discrepancies = new List<string>();
            foreach (var line in lines.OrderBy(l => l.SkuId))
            {
                line.SystemQty = await PresentQuantityAsync(line.SkuId, c.BranchId, innerCt);
                if (line.SystemQty != line.CountedQty)
                {
                    var d = new InventoryDiscrepancy(await Numbering.NextAsync(db, "discrepancy_seq", "DSC", innerCt), "COUNT", c.Id, c.Number, c.BranchId, line.SkuId,
                        line.SystemQty.Value, line.CountedQty!.Value, [], clock.UtcNow);
                    db.Add(d);
                    discrepancies.Add(d.Number);
                }
            }
            await audit.RecordAsync(new AuditRecord("inventory.count.submitted", "StockCount", id.ToString(),
                After: new { c.Number, lines = lines.Select(l => new { l.SkuId, l.SystemQty, l.CountedQty }), discrepancies }, BranchId: c.BranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return await ToDtoAsync(c, innerCt);
        }, ct);

    private async Task<int> PresentQuantityAsync(Guid skuId, Guid branchId, CancellationToken ct)
    {
        var quantities = await db.Set<StockLevel>().Where(l => l.SkuId == skuId && l.BranchId == branchId && Present.Contains(l.Status)).SumAsync(l => l.Quantity, ct);
        var items = await db.Set<InventoryItem>().CountAsync(i => i.SkuId == skuId && i.BranchId == branchId && Present.Contains(i.Status) && i.WrittenOffAt == null, ct);
        return quantities + items;
    }

    private async Task<List<Guid>> SkusWithStockAsync(Guid branchId, CancellationToken ct)
    {
        var fromLevels = await db.Set<StockLevel>().Where(l => l.BranchId == branchId && l.Quantity > 0 && Present.Contains(l.Status)).Select(l => l.SkuId).Distinct().ToListAsync(ct);
        var fromItems = await db.Set<InventoryItem>().Where(i => i.BranchId == branchId && Present.Contains(i.Status) && i.WrittenOffAt == null).Select(i => i.SkuId).Distinct().ToListAsync(ct);
        return fromLevels.Union(fromItems).ToList();
    }

    private async Task<StockCount> LoadOpenAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Set<StockCount>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (c.Status != StockCountStatus.Open)
        {
            throw new BusinessRuleException("COUNT_ALREADY_SUBMITTED", "This stock count was already submitted.");
        }
        return c;
    }

    private async Task<CountDto> ToDtoAsync(StockCount c, CancellationToken ct)
    {
        var lines = await db.Set<StockCountLine>().AsNoTracking().Where(l => l.CountId == c.Id).ToListAsync(ct);
        var skus = await access.SkusAsync(lines.Select(l => l.SkuId).ToList(), ct);
        var names = await access.BranchNamesAsync(ct);
        var discrepancies = await db.Set<InventoryDiscrepancy>().AsNoTracking().Where(d => d.SourceType == "COUNT" && d.SourceId == c.Id).Select(d => d.Id).ToListAsync(ct);
        var open = c.Status == StockCountStatus.Open;
        return new CountDto(c.Id, c.Number, c.BranchId, names.GetValueOrDefault(c.BranchId, "?"), c.Status.ToString(), c.Notes, c.CreatedBy, c.CreatedAt, c.SubmittedAt,
            lines.OrderBy(l => skus[l.SkuId].SkuCode).Select(l => new CountLineDto(l.SkuId, skus[l.SkuId].SkuCode, skus[l.SkuId].ProductName, skus[l.SkuId].VariantName,
                l.CountedQty, open ? null : l.SystemQty, open ? null : l.CountedQty - l.SystemQty)).ToList(),
            discrepancies);
    }

    private static NotFoundException NotFound() => new("COUNT_NOT_FOUND", "Stock count not found.");
}
