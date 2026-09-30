using System.Globalization;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Orders.Application;

/// <param name="ItemIds">The scanned pieces for individually tracked (serialized) items; one per unit.</param>
/// <param name="UnitPrice">Negotiated unit price (≤ retail); omit to sell at retail.</param>
public sealed record PosLineRequest(Guid SkuId, int Quantity, IReadOnlyList<Guid>? ItemIds, decimal? UnitPrice, string? PriceReason);

public sealed record PosApprovalRequest(Guid ApproverUserId, string Pin);

public sealed record PosPaymentRequest(string Method, decimal Amount, string? Reference);

public sealed record PosSaleRequest(Guid BranchId, IReadOnlyList<PosLineRequest> Lines, IReadOnlyList<PosPaymentRequest>? Payments, PosApprovalRequest? Approval,
    string? CustomerName, string? CustomerMobile);

public sealed record PosQuoteLineDto(Guid SkuId, string SkuCode, string ProductName, string VariantName, int Quantity, decimal RetailUnitPrice, decimal FinalUnitPrice,
    decimal DiscountPct, decimal LineTotal);

/// <param name="ApprovalRequired">NONE, MANAGER or OWNER for the largest discount in the basket.</param>
public sealed record PosQuoteDto(IReadOnlyList<PosQuoteLineDto> Lines, decimal RetailTotal, decimal DiscountTotal, decimal GrandTotal, string ApprovalRequired,
    decimal MaxDiscountPct, decimal SellerLimitPct);

public sealed record PosBranchDto(Guid Id, string Code, string Name, bool CanOverridePrice, bool CanApprove);

public sealed record PosContextDto(Guid UserId, string DisplayName, bool IsOwner, IReadOnlyList<PosBranchDto> Branches, decimal StaffMaxDiscountPct,
    decimal ManagerMaxDiscountPct, IReadOnlyList<string> PaymentMethods, string ShopName);

public sealed record PosReceiptLineDto(string Name, string SkuCode, int Quantity, decimal RetailUnitPrice, decimal FinalUnitPrice, decimal LineTotal);

public sealed record PosPaymentDto(string Method, decimal Amount, string? Reference);

public sealed record PosReceiptDto(Guid OrderId, string Number, string ShopName, string BranchName, DateTimeOffset SoldAt, string Cashier, string? CustomerName,
    string? CustomerMobile, IReadOnlyList<PosReceiptLineDto> Lines, decimal RetailTotal, decimal DiscountTotal, decimal GrandTotal, IReadOnlyList<PosPaymentDto> Payments,
    string? ApprovedBy);

public sealed record PosSaleResult(Guid OrderId, string Number, decimal GrandTotal);

public sealed record PosSearchItemDto(Guid SkuId, string SkuCode, Guid ProductId, string ProductName, string VariantName, string TrackingMode, decimal Price, int AvailableHere);

public sealed record PosSaleSummaryDto(Guid OrderId, string Number, DateTimeOffset SoldAt, decimal GrandTotal, int Items, Guid CashierUserId);

/// <summary>
/// POS store sales (SPEC §19.3, §20, §23, §32; ADR-001 §9; Phase 8 decisions): retail pricing with authorized bargaining (seller's own
/// limit, manager approval, Owner approval above the manager limit — PIN on the device, never self-approval), split payments that must
/// add up, and a single finalize transaction that marks the exact pieces SOLD. Internet is mandatory: nothing is sold unless the
/// backend commits, and the Idempotency-Key makes a retry after a dropped connection one sale.
/// </summary>
internal sealed class PosSaleService(
    ManokshaDbContext db,
    IIdempotencyService idempotency,
    IPermissionService permissions,
    IApprovals approvals,
    IPriceCalculator prices,
    ICatalogLookup catalog,
    IStockAllocator allocator,
    IBranchDirectory branches,
    IOrderStock orderStock,
    ISettingsReader settings,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    public const string ShopName = "Manoksha Collections";
    private const int MaxLines = 100;

    public async Task<PosContextDto> ContextAsync(CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var list = (await branches.ListAsync(ct)).Where(b => b.IsActive && access.HasForBranch(P.Pos.Sell, b.Id))
            .Select(b => new PosBranchDto(b.Id, b.Code, b.Name, access.HasForBranch(P.Pos.PriceOverride, b.Id), access.HasForBranch(P.Pos.PriceOverrideApprove, b.Id)))
            .ToList();
        var (staff, manager) = await LimitsAsync(ct);
        return new PosContextDto(currentUser.UserId, await approvals.DisplayNameAsync(currentUser.UserId, ct), access.IsOwner, list, staff, manager,
            await MethodsAsync(ct), ShopName);
    }

    public async Task<PosQuoteDto> QuoteAsync(PosSaleRequest request, CancellationToken ct)
    {
        await permissions.EnsurePermissionForBranchAsync(P.Pos.Sell, request.BranchId, ct);
        return (await EvaluateAsync(request, ct)).Quote;
    }

    public async Task<IReadOnlyList<ApproverOption>> ApproversAsync(Guid branchId, string level, CancellationToken ct)
    {
        await permissions.EnsurePermissionForBranchAsync(P.Pos.Sell, branchId, ct);
        var ownerOnly = string.Equals(level, "OWNER", StringComparison.OrdinalIgnoreCase);
        return (await approvals.ListApproversAsync(P.Pos.PriceOverrideApprove, branchId, ownerOnly, ct)).Where(a => a.UserId != currentUser.UserId).ToList();
    }

    public async Task<PosSaleResult> SellAsync(PosSaleRequest request, string idempotencyKey, CancellationToken ct)
    {
        await permissions.EnsurePermissionForBranchAsync(P.Pos.Sell, request.BranchId, ct);
        var eval = await EvaluateAsync(request, ct);

        // Approval is verified before the sale transaction so a wrong PIN is always counted (and never rolled back).
        ApproverInfo? approver = null;
        if (eval.Quote.ApprovalRequired != "NONE")
        {
            if (request.Approval is null)
            {
                var ex = new BusinessRuleException("APPROVAL_REQUIRED",
                    eval.Quote.ApprovalRequired == "OWNER" ? "This discount needs the Owner's approval." : "This discount needs a manager's approval.", 409);
                ex.Details["level"] = eval.Quote.ApprovalRequired;
                throw ex;
            }
            if (request.Approval.ApproverUserId == currentUser.UserId)
            {
                throw new ForbiddenException("SELF_APPROVAL_NOT_ALLOWED", "Someone else must approve your discount.");
            }
            approver = await approvals.VerifyAsync(request.Approval.ApproverUserId, request.Approval.Pin, P.Pos.PriceOverrideApprove, request.BranchId,
                ownerOnly: eval.Quote.ApprovalRequired == "OWNER", ct);
        }
        var payments = await ValidatePaymentsAsync(request.Payments, eval.Quote.GrandTotal, ct);
        var (customerName, customerMobile) = Customer(request);

        return await idempotency.ExecuteAsync("pos.sale", idempotencyKey, request with { Approval = request.Approval is null ? null : request.Approval with { Pin = "***" } },
            async innerCt =>
            {
                var branch = await branches.FindAsync(request.BranchId, innerCt) ?? throw new NotFoundException("BRANCH_NOT_FOUND", "Branch not found.");
                if (!branch.IsActive)
                {
                    throw new BusinessRuleException("BRANCH_INACTIVE", "This branch is inactive.", 409);
                }
                var now = clock.UtcNow;
                var seq = await db.Database.SqlQuery<long>($"SELECT nextval('orders.order_number_seq') AS \"Value\"").SingleAsync(innerCt);
                var number = $"MC-POS-{seq:D6}";
                var delivery = new DeliveryDetails(customerName ?? "Walk-in customer", customerMobile ?? string.Empty, null, "In-store purchase", branch.Name, "-", "-");
                var order = new Order(Uuid7.NewGuid(), number, OrderChannel.Store, null, null, branch.Id, delivery, eval.Quote.GrandTotal, 0m, currentUser.UserId, now);
                order.CompleteStoreSale(now);
                db.Add(order);
                await db.SaveChangesAsync(innerCt);

                var sold = await allocator.SellAvailableAsync(branch.Id,
                    eval.Lines.Select(l => new ReservedLine(l.Sku.SkuId, l.Quantity, l.ItemIds)).ToList(), "Order", order.Id, number, innerCt);
                var overrides = new List<object>();
                foreach (var l in eval.Lines)
                {
                    var s = sold.Single(x => x.SkuId == l.Sku.SkuId);
                    var discounted = l.Final < l.Retail;
                    var line = new OrderLine(order.Id, l.Sku.SkuId, l.Sku.SkuCode, l.Sku.ProductName, l.Sku.VariantName, l.Quantity, l.RetailPriceId, l.Retail,
                        discounted ? "POS_NEGOTIATED" : DiscountSources.None, l.Pct, l.Final, null, null, null, s.CostAmount, [.. s.ItemIds]);
                    db.Add(line);
                    if (discounted)
                    {
                        var level = approver is null ? "SELLER" : eval.Quote.ApprovalRequired;
                        db.Add(new PosPriceOverride(order.Id, line.Id, branch.Id, l.Sku.SkuId, l.Retail, l.Final, l.Pct, l.Quantity, l.Reason!, level, currentUser.UserId,
                            approver?.UserId, now));
                        overrides.Add(new { l.Sku.SkuCode, original = l.Retail, final = l.Final, pct = l.Pct, l.Reason, level, approver = approver?.UserId });
                    }
                }
                foreach (var p in payments)
                {
                    db.Add(new PosPayment(order.Id, p.Method, p.Amount, p.Reference, currentUser.UserId, now));
                }
                db.Add(new OrderStatusChange(order.Id, null, OrderStatus.Completed, currentUser.UserId, "Store sale finalized", now));
                await audit.RecordAsync(new AuditRecord("orders.pos_sale.completed", "Order", order.Id.ToString(),
                    After: new
                    {
                        number,
                        order.GrandTotal,
                        eval.Quote.RetailTotal,
                        eval.Quote.DiscountTotal,
                        payments = payments.Select(p => new { p.Method, p.Amount, p.Reference }),
                        overrides,
                        approvedBy = approver?.UserId,
                    }, BranchId: branch.Id), innerCt);
                outbox.Enqueue(new OrderStatusChanged(order.Id, number, OrderChannel.Store.ToString(), OrderStatus.Completed.ToString(), branch.Id));
                await db.SaveChangesAsync(innerCt);
                return new PosSaleResult(order.Id, number, order.GrandTotal);
            }, ct);
    }

    public async Task<PosReceiptDto> ReceiptAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Set<Order>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.Channel == OrderChannel.Store, ct)
                    ?? throw new NotFoundException("SALE_NOT_FOUND", "Sale not found.");
        var access = await permissions.GetEffectiveAccessAsync(ct);
        if (!access.HasForBranch(P.Pos.Sell, order.FulfillmentBranchId) && !access.HasForBranch(P.Orders.View, order.FulfillmentBranchId))
        {
            throw new NotFoundException("SALE_NOT_FOUND", "Sale not found.");
        }
        var lines = await db.Set<OrderLine>().AsNoTracking().Where(l => l.OrderId == orderId).OrderBy(l => l.SkuCode).ToListAsync(ct);
        var pays = await db.Set<PosPayment>().AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.RecordedAt).ToListAsync(ct);
        var approverId = await db.Set<PosPriceOverride>().AsNoTracking().Where(o => o.OrderId == orderId && o.ApproverUserId != null).Select(o => o.ApproverUserId).FirstOrDefaultAsync(ct);
        var branch = await branches.FindAsync(order.FulfillmentBranchId, ct);
        var retailTotal = lines.Sum(l => l.RetailUnitPrice * l.Quantity);
        var walkIn = order.Delivery.Name == "Walk-in customer";
        return new PosReceiptDto(order.Id, order.Number, ShopName, branch?.Name ?? "?", order.ConfirmedAt ?? order.CreatedAt, await approvals.DisplayNameAsync(order.PlacedBy, ct),
            walkIn ? null : order.Delivery.Name, string.IsNullOrEmpty(order.Delivery.Mobile) ? null : MobileNumber.Mask(order.Delivery.Mobile),
            lines.Select(l => new PosReceiptLineDto($"{l.ProductName} · {l.VariantName}", l.SkuCode, l.Quantity, l.RetailUnitPrice, l.FinalUnitPrice, l.LineTotal)).ToList(),
            retailTotal, retailTotal - order.GrandTotal, order.GrandTotal, pays.Select(p => new PosPaymentDto(p.Method, p.Amount, p.Reference)).ToList(),
            approverId is { } a ? await approvals.DisplayNameAsync(a, ct) : null);
    }

    /// <summary>Find items by name or SKU code (for items without a readable label), with the stock available at this branch.</summary>
    public async Task<IReadOnlyList<PosSearchItemDto>> SearchAsync(Guid branchId, string? q, CancellationToken ct)
    {
        await permissions.EnsurePermissionForBranchAsync(P.Pos.Sell, branchId, ct);
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return [];
        }
        var page = await catalog.ListSellableSkusAsync(SalesChannel.Retail, q, null, 1, 30, ct);
        var ids = page.Items.Select(i => i.Sku.SkuId).ToList();
        var retail = await prices.GetRetailPricesAsync(ids, ct);
        var here = await orderStock.AvailableAtBranchAsync(branchId, ids, ct);
        return page.Items.Where(i => retail.ContainsKey(i.Sku.SkuId)).Select(i => new PosSearchItemDto(i.Sku.SkuId, i.Sku.SkuCode, i.Sku.ProductId, i.Sku.ProductName,
            i.Sku.VariantName, i.Sku.TrackingMode, retail[i.Sku.SkuId].Price, here.GetValueOrDefault(i.Sku.SkuId))).ToList();
    }

    /// <summary>Today's (IST) store sales at a branch: approvers see the whole branch, sellers their own sales.</summary>
    public async Task<IReadOnlyList<PosSaleSummaryDto>> TodayAsync(Guid branchId, CancellationToken ct)
    {
        await permissions.EnsurePermissionForBranchAsync(P.Pos.Sell, branchId, ct);
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var ist = TimeSpan.FromHours(5.5);
        var localNow = clock.UtcNow.ToOffset(ist);
        var start = new DateTimeOffset(localNow.Date, ist).ToUniversalTime();
        var q = db.Set<Order>().AsNoTracking().Where(o => o.Channel == OrderChannel.Store && o.FulfillmentBranchId == branchId && o.CreatedAt >= start);
        if (!access.HasForBranch(P.Pos.PriceOverrideApprove, branchId))
        {
            var me = currentUser.UserId;
            q = q.Where(o => o.PlacedBy == me);
        }
        var orders = await q.OrderByDescending(o => o.CreatedAt).Take(500).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var counts = await db.Set<OrderLine>().AsNoTracking().Where(l => ids.Contains(l.OrderId)).GroupBy(l => l.OrderId)
            .Select(g => new { g.Key, Items = g.Sum(l => l.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Items, ct);
        return orders.Select(o => new PosSaleSummaryDto(o.Id, o.Number, o.CreatedAt, o.GrandTotal, counts.GetValueOrDefault(o.Id), o.PlacedBy)).ToList();
    }

    // ---- evaluation ----

    private sealed record EvaluatedLine(SkuInfo Sku, int Quantity, Guid[] ItemIds, Guid RetailPriceId, decimal Retail, decimal Final, decimal Pct, string? Reason);

    private sealed record Evaluation(PosQuoteDto Quote, IReadOnlyList<EvaluatedLine> Lines);

    private async Task<Evaluation> EvaluateAsync(PosSaleRequest request, CancellationToken ct)
    {
        var lines = (request.Lines ?? []).ToList();
        if (lines.Count is 0 or > MaxLines || lines.Select(l => l.SkuId).Distinct().Count() != lines.Count)
        {
            throw new BusinessRuleException("CART_INVALID", $"Add 1–{MaxLines} different items (scan the same item again to add quantity).", 400);
        }
        if (lines.Exists(l => l.Quantity is < 1 or > 1000))
        {
            throw new BusinessRuleException("QUANTITY_INVALID", "Each quantity must be between 1 and 1000.", 400);
        }
        var ids = lines.Select(l => l.SkuId).ToList();
        var skus = await catalog.FindSkusAsync(ids, ct);
        var retail = (await prices.QuoteForRetailAsync(ids, ct)).ToDictionary(q => q.SkuId);
        var evaluated = new List<EvaluatedLine>();
        foreach (var l in lines)
        {
            var sku = skus[l.SkuId];
            var items = (l.ItemIds ?? []).ToArray();
            if (sku.TrackingMode == "Serialized" && (items.Length != l.Quantity || items.Distinct().Count() != items.Length))
            {
                throw new BusinessRuleException("ITEMS_REQUIRED", $"{sku.ProductName}: scan each piece (one barcode per unit).", 400);
            }
            if (sku.TrackingMode != "Serialized" && items.Length > 0)
            {
                throw new BusinessRuleException("ITEMS_NOT_ALLOWED", $"{sku.ProductName} is sold by quantity.", 400);
            }
            var r = retail[l.SkuId];
            var final = l.UnitPrice ?? r.Price;
            if (final < 0 || final > r.Price || !Money.HasValidScale(final))
            {
                throw new BusinessRuleException("PRICE_INVALID", $"{sku.ProductName}: the price must be between ₹0 and the retail price ₹{r.Price:N2}.", 400);
            }
            var pct = r.Price == 0 ? 0 : Math.Round((r.Price - final) / r.Price * 100m, 4, MidpointRounding.AwayFromZero);
            var reason = l.PriceReason?.Trim();
            if (final < r.Price && reason is not { Length: >= 3 })
            {
                throw new BusinessRuleException("PRICE_REASON_REQUIRED", $"{sku.ProductName}: give a reason for the discount.", 400);
            }
            evaluated.Add(new EvaluatedLine(sku, l.Quantity, items, r.RetailPriceId, r.Price, final, pct, final < r.Price ? reason : null));
        }

        var access = await permissions.GetEffectiveAccessAsync(ct);
        var (staffMax, managerMax) = await LimitsAsync(ct);
        var sellerLimit = access.IsOwner ? 100m
            : access.HasForBranch(P.Pos.PriceOverrideApprove, request.BranchId) ? managerMax
            : access.HasForBranch(P.Pos.PriceOverride, request.BranchId) ? staffMax
            : 0m;
        var maxPct = evaluated.Max(l => l.Pct);
        var required = maxPct == 0 || maxPct <= sellerLimit ? "NONE" : maxPct <= managerMax ? "MANAGER" : "OWNER";
        var retailTotal = evaluated.Sum(l => l.Retail * l.Quantity);
        var grand = evaluated.Sum(l => l.Final * l.Quantity);
        var quote = new PosQuoteDto(
            evaluated.Select(l => new PosQuoteLineDto(l.Sku.SkuId, l.Sku.SkuCode, l.Sku.ProductName, l.Sku.VariantName, l.Quantity, l.Retail, l.Final, l.Pct, l.Final * l.Quantity))
                .ToList(),
            retailTotal, retailTotal - grand, grand, required, maxPct, sellerLimit);
        return new Evaluation(quote, evaluated);
    }

    private async Task<List<PosPaymentRequest>> ValidatePaymentsAsync(IReadOnlyList<PosPaymentRequest>? payments, decimal total, CancellationToken ct)
    {
        var enabled = await MethodsAsync(ct);
        var list = (payments ?? []).Select(p => p with { Method = (p.Method ?? string.Empty).Trim().ToUpperInvariant(), Reference = p.Reference?.Trim() }).ToList();
        if (list.Count == 0)
        {
            throw new BusinessRuleException("PAYMENT_REQUIRED", "Record how the customer paid.", 400);
        }
        foreach (var p in list)
        {
            if (!enabled.Contains(p.Method))
            {
                throw new BusinessRuleException("PAYMENT_METHOD_NOT_ACCEPTED", $"{p.Method} is not an accepted payment method (accepted: {string.Join(", ", enabled)}).", 400);
            }
            if (p.Amount <= 0 || !Money.HasValidScale(p.Amount))
            {
                throw new BusinessRuleException("PAYMENT_AMOUNT_INVALID", "Each payment must be more than ₹0 with at most 2 decimals.", 400);
            }
            if (p.Method != "CASH" && p.Reference is not { Length: >= 4 and <= 100 })
            {
                throw new BusinessRuleException("PAYMENT_REFERENCE_REQUIRED", $"Enter the {p.Method} transaction reference.", 400);
            }
        }
        var paid = list.Sum(p => p.Amount);
        if (paid != total)
        {
            var ex = new BusinessRuleException("PAYMENT_TOTAL_MISMATCH", $"Payments add up to ₹{paid:N2} but the total is ₹{total:N2}.", 400);
            ex.Details["total"] = total;
            ex.Details["paid"] = paid;
            throw ex;
        }
        return list;
    }

    private static (string? Name, string? Mobile) Customer(PosSaleRequest r)
    {
        var name = string.IsNullOrWhiteSpace(r.CustomerName) ? null : r.CustomerName.Trim();
        if (name is { Length: > 200 })
        {
            throw new BusinessRuleException("CUSTOMER_INVALID", "The customer name is too long.", 400);
        }
        string? mobile = null;
        if (!string.IsNullOrWhiteSpace(r.CustomerMobile))
        {
            mobile = MobileNumber.TryNormalize(r.CustomerMobile, out var m) ? m : throw new BusinessRuleException("MOBILE_INVALID", "Enter a valid 10-digit mobile number.", 400);
        }
        return (name, mobile);
    }

    private async Task<(decimal Staff, decimal Manager)> LimitsAsync(CancellationToken ct)
    {
        var staff = await settings.GetAsync<decimal>(SettingKeys.PosStaffMaxDiscountPct, ct);
        var manager = await settings.GetAsync<decimal>(SettingKeys.PosManagerMaxDiscountPct, ct);
        return (staff, Math.Max(staff, manager));
    }

    private async Task<IReadOnlyList<string>> MethodsAsync(CancellationToken ct) =>
        (await settings.GetAsync<string>(SettingKeys.PosPaymentMethods, ct)).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.ToUpper(CultureInfo.InvariantCulture)).Distinct().ToList();
}
