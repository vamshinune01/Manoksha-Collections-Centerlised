using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Purchasing.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Purchasing.Application;

/// <summary>
/// Purchase orders (SPEC §8): Owner-controlled unless purchasing.manage is delegated. Receiving staff see issued POs for their
/// branch (without expected costs) so they can record goods receipts.
/// </summary>
internal sealed class PurchaseOrderService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IPermissionService permissions,
    IBranchDirectory branches,
    ICatalogLookup catalog,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<IReadOnlyList<PurchaseOrderDto>> ListAsync(string? status, Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var q = db.Set<PurchaseOrder>().AsNoTracking();
        var canView = access.Has(P.Purchasing.View);
        if (!canView)
        {
            // Receiving staff: only receivable POs of branches where they may record goods receipts.
            var branchesAllowed = access.BranchesWith(P.Purchasing.GoodsReceiptRecord);
            if (branchesAllowed is not null)
            {
                q = q.Where(po => branchesAllowed.Contains(po.ReceivingBranchId));
            }
            q = q.Where(po => po.Status == PurchaseOrderStatus.Issued || po.Status == PurchaseOrderStatus.PartiallyReceived);
        }
        if (branchId is { } b)
        {
            q = q.Where(po => po.ReceivingBranchId == b);
        }
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PurchaseOrderStatus>(status, true, out var st))
        {
            q = q.Where(po => po.Status == st);
        }
        return await ToDtosAsync(await q.OrderByDescending(po => po.CreatedAt).Take(200).ToListAsync(ct), canView, ct);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var po = await db.Set<PurchaseOrder>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        var canView = await permissions.HasPermissionAsync(P.Purchasing.View, ct);
        if (!canView && !await permissions.HasPermissionForBranchAsync(P.Purchasing.GoodsReceiptRecord, po.ReceivingBranchId, ct))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You cannot view this purchase order.");
        }
        return (await ToDtosAsync([po], canView, ct))[0];
    }

    public Task<PurchaseOrderDto> CreateAsync(CreatePurchaseOrderRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        var lines = r.Lines ?? [];
        if (lines.Count == 0 || lines.Select(l => l.SkuId).Distinct().Count() != lines.Count)
        {
            throw new BusinessRuleException("PO_LINES_INVALID", "Add at least one SKU, each only once.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureSupplierAsync(r.SupplierId, innerCt);
            await EnsureBranchAsync(r.ReceivingBranchId, innerCt);
            await EnsureSkusAsync(lines.Select(l => l.SkuId).ToList(), innerCt);
            var seq = await db.Database.SqlQuery<long>($"SELECT nextval('purchasing.po_seq') AS \"Value\"").SingleAsync(innerCt);
            var po = new PurchaseOrder($"PO-{seq:D6}", r.SupplierId, r.ReceivingBranchId, r.SupplierReference?.Trim(), r.ExpectedDate, r.Notes?.Trim(), currentUser.UserId, clock.UtcNow);
            db.Add(po);
            foreach (var l in lines)
            {
                db.Add(new PurchaseOrderLine(po.Id, l.SkuId, l.OrderedQty, l.ExpectedUnitCost));
            }
            await audit.RecordAsync(new AuditRecord("purchasing.po.created", "PurchaseOrder", po.Id.ToString(),
                After: new { po.Number, r.SupplierId, r.ReceivingBranchId, lines }, Reason: r.Reason, BranchId: r.ReceivingBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([po], true, innerCt))[0];
        }, ct);
    }

    public Task<PurchaseOrderDto> UpdateAsync(Guid id, UpdatePurchaseOrderRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var po = await LoadAsync(id, innerCt);
            await EnsureSupplierAsync(r.SupplierId, innerCt);
            await EnsureBranchAsync(r.ReceivingBranchId, innerCt);
            var before = new { po.SupplierId, po.ReceivingBranchId, po.SupplierReference, po.ExpectedDate, po.Notes };
            po.UpdateHeader(r.SupplierId, r.ReceivingBranchId, r.SupplierReference?.Trim(), r.ExpectedDate, r.Notes?.Trim());
            await audit.RecordAsync(new AuditRecord("purchasing.po.updated", "PurchaseOrder", id.ToString(), before,
                new { po.SupplierId, po.ReceivingBranchId, po.SupplierReference, po.ExpectedDate, po.Notes }, r.Reason, po.ReceivingBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([po], true, innerCt))[0];
        }, ct);
    }

    /// <summary>Changes a line's quantity/cost or adds a line — the only way to receive more than originally ordered.</summary>
    public Task<PurchaseOrderDto> AmendLineAsync(Guid id, AmendLineRequest r, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var po = await LoadAsync(id, innerCt);
            if (!po.IsAmendable)
            {
                throw new BusinessRuleException("PO_STATUS_INVALID", $"A {po.Status} purchase order cannot be amended.");
            }
            object before;
            PurchaseOrderLine line;
            if (r.LineId is { } lineId)
            {
                line = await db.Set<PurchaseOrderLine>().SingleOrDefaultAsync(l => l.Id == lineId && l.PurchaseOrderId == id, innerCt)
                    ?? throw new NotFoundException("PO_LINE_NOT_FOUND", "PO line not found.");
                before = new { line.SkuId, line.OrderedQty, line.ExpectedUnitCost };
                line.SetOrdered(r.OrderedQty);
                line.SetExpectedCost(r.ExpectedUnitCost);
            }
            else
            {
                var skuId = r.SkuId ?? throw new BusinessRuleException("PO_LINES_INVALID", "Choose a SKU for the new line.", 400);
                await EnsureSkusAsync([skuId], innerCt);
                if (await db.Set<PurchaseOrderLine>().AnyAsync(l => l.PurchaseOrderId == id && l.SkuId == skuId, innerCt))
                {
                    throw new ConflictException("PO_LINE_EXISTS", "This SKU is already on the PO; amend its line instead.");
                }
                before = new { };
                line = new PurchaseOrderLine(id, skuId, r.OrderedQty, r.ExpectedUnitCost);
                db.Add(line);
            }
            po.ReopenAfterAmendment();
            await audit.RecordAsync(new AuditRecord("purchasing.po.amended", "PurchaseOrder", id.ToString(), before,
                new { line.Id, line.SkuId, line.OrderedQty, line.ExpectedUnitCost }, r.Reason, po.ReceivingBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            await RefreshStatusAsync(po, innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([po], true, innerCt))[0];
        }, ct);
    }

    public Task<PurchaseOrderDto> IssueAsync(Guid id, PoReasonRequest r, CancellationToken ct) =>
        TransitionAsync(id, r, "purchasing.po.issued", po => po.Issue(currentUser.UserId, clock.UtcNow), ct);

    public Task<PurchaseOrderDto> CloseAsync(Guid id, PoReasonRequest r, CancellationToken ct) =>
        TransitionAsync(id, r, "purchasing.po.closed", po => po.Close(currentUser.UserId, r.Reason, clock.UtcNow), ct);

    public Task<PurchaseOrderDto> CancelAsync(Guid id, PoReasonRequest r, CancellationToken ct) =>
        unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            RequireReason(r.Reason);
            var po = await LoadAsync(id, innerCt);
            var received = await db.Set<PurchaseOrderLine>().AnyAsync(l => l.PurchaseOrderId == id && l.ReceivedQty > 0, innerCt);
            po.Cancel(currentUser.UserId, r.Reason, received, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("purchasing.po.cancelled", "PurchaseOrder", id.ToString(), After: new { po.Number }, Reason: r.Reason, BranchId: po.ReceivingBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([po], true, innerCt))[0];
        }, ct);

    private Task<PurchaseOrderDto> TransitionAsync(Guid id, PoReasonRequest r, string action, Action<PurchaseOrder> change, CancellationToken ct)
    {
        RequireReason(r.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var po = await LoadAsync(id, innerCt);
            var before = po.Status.ToString();
            change(po);
            await audit.RecordAsync(new AuditRecord(action, "PurchaseOrder", id.ToString(), new { status = before }, new { status = po.Status.ToString() }, r.Reason, po.ReceivingBranchId), innerCt);
            await db.SaveChangesAsync(innerCt);
            return (await ToDtosAsync([po], true, innerCt))[0];
        }, ct);
    }

    internal async Task RefreshStatusAsync(PurchaseOrder po, CancellationToken ct)
    {
        if (po.Status is not (PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received or PurchaseOrderStatus.Issued))
        {
            return;
        }
        var lines = await db.Set<PurchaseOrderLine>().Where(l => l.PurchaseOrderId == po.Id).ToListAsync(ct);
        if (lines.Exists(l => l.ReceivedQty > 0))
        {
            po.RefreshReceiptStatus(lines.TrueForAll(l => l.RemainingQty == 0));
        }
    }

    internal async Task<PurchaseOrder> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Set<PurchaseOrder>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();

    private async Task EnsureSupplierAsync(Guid supplierId, CancellationToken ct)
    {
        var s = await db.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == supplierId, ct) ?? throw new NotFoundException("SUPPLIER_NOT_FOUND", "Supplier not found.");
        if (!s.IsActive)
        {
            throw new BusinessRuleException("SUPPLIER_INACTIVE", "The supplier is inactive.");
        }
    }

    private async Task EnsureBranchAsync(Guid branchId, CancellationToken ct)
    {
        var b = await branches.FindAsync(branchId, ct) ?? throw new NotFoundException("BRANCH_NOT_FOUND", "Branch not found.");
        if (!b.IsActive)
        {
            throw new BusinessRuleException("BRANCH_INACTIVE", $"Branch {b.Name} is inactive.");
        }
    }

    private async Task EnsureSkusAsync(IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var found = await catalog.FindSkusAsync(skuIds, ct);
        if (found.Count != skuIds.Distinct().Count())
        {
            throw new NotFoundException("SKU_NOT_FOUND", "One or more SKUs were not found.");
        }
    }

    private async Task<IReadOnlyList<PurchaseOrderDto>> ToDtosAsync(IReadOnlyList<PurchaseOrder> orders, bool showCosts, CancellationToken ct)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var lines = await db.Set<PurchaseOrderLine>().AsNoTracking().Where(l => ids.Contains(l.PurchaseOrderId)).ToListAsync(ct);
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).Distinct().ToList(), ct);
        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        var suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var names = (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return orders.Select(o =>
        {
            var own = lines.Where(l => l.PurchaseOrderId == o.Id).ToList();
            return new PurchaseOrderDto(o.Id, o.Number, o.Status.ToString(), o.SupplierId, suppliers.GetValueOrDefault(o.SupplierId, "?"), o.ReceivingBranchId,
                names.GetValueOrDefault(o.ReceivingBranchId, "?"), o.SupplierReference, o.ExpectedDate, o.Notes, o.CreatedAt, o.IssuedAt, o.ClosedAt, o.CloseReason,
                showCosts ? Money.Round(own.Sum(l => l.OrderedQty * l.ExpectedUnitCost)) : null,
                own.OrderBy(l => skus[l.SkuId].SkuCode).Select(l => new PoLineDto(l.Id, l.SkuId, skus[l.SkuId].SkuCode, skus[l.SkuId].ProductName, skus[l.SkuId].VariantName,
                    skus[l.SkuId].TrackingMode, l.OrderedQty, showCosts ? l.ExpectedUnitCost : null, l.ReceivedQty, l.DamagedQty, l.RemainingQty)).ToList());
        }).ToList();
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    private static NotFoundException NotFound() => new("PO_NOT_FOUND", "Purchase order not found.");
}
