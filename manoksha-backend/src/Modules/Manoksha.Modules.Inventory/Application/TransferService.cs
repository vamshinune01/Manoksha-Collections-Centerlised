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
/// REQUESTED → APPROVED → PREPARED → IN_TRANSIT → RECEIVED (or DISCREPANCY until resolved) — SPEC §10, §27.5.
/// The source-branch manager approves; nobody approves their own request, except the Owner, whose self-authorization is
/// recorded explicitly (ADR-001 §7). Cost basis travels with the stock (ADR-001 §3).
/// </summary>
internal sealed class TransferService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    StockEngine engine,
    InventoryAccess access,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<IReadOnlyList<TransferDto>> ListAsync(string? status, Guid? branchId, CancellationToken ct)
    {
        var visible = await access.VisibleBranchesAsync(P.Inventory.View, ct);
        var q = db.Set<Transfer>().AsNoTracking();
        if (visible is not null)
        {
            q = q.Where(t => visible.Contains(t.SourceBranchId) || visible.Contains(t.DestinationBranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(t => t.SourceBranchId == b || t.DestinationBranchId == b);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TransferStatus>(status, true, out var st))
        {
            q = q.Where(t => t.Status == st);
        }
        var transfers = await q.OrderByDescending(t => t.RequestedAt).Take(200).ToListAsync(ct);
        return await ToDtosAsync(transfers, ct);
    }

    public async Task<TransferDto> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Set<Transfer>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (!await access.HasAsync(P.Inventory.View, t.SourceBranchId, ct) && !await access.HasAsync(P.Inventory.View, t.DestinationBranchId, ct))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You cannot view this transfer.");
        }
        return (await ToDtosAsync([t], ct))[0];
    }

    public Task<TransferDto> CreateAsync(CreateTransferRequest r, CancellationToken ct)
    {
        InventoryAccess.RequireText(r.Reason, "REASON_REQUIRED", "A reason is required for a transfer request.");
        if (r.SourceBranchId == r.DestinationBranchId)
        {
            throw new BusinessRuleException("TRANSFER_SAME_BRANCH", "Source and destination must be different branches.", 400);
        }
        var lines = r.Lines ?? [];
        if (lines.Count == 0 || lines.Select(l => l.SkuId).Distinct().Count() != lines.Count)
        {
            throw new BusinessRuleException("TRANSFER_LINES_INVALID", "Add at least one SKU, each only once.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            if (!await access.HasAsync(P.Transfers.Create, r.SourceBranchId, innerCt) && !await access.HasAsync(P.Transfers.Create, r.DestinationBranchId, innerCt))
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "You can only request transfers for your own branch.");
            }
            await access.ActiveBranchAsync(r.SourceBranchId, innerCt);
            await access.ActiveBranchAsync(r.DestinationBranchId, innerCt);
            var skus = await access.SkusAsync(lines.Select(l => l.SkuId).ToList(), innerCt);

            var transfer = new Transfer(await Numbering.NextAsync(db, "transfer_seq", "TRF", innerCt), r.SourceBranchId, r.DestinationBranchId, r.Reason.Trim(), currentUser.UserId, clock.UtcNow);
            db.Add(transfer);
            foreach (var l in lines)
            {
                db.Add(new TransferLine(transfer.Id, l.SkuId, InventoryAccess.IsSerialized(skus[l.SkuId]), l.Quantity));
            }
            await audit.RecordAsync(new AuditRecord("inventory.transfer.requested", "Transfer", transfer.Id.ToString(),
                After: new { transfer.Number, from = r.SourceBranchId, to = r.DestinationBranchId, lines }, Reason: r.Reason, BranchId: r.SourceBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([transfer], innerCt))[0];
        }, ct);
    }

    public Task<TransferDto> ApproveAsync(Guid id, DecisionRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var t = await LoadAsync(id, innerCt);
            await access.EnsureAsync(P.Transfers.Approve, t.SourceBranchId, innerCt);
            var ownerSelf = false;
            if (t.RequestedBy == currentUser.UserId)
            {
                if (!await access.IsOwnerAsync(innerCt))
                {
                    throw new ForbiddenException("SELF_APPROVAL_NOT_ALLOWED", "You cannot approve your own transfer request.");
                }
                ownerSelf = true;
            }
            t.Approve(currentUser.UserId, ownerSelf, r.Note, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("inventory.transfer.approved", "Transfer", id.ToString(),
                After: new { t.Number, requestedBy = t.RequestedBy, approvedBy = currentUser.UserId, approvedAt = t.ApprovedAt, ownerInitiatedAndAuthorized = ownerSelf },
                Reason: r.Note, BranchId: t.SourceBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([t], innerCt))[0];
        }, ct);

    public Task<TransferDto> RejectAsync(Guid id, InventoryReasonRequest r, CancellationToken ct)
    {
        InventoryAccess.RequireText(r.Reason, "REASON_REQUIRED", "A reason is required to reject.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var t = await LoadAsync(id, innerCt);
            await access.EnsureAsync(P.Transfers.Approve, t.SourceBranchId, innerCt);
            t.Reject(currentUser.UserId, r.Reason, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("inventory.transfer.rejected", "Transfer", id.ToString(), After: new { t.Number }, Reason: r.Reason, BranchId: t.SourceBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([t], innerCt))[0];
        }, ct);
    }

    /// <summary>Picks the stock: AVAILABLE → TRANSFER_PENDING at the source (quantities or scanned pieces).</summary>
    public Task<TransferDto> PrepareAsync(Guid id, LinesRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var t = await LoadAsync(id, innerCt);
            await access.EnsureAsync(P.Transfers.Dispatch, t.SourceBranchId, innerCt);
            t.MarkPrepared(currentUser.UserId, clock.UtcNow);
            var lines = await LinesAsync(t.Id, innerCt);
            var requests = (r.Lines ?? []).ToDictionary(x => x.LineId);
            var ctx = new MovementContext("TRANSFER_PREPARE", "Transfer", t.Id, t.Number, null);
            var total = 0;
            foreach (var line in lines.OrderBy(l => l.SkuId))
            {
                if (!requests.TryGetValue(line.Id, out var req))
                {
                    continue;
                }
                if (line.Serialized)
                {
                    var items = (req.ItemIds ?? []).Distinct().ToList();
                    if (items.Count > line.RequestedQty)
                    {
                        throw new BusinessRuleException("TRANSFER_QTY_EXCEEDS_REQUEST", "More pieces than requested were selected.", 400);
                    }
                    await engine.MoveItemsAsync(items, line.SkuId, t.SourceBranchId, InventoryStatus.Available, InventoryStatus.TransferPending, ctx, ct: innerCt);
                    items.ForEach(i => db.Add(new TransferLineItem(line.Id, i)));
                    line.PreparedQty = items.Count;
                }
                else
                {
                    var qty = req.Quantity ?? 0;
                    if (qty < 0 || qty > line.RequestedQty)
                    {
                        throw new BusinessRuleException("TRANSFER_QTY_EXCEEDS_REQUEST", "Prepared quantity must be between 0 and the requested quantity.", 400);
                    }
                    if (qty > 0)
                    {
                        await engine.MoveQuantityAsync(line.SkuId, t.SourceBranchId, InventoryStatus.Available, InventoryStatus.TransferPending, qty, ctx, ct: innerCt);
                    }
                    line.PreparedQty = qty;
                }
                total += line.PreparedQty;
            }
            if (total == 0)
            {
                throw new BusinessRuleException("TRANSFER_NOTHING_PREPARED", "Prepare at least one unit, or cancel the transfer.", 400);
            }
            await audit.RecordAsync(new AuditRecord("inventory.transfer.prepared", "Transfer", id.ToString(),
                After: new { t.Number, lines = lines.Select(l => new { l.SkuId, l.RequestedQty, l.PreparedQty }) }, BranchId: t.SourceBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([t], innerCt))[0];
        }, ct);

    /// <summary>TRANSFER_PENDING → IN_TRANSIT; the source's oldest FIFO layers are consumed and carried by the transfer.</summary>
    public Task<TransferDto> DispatchAsync(Guid id, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var t = await LoadAsync(id, innerCt);
            await access.EnsureAsync(P.Transfers.Dispatch, t.SourceBranchId, innerCt);
            t.MarkDispatched(currentUser.UserId, clock.UtcNow);
            var ctx = new MovementContext("TRANSFER_DISPATCH", "Transfer", t.Id, t.Number, null);
            foreach (var line in (await LinesAsync(t.Id, innerCt)).Where(l => l.PreparedQty > 0).OrderBy(l => l.SkuId))
            {
                if (line.Serialized)
                {
                    var items = await db.Set<TransferLineItem>().Where(i => i.TransferLineId == line.Id).Select(i => i.ItemId).ToListAsync(innerCt);
                    await engine.MoveItemsAsync(items, line.SkuId, t.SourceBranchId, InventoryStatus.TransferPending, InventoryStatus.InTransit, ctx, ct: innerCt);
                }
                else
                {
                    await engine.MoveQuantityAsync(line.SkuId, t.SourceBranchId, InventoryStatus.TransferPending, InventoryStatus.InTransit, line.PreparedQty, ctx, ct: innerCt);
                }
                foreach (var a in await engine.ConsumeFifoAsync(line.SkuId, t.SourceBranchId, line.PreparedQty, "TRANSFER_OUT", "Transfer", t.Id, innerCt))
                {
                    db.Add(new TransferCostAllocation(line.Id, a.LayerId, a.LayerDate, a.LayerSeq, a.UnitCost, a.Quantity));
                }
                line.DispatchedQty = line.PreparedQty;
            }
            await audit.RecordAsync(new AuditRecord("inventory.transfer.dispatched", "Transfer", id.ToString(), After: new { t.Number, dispatchedAt = t.DispatchedAt }, BranchId: t.SourceBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([t], innerCt))[0];
        }, ct);

    /// <summary>
    /// Destination verifies receipt. Matched units become AVAILABLE at the destination with their carried cost; anything
    /// missing becomes a discrepancy — the transfer never silently completes (SPEC §10).
    /// </summary>
    public Task<TransferDto> ReceiveAsync(Guid id, LinesRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var t = await LoadAsync(id, innerCt);
            await access.EnsureAsync(P.Transfers.Receive, t.DestinationBranchId, innerCt);
            var requests = (r.Lines ?? []).ToDictionary(x => x.LineId);
            var lines = await LinesAsync(t.Id, innerCt);
            var skus = await access.SkusAsync(lines.Select(l => l.SkuId).ToList(), innerCt);
            var discrepancies = new List<InventoryDiscrepancy>();
            var ctx = new MovementContext("TRANSFER_RECEIVE", "Transfer", t.Id, t.Number, null);

            foreach (var line in lines.Where(l => l.DispatchedQty > 0).OrderBy(l => l.SkuId))
            {
                requests.TryGetValue(line.Id, out var req);
                Guid[] missingItems = [];
                if (line.Serialized)
                {
                    var lineItems = await db.Set<TransferLineItem>().Where(i => i.TransferLineId == line.Id).ToListAsync(innerCt);
                    var received = (req?.ItemIds ?? []).Distinct().ToHashSet();
                    if (received.Any(id => lineItems.TrueForAll(li => li.ItemId != id)))
                    {
                        throw new BusinessRuleException("ITEM_NOT_IN_TRANSFER", "A scanned piece is not part of this transfer.", 400);
                    }
                    await engine.MoveItemsAsync(received, line.SkuId, t.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Available, ctx, t.DestinationBranchId, innerCt);
                    foreach (var li in lineItems)
                    {
                        li.Outcome = received.Contains(li.ItemId) ? "RECEIVED" : null;
                    }
                    missingItems = lineItems.Where(li => !received.Contains(li.ItemId)).Select(li => li.ItemId).ToArray();
                    line.ReceivedQty = received.Count;
                }
                else
                {
                    var qty = req?.Quantity ?? 0;
                    if (qty < 0 || qty > line.DispatchedQty)
                    {
                        throw new BusinessRuleException("TRANSFER_RECEIVED_EXCEEDS_DISPATCHED",
                            $"Received quantity for {skus[line.SkuId].SkuCode} must be between 0 and {line.DispatchedQty} (dispatched).", 400);
                    }
                    if (qty > 0)
                    {
                        await engine.MoveQuantityAsync(line.SkuId, t.SourceBranchId, InventoryStatus.InTransit, InventoryStatus.Available, qty, ctx, t.DestinationBranchId, innerCt);
                    }
                    line.ReceivedQty = qty;
                }
                await SettleAllocationsAsync(line, line.ReceivedQty, t.DestinationBranchId, "TRANSFER_IN", t.Id, innerCt);

                if (line.ReceivedQty < line.DispatchedQty)
                {
                    var d = new InventoryDiscrepancy(await Numbering.NextAsync(db, "discrepancy_seq", "DSC", innerCt), "TRANSFER", t.Id, t.Number,
                        t.DestinationBranchId, line.SkuId, line.DispatchedQty, line.ReceivedQty, missingItems, clock.UtcNow);
                    db.Add(d);
                    discrepancies.Add(d);
                }
            }

            t.MarkReceived(currentUser.UserId, discrepancies.Count > 0, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("inventory.transfer.received", "Transfer", id.ToString(),
                After: new
                {
                    t.Number,
                    status = t.Status.ToString(),
                    lines = lines.Select(l => new { l.SkuId, l.DispatchedQty, l.ReceivedQty }),
                    discrepancies = discrepancies.Select(d => d.Number),
                }, BranchId: t.DestinationBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([t], innerCt))[0];
        }, ct);

    public Task<TransferDto> CancelAsync(Guid id, InventoryReasonRequest r, CancellationToken ct)
    {
        InventoryAccess.RequireText(r.Reason, "REASON_REQUIRED", "A reason is required to cancel.");
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var t = await LoadAsync(id, innerCt);
            if (t.RequestedBy != currentUser.UserId && !await access.HasAsync(P.Transfers.Approve, t.SourceBranchId, innerCt))
            {
                throw new ForbiddenException(ErrorCodes.Forbidden, "Only the requester or a source-branch approver can cancel this transfer.");
            }
            var wasPrepared = t.Status == TransferStatus.Prepared;
            t.Cancel(currentUser.UserId, r.Reason, clock.UtcNow);
            if (wasPrepared)
            {
                var ctx = new MovementContext("TRANSFER_CANCEL_RELEASE", "Transfer", t.Id, t.Number, r.Reason);
                foreach (var line in (await LinesAsync(t.Id, innerCt)).Where(l => l.PreparedQty > 0).OrderBy(l => l.SkuId))
                {
                    if (line.Serialized)
                    {
                        var items = await db.Set<TransferLineItem>().Where(i => i.TransferLineId == line.Id).Select(i => i.ItemId).ToListAsync(innerCt);
                        await engine.MoveItemsAsync(items, line.SkuId, t.SourceBranchId, InventoryStatus.TransferPending, InventoryStatus.Available, ctx, ct: innerCt);
                    }
                    else
                    {
                        await engine.MoveQuantityAsync(line.SkuId, t.SourceBranchId, InventoryStatus.TransferPending, InventoryStatus.Available, line.PreparedQty, ctx, ct: innerCt);
                    }
                }
            }
            await audit.RecordAsync(new AuditRecord("inventory.transfer.cancelled", "Transfer", id.ToString(), After: new { t.Number, releasedStock = wasPrepared }, Reason: r.Reason, BranchId: t.SourceBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([t], innerCt))[0];
        }, ct);
    }

    /// <summary>
    /// Settles part of a line's carried cost: RECEIVED/RECEIVED_LATE/RETURNED recreate FIFO layers (original unit cost and
    /// date) at <paramref name="branchId"/>; WRITTEN_OFF records the carried cost as a loss.
    /// </summary>
    internal async Task SettleAllocationsAsync(TransferLine line, int quantity, Guid? branchId, string sourceType, Guid transferId, CancellationToken ct)
    {
        var remaining = quantity;
        var allocations = await db.Set<TransferCostAllocation>().Where(a => a.TransferLineId == line.Id).OrderBy(a => a.LayerDate).ThenBy(a => a.LayerSeq).ToListAsync(ct);
        foreach (var a in allocations)
        {
            if (remaining == 0)
            {
                break;
            }
            var take = Math.Min(remaining, a.Unsettled);
            if (take == 0)
            {
                continue;
            }
            a.SettledQty += take;
            remaining -= take;
            if (branchId is { } b)
            {
                engine.CreateLayer(line.SkuId, b, a.LayerDate, a.UnitCost, take, sourceType, transferId, a.SourceLayerId);
            }
        }
        if (remaining > 0)
        {
            throw new BusinessRuleException("COST_LAYERS_INSUFFICIENT", "Transfer cost records do not cover this quantity.", 409);
        }
    }

    internal async Task<Transfer> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<Transfer>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();

    internal Task<List<TransferLine>> LinesAsync(Guid transferId, CancellationToken ct) =>
        db.Set<TransferLine>().Where(l => l.TransferId == transferId).ToListAsync(ct);

    private async Task<IReadOnlyList<TransferDto>> ToDtosAsync(IReadOnlyList<Transfer> transfers, CancellationToken ct)
    {
        var ids = transfers.Select(t => t.Id).ToList();
        var lines = await db.Set<TransferLine>().AsNoTracking().Where(l => ids.Contains(l.TransferId)).ToListAsync(ct);
        // Include tracked (unsaved-state-reflecting) versions when present.
        lines = lines.Select(l => db.ChangeTracker.Entries<TransferLine>().FirstOrDefault(e => e.Entity.Id == l.Id)?.Entity ?? l).ToList();
        var lineIds = lines.Select(l => l.Id).ToList();
        var items = await (from li in db.Set<TransferLineItem>()
                           join i in db.Set<InventoryItem>() on li.ItemId equals i.Id
                           where lineIds.Contains(li.TransferLineId)
                           select new { li.TransferLineId, li.ItemId, i.Barcode, li.Outcome }).AsNoTracking().ToListAsync(ct);
        var skus = await access.SkusAsync(lines.Select(l => l.SkuId).Distinct().ToList(), ct);
        var names = await access.BranchNamesAsync(ct);
        return transfers.Select(t => new TransferDto(
            t.Id, t.Number, t.Status.ToString(), t.SourceBranchId, names.GetValueOrDefault(t.SourceBranchId, "?"), t.DestinationBranchId,
            names.GetValueOrDefault(t.DestinationBranchId, "?"), t.Reason, t.RequestedBy, t.RequestedAt, t.ApprovedBy, t.ApprovedAt, t.OwnerSelfAuthorized,
            t.DecisionNote, t.PreparedAt, t.DispatchedAt, t.ReceivedAt,
            lines.Where(l => l.TransferId == t.Id).OrderBy(l => skus[l.SkuId].SkuCode).Select(l => new TransferLineDto(
                l.Id, l.SkuId, skus[l.SkuId].SkuCode, skus[l.SkuId].ProductName, skus[l.SkuId].VariantName, l.Serialized, l.RequestedQty, l.PreparedQty,
                l.DispatchedQty, l.ReceivedQty, l.ResolvedQty, l.OutstandingQty,
                items.Where(i => i.TransferLineId == l.Id).Select(i => new TransferItemDto(i.ItemId, i.Barcode, i.Outcome)).ToList())).ToList())).ToList();
    }

    private static NotFoundException NotFound() => new("TRANSFER_NOT_FOUND", "Transfer not found.");
}
