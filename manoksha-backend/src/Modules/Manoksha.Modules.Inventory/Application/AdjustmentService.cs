using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Inventory.Application;

/// <summary>
/// Inventory corrections (SPEC §26 small/large adjustments; ADR-001 §14–16):
/// value at cost ≤ <see cref="SettingKeys.AdjustmentManagerMaxValue"/> → a branch approver may approve; above → Owner only.
/// The approver is always a different person from the requester — no exception, not even for the Owner.
/// </summary>
internal sealed class AdjustmentService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    StockEngine engine,
    InventoryAccess access,
    ISettingsReader settings,
    IItemBarcodeIssuer barcodes,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    private static readonly InventoryStatus[] OnHandStatuses =
        [InventoryStatus.Available, InventoryStatus.Damaged, InventoryStatus.Repair, InventoryStatus.Blocked, InventoryStatus.Lost];

    public async Task<IReadOnlyList<AdjustmentDto>> ListAsync(string? status, Guid? branchId, CancellationToken ct)
    {
        var access1 = await access.VisibleBranchesAsync(P.Inventory.AdjustRequest, ct);
        var access2 = await access.VisibleBranchesAsync(P.Inventory.AdjustApprove, ct);
        var q = db.Set<InventoryAdjustment>().AsNoTracking();
        if (access1 is not null && access2 is not null)
        {
            var visible = access1.Union(access2).ToList();
            q = q.Where(a => visible.Contains(a.BranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(a => a.BranchId == b);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AdjustmentStatus>(status, true, out var st))
        {
            q = q.Where(a => a.Status == st);
        }
        return await ToDtosAsync(await q.OrderByDescending(a => a.RequestedAt).Take(200).ToListAsync(ct), ct);
    }

    public Task<AdjustmentDto> RequestAsync(CreateAdjustmentRequest r, CancellationToken ct)
    {
        InventoryAccess.RequireText(r.ReasonCode, "REASON_REQUIRED", "A reason code is required.");
        InventoryAccess.RequireText(r.Notes, "REASON_REQUIRED", "Notes explaining the adjustment are required.");
        if (!Enum.TryParse<AdjustmentKind>(r.Kind, true, out var kind))
        {
            throw new BusinessRuleException("ADJUSTMENT_KIND_INVALID", "Kind must be StatusChange, WriteOff or Found.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await access.EnsureAsync(P.Inventory.AdjustRequest, r.BranchId, innerCt);
            await access.ActiveBranchAsync(r.BranchId, innerCt);
            var sku = (await access.SkusAsync([r.SkuId], innerCt))[r.SkuId];
            var serialized = InventoryAccess.IsSerialized(sku);
            var itemIds = (r.ItemIds ?? []).Distinct().ToArray();
            InventoryStatus? from = ParseStatus(r.FromStatus);
            InventoryStatus? to = ParseStatus(r.ToStatus);
            int quantity;

            switch (kind)
            {
                case AdjustmentKind.StatusChange:
                    if (from is null || to is null || from == to || !OnHandStatuses.Contains(from.Value) || !OnHandStatuses.Contains(to.Value))
                    {
                        throw new BusinessRuleException("ADJUSTMENT_STATUS_INVALID", "Choose two different statuses among Available, Damaged, Repair, Blocked and Lost.", 400);
                    }
                    break;
                case AdjustmentKind.WriteOff:
                    if (from is not (InventoryStatus.Damaged or InventoryStatus.Lost))
                    {
                        throw new BusinessRuleException("ADJUSTMENT_STATUS_INVALID", "Only Damaged or Lost stock can be written off.", 400);
                    }
                    to = null;
                    break;
                default:
                    from = null;
                    to = InventoryStatus.Available;
                    if (itemIds.Length > 0)
                    {
                        throw new BusinessRuleException("ADJUSTMENT_ITEMS_INVALID", "Found pieces get new identities; enter a quantity.", 400);
                    }
                    break;
            }

            if (serialized && kind != AdjustmentKind.Found)
            {
                if (itemIds.Length == 0)
                {
                    throw new BusinessRuleException("ADJUSTMENT_ITEMS_REQUIRED", "Select the specific pieces for this per-piece SKU.", 400);
                }
                quantity = itemIds.Length;
            }
            else
            {
                quantity = r.Quantity ?? 0;
                if (quantity <= 0 || itemIds.Length > 0)
                {
                    throw new BusinessRuleException("QUANTITY_INVALID", "Enter a quantity greater than zero.", 400);
                }
            }

            if (r.DiscrepancyId is { } dId)
            {
                var d = await db.Set<InventoryDiscrepancy>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == dId, innerCt)
                    ?? throw new NotFoundException("DISCREPANCY_NOT_FOUND", "Discrepancy not found.");
                if (d.Status != DiscrepancyStatus.Open || d.SourceType != "COUNT" || d.BranchId != r.BranchId || d.SkuId != r.SkuId)
                {
                    throw new BusinessRuleException("DISCREPANCY_MISMATCH", "The adjustment must match an open count discrepancy for the same branch and SKU.", 400);
                }
            }

            var adj = new InventoryAdjustment(await Numbering.NextAsync(db, "adjustment_seq", "ADJ", innerCt), r.BranchId, r.SkuId, kind, from, to, quantity, itemIds,
                r.ReasonCode.Trim(), r.Notes.Trim(), r.DiscrepancyId, currentUser.UserId, clock.UtcNow);
            db.Add(adj);
            await audit.RecordAsync(new AuditRecord("inventory.adjustment.requested", "InventoryAdjustment", adj.Id.ToString(),
                After: new { adj.Number, kind = kind.ToString(), from = from?.ToString(), to = to?.ToString(), quantity, itemIds, r.ReasonCode },
                Reason: r.Notes, BranchId: r.BranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([adj], innerCt))[0];
        }, ct);
    }

    public Task<AdjustmentDto> ApproveAsync(Guid id, ApproveAdjustmentRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var adj = await LoadAsync(id, innerCt);
            decimal? unitCost = null;
            if (adj.Kind == AdjustmentKind.Found)
            {
                unitCost = r.UnitCost ?? throw new BusinessRuleException("UNIT_COST_REQUIRED", "Enter the unit cost for the found stock.", 400);
                if (unitCost < 0 || !Money.HasValidScale(unitCost.Value))
                {
                    throw new BusinessRuleException("UNIT_COST_INVALID", "Unit cost must be a non-negative amount with at most 2 decimals.", 400);
                }
            }
            var value = Money.Round(adj.Quantity * (unitCost ?? await engine.LatestUnitCostAsync(adj.SkuId, adj.BranchId, innerCt) ?? 0m));
            var requiresOwner = await EnsureApproverAsync(adj, value, innerCt);

            await ApplyAsync(adj, unitCost, innerCt);
            adj.Apply(currentUser.UserId, r.Note, unitCost, value, requiresOwner, clock.UtcNow);

            if (adj.DiscrepancyId is { } dId)
            {
                var d = await db.Set<InventoryDiscrepancy>().SingleAsync(x => x.Id == dId, innerCt);
                if (d.Status == DiscrepancyStatus.Open)
                {
                    d.Resolve("ADJUSTED", $"Resolved by {adj.Number}", currentUser.UserId, clock.UtcNow);
                }
            }
            await audit.RecordAsync(new AuditRecord("inventory.adjustment.approved", "InventoryAdjustment", id.ToString(),
                After: new { adj.Number, requestedBy = adj.RequestedBy, approvedBy = currentUser.UserId, valueAtCost = value, unitCost, requiredOwner = requiresOwner },
                Reason: r.Note, BranchId: adj.BranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([adj], innerCt))[0];
        }, ct);

    public Task<AdjustmentDto> RejectAsync(Guid id, InventoryReasonRequest r, CancellationToken ct)
    {
        InventoryAccess.RequireText(r.Reason, "REASON_REQUIRED", "A reason is required to reject.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var adj = await LoadAsync(id, innerCt);
            var value = Money.Round(adj.Quantity * (await engine.LatestUnitCostAsync(adj.SkuId, adj.BranchId, innerCt) ?? 0m));
            await EnsureApproverAsync(adj, adj.Kind == AdjustmentKind.Found ? 0 : value, innerCt);
            adj.Reject(currentUser.UserId, r.Reason, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("inventory.adjustment.rejected", "InventoryAdjustment", id.ToString(), After: new { adj.Number }, Reason: r.Reason, BranchId: adj.BranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([adj], innerCt))[0];
        }, ct);
    }

    /// <returns>True when the Owner's authority was required (value above the configured branch limit).</returns>
    private async Task<bool> EnsureApproverAsync(InventoryAdjustment adj, decimal value, CancellationToken ct)
    {
        if (adj.RequestedBy == currentUser.UserId)
        {
            throw new ForbiddenException("SELF_APPROVAL_NOT_ALLOWED", "You cannot approve or reject your own adjustment request; another authorized person must decide.");
        }
        var limit = await settings.GetAsync<decimal>(SettingKeys.AdjustmentManagerMaxValue, ct);
        var requiresOwner = value > limit;
        if (requiresOwner)
        {
            if (!await access.IsOwnerAsync(ct))
            {
                var ex = new ForbiddenException("OWNER_APPROVAL_REQUIRED", $"This adjustment (₹{value:N2} at cost) exceeds the branch approval limit (₹{limit:N2}); only the Owner can approve it.");
                ex.Details["valueAtCost"] = value;
                ex.Details["limit"] = limit;
                throw ex;
            }
        }
        else
        {
            await access.EnsureAsync(P.Inventory.AdjustApprove, adj.BranchId, ct);
        }
        return requiresOwner;
    }

    private async Task ApplyAsync(InventoryAdjustment adj, decimal? unitCost, CancellationToken ct)
    {
        var sku = (await access.SkusAsync([adj.SkuId], ct))[adj.SkuId];
        var serialized = InventoryAccess.IsSerialized(sku);
        var ctx = new MovementContext($"ADJUSTMENT_{adj.Kind.ToString().ToUpperInvariant()}", "InventoryAdjustment", adj.Id, adj.Number, adj.Notes);
        switch (adj.Kind)
        {
            case AdjustmentKind.StatusChange when serialized:
                await engine.MoveItemsAsync(adj.ItemIds, adj.SkuId, adj.BranchId, adj.FromStatus!.Value, adj.ToStatus!.Value, ctx, ct: ct);
                break;
            case AdjustmentKind.StatusChange:
                await engine.MoveQuantityAsync(adj.SkuId, adj.BranchId, adj.FromStatus!.Value, adj.ToStatus!.Value, adj.Quantity, ctx, ct: ct);
                break;
            case AdjustmentKind.WriteOff:
                if (serialized)
                {
                    await engine.WriteOffItemsAsync(adj.ItemIds, adj.SkuId, adj.BranchId, adj.FromStatus!.Value, ctx, ct);
                }
                else
                {
                    await engine.RemoveQuantityAsync(adj.SkuId, adj.BranchId, adj.FromStatus!.Value, adj.Quantity, ctx, ct: ct);
                }
                await engine.ConsumeFifoAsync(adj.SkuId, adj.BranchId, adj.Quantity, "WRITE_OFF", "InventoryAdjustment", adj.Id, ct);
                break;
            case AdjustmentKind.Found:
                engine.CreateLayer(adj.SkuId, adj.BranchId, clock.UtcNow, unitCost!.Value, adj.Quantity, "ADJUSTMENT_FOUND", adj.Id);
                if (serialized)
                {
                    for (var i = 0; i < adj.Quantity; i++)
                    {
                        var itemId = Uuid7.NewGuid();
                        var issued = await barcodes.IssueForItemAsync(adj.SkuId, itemId, ct);
                        engine.CreateItem(itemId, adj.SkuId, adj.BranchId, InventoryStatus.Available, issued.Code, ctx);
                    }
                }
                else
                {
                    await engine.AddQuantityAsync(adj.SkuId, adj.BranchId, InventoryStatus.Available, adj.Quantity, ctx, ct: ct);
                }
                break;
        }
    }

    private async Task<InventoryAdjustment> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<InventoryAdjustment>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("ADJUSTMENT_NOT_FOUND", "Adjustment not found.");

    private static InventoryStatus? ParseStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : Enum.TryParse<InventoryStatus>(value, true, out var s) ? s
        : throw new BusinessRuleException("ADJUSTMENT_STATUS_INVALID", $"Unknown status '{value}'.", 400);

    private async Task<IReadOnlyList<AdjustmentDto>> ToDtosAsync(IReadOnlyList<InventoryAdjustment> list, CancellationToken ct)
    {
        var skus = await access.SkusAsync(list.Select(a => a.SkuId).Distinct().ToList(), ct);
        var names = await access.BranchNamesAsync(ct);
        var limit = await settings.GetAsync<decimal>(SettingKeys.AdjustmentManagerMaxValue, ct);
        var result = new List<AdjustmentDto>();
        foreach (var a in list)
        {
            decimal? estimate = null;
            if (a.Status == AdjustmentStatus.Pending && a.Kind != AdjustmentKind.Found)
            {
                estimate = Money.Round(a.Quantity * (await engine.LatestUnitCostAsync(a.SkuId, a.BranchId, ct) ?? 0m));
            }
            result.Add(new AdjustmentDto(a.Id, a.Number, a.BranchId, names.GetValueOrDefault(a.BranchId, "?"), a.SkuId, skus[a.SkuId].SkuCode, skus[a.SkuId].ProductName,
                a.Kind.ToString(), a.FromStatus?.ToString(), a.ToStatus?.ToString(), a.Quantity, a.ItemIds, a.ReasonCode, a.Notes, a.DiscrepancyId, a.Status.ToString(),
                a.RequestedBy, a.RequestedAt, a.DecidedBy, a.DecidedAt, a.DecisionNote, a.UnitCost, a.ValueAtCost, estimate,
                a.Status == AdjustmentStatus.Pending ? (a.Kind == AdjustmentKind.Found ? null : estimate > limit) : a.RequiredOwner));
        }
        return result;
    }
}
