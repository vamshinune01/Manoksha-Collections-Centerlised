using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Inventory.Application;

/// <summary>
/// Discrepancies require an explicit, audited decision. Transfer shortfalls: RECEIVED_LATE (destination found them),
/// RETURNED_TO_SOURCE (back at the source) or WRITTEN_OFF (lost in transit; carried cost becomes a loss).
/// Count differences are resolved by an approved adjustment, or DISMISSED with a reason.
/// </summary>
internal sealed class DiscrepancyService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    StockEngine engine,
    TransferService transfers,
    InventoryAccess access,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public const string ReceivedLate = "RECEIVED_LATE";
    public const string ReturnedToSource = "RETURNED_TO_SOURCE";
    public const string WrittenOff = "WRITTEN_OFF";
    public const string Dismissed = "DISMISSED";

    public async Task<IReadOnlyList<DiscrepancyDto>> ListAsync(string? status, Guid? branchId, CancellationToken ct)
    {
        var visible = await access.VisibleBranchesAsync(P.Inventory.View, ct);
        var q = db.Set<InventoryDiscrepancy>().AsNoTracking();
        if (visible is not null)
        {
            q = q.Where(d => visible.Contains(d.BranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(d => d.BranchId == b);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DiscrepancyStatus>(status, true, out var st))
        {
            q = q.Where(d => d.Status == st);
        }
        return await ToDtosAsync(await q.OrderByDescending(d => d.CreatedAt).Take(200).ToListAsync(ct), ct);
    }

    public async Task<DiscrepancyDto> GetAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Set<InventoryDiscrepancy>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        await access.EnsureAsync(P.Inventory.View, d.BranchId, ct);
        return (await ToDtosAsync([d], ct))[0];
    }

    public Task<DiscrepancyDto> ResolveAsync(Guid id, ResolveDiscrepancyRequest r, CancellationToken ct)
    {
        InventoryAccess.RequireText(r.Notes, "REASON_REQUIRED", "Notes are required to resolve a discrepancy.");
        var action = (r.Action ?? string.Empty).Trim().ToUpperInvariant();
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var d = await db.Set<InventoryDiscrepancy>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            await access.EnsureAsync(P.Inventory.DiscrepancyResolve, d.BranchId, innerCt);
            if (d.Status != DiscrepancyStatus.Open)
            {
                throw new BusinessRuleException("DISCREPANCY_ALREADY_RESOLVED", "This discrepancy is already resolved.");
            }

            if (d.SourceType == "COUNT")
            {
                if (action != Dismissed)
                {
                    throw new BusinessRuleException("DISCREPANCY_ACTION_INVALID", "Count differences are fixed with an inventory adjustment, or dismissed with a reason.", 400);
                }
                d.Resolve(Dismissed, r.Notes, currentUser.UserId, clock.UtcNow);
            }
            else
            {
                await ResolveTransferShortfallAsync(d, action, r, innerCt);
            }

            await audit.RecordAsync(new AuditRecord("inventory.discrepancy.resolved", "Discrepancy", id.ToString(),
                After: new { d.Number, action, r.Quantity, r.ItemIds, status = d.Status.ToString() }, Reason: r.Notes, BranchId: d.BranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([d], innerCt))[0];
        }, ct);
    }

    private async Task ResolveTransferShortfallAsync(InventoryDiscrepancy d, string action, ResolveDiscrepancyRequest r, CancellationToken ct)
    {
        if (action is not (ReceivedLate or ReturnedToSource or WrittenOff))
        {
            throw new BusinessRuleException("DISCREPANCY_ACTION_INVALID", "Choose RECEIVED_LATE, RETURNED_TO_SOURCE or WRITTEN_OFF.", 400);
        }
        var transfer = await transfers.LoadAsync(d.SourceId, ct);
        var line = (await transfers.LinesAsync(transfer.Id, ct)).Single(l => l.SkuId == d.SkuId);
        var ctx = new MovementContext($"TRANSFER_{action}", "Transfer", transfer.Id, transfer.Number, r.Notes);
        int quantity;

        if (line.Serialized)
        {
            var pending = await db.Set<TransferLineItem>().Where(i => i.TransferLineId == line.Id && i.Outcome == null).ToListAsync(ct);
            var chosen = (r.ItemIds ?? []).Distinct().ToList();
            if (chosen.Count == 0 || chosen.Any(id => pending.TrueForAll(p => p.ItemId != id)))
            {
                throw new BusinessRuleException("DISCREPANCY_ITEMS_INVALID", "Select missing pieces from this discrepancy.", 400);
            }
            switch (action)
            {
                case ReceivedLate:
                    await engine.MoveItemsAsync(chosen, line.SkuId, transfer.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Available, ctx, transfer.DestinationBranchId, ct);
                    break;
                case ReturnedToSource:
                    await engine.MoveItemsAsync(chosen, line.SkuId, transfer.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Available, ctx, ct: ct);
                    break;
                default:
                    await engine.MoveItemsAsync(chosen, line.SkuId, transfer.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Lost, ctx, ct: ct);
                    await engine.WriteOffItemsAsync(chosen, line.SkuId, transfer.SourceBranchId, InventoryStatus.Lost, ctx, ct);
                    break;
            }
            pending.Where(p => chosen.Contains(p.ItemId)).ToList().ForEach(p => p.Outcome = action);
            quantity = chosen.Count;
        }
        else
        {
            quantity = r.Quantity ?? 0;
            if (quantity <= 0 || quantity > line.OutstandingQty)
            {
                throw new BusinessRuleException("DISCREPANCY_QTY_INVALID", $"Quantity must be between 1 and {line.OutstandingQty}.", 400);
            }
            switch (action)
            {
                case ReceivedLate:
                    await engine.MoveQuantityAsync(line.SkuId, transfer.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Available, quantity, ctx, transfer.DestinationBranchId, ct);
                    break;
                case ReturnedToSource:
                    await engine.MoveQuantityAsync(line.SkuId, transfer.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Available, quantity, ctx, ct: ct);
                    break;
                default:
                    await engine.RemoveQuantityAsync(line.SkuId, transfer.SourceBranchId, InventoryStatus.InTransit, quantity, ctx, ct: ct);
                    break;
            }
        }

        Guid? layerBranch = action switch
        {
            ReceivedLate => transfer.DestinationBranchId,
            ReturnedToSource => transfer.SourceBranchId,
            _ => null,
        };
        await transfers.SettleAllocationsAsync(line, quantity, layerBranch, action == ReturnedToSource ? "TRANSFER_RETURN" : "TRANSFER_IN", transfer.Id, ct);
        line.ResolvedQty += quantity;
        d.AppendNote($"{clock.UtcNow:yyyy-MM-dd HH:mm} {action} ×{quantity}: {r.Notes}");

        if (line.OutstandingQty == 0)
        {
            d.Resolve(action, d.ResolutionNotes ?? r.Notes, currentUser.UserId, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            var open = await db.Set<InventoryDiscrepancy>().AnyAsync(x => x.SourceType == "TRANSFER" && x.SourceId == transfer.Id && x.Status == DiscrepancyStatus.Open && x.Id != d.Id, ct);
            if (!open)
            {
                transfer.MarkDiscrepanciesResolved();
            }
        }
    }

    internal async Task<IReadOnlyList<DiscrepancyDto>> ToDtosAsync(IReadOnlyList<InventoryDiscrepancy> list, CancellationToken ct)
    {
        var skus = await access.SkusAsync(list.Select(d => d.SkuId).Distinct().ToList(), ct);
        var names = await access.BranchNamesAsync(ct);
        var transferIds = list.Where(d => d.SourceType == "TRANSFER").Select(d => d.SourceId).ToList();
        var lines = await db.Set<TransferLine>().AsNoTracking().Where(l => transferIds.Contains(l.TransferId)).ToListAsync(ct);
        return list.Select(d =>
        {
            var outstanding = d.Status == DiscrepancyStatus.Resolved
                ? 0
                : d.SourceType == "TRANSFER"
                    ? (db.ChangeTracker.Entries<TransferLine>().FirstOrDefault(e => e.Entity.TransferId == d.SourceId && e.Entity.SkuId == d.SkuId)?.Entity
                        ?? lines.Single(l => l.TransferId == d.SourceId && l.SkuId == d.SkuId)).OutstandingQty
                    : Math.Abs(d.Variance);
            return new DiscrepancyDto(d.Id, d.Number, d.SourceType, d.SourceId, d.SourceNumber, d.BranchId, names.GetValueOrDefault(d.BranchId, "?"), d.SkuId,
                skus[d.SkuId].SkuCode, skus[d.SkuId].ProductName, d.ExpectedQty, d.ActualQty, d.Variance, outstanding, d.MissingItemIds, d.Status.ToString(),
                d.Resolution, d.ResolutionNotes, d.CreatedAt, d.ResolvedAt);
        }).ToList();
    }

    private static NotFoundException NotFound() => new("DISCREPANCY_NOT_FOUND", "Discrepancy not found.");
}
