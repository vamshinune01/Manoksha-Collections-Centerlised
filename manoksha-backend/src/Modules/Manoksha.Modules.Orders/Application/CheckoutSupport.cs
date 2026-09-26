using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Orders.Application;

internal sealed record BranchResolution(Guid BranchId, BasketAllocation Allocation);

/// <summary>
/// Owner fulfillment priority (SPEC §11): evaluate branches in order for the COMPLETE basket; the first ACTIVE branch that can
/// fulfil every line wins. Never splits an order. Every evaluation is recorded for the Exception Center.
/// </summary>
internal sealed class FulfillmentRouter(IFulfillmentPriorityProvider priority, IStockAllocator allocator)
{
    public async Task<(BranchResolution? Resolution, List<object> Evaluations)> ResolveAsync(IReadOnlyList<BasketLine> basket, StockHold hold, Guid orderId,
        string orderNumber, CancellationToken ct)
    {
        var (_, entries) = await priority.GetCurrentAsync(ct);
        var evaluations = new List<object>();
        foreach (var entry in entries)
        {
            if (!entry.IsActive)
            {
                evaluations.Add(new { entry.Priority, entry.BranchCode, result = "SKIPPED_INACTIVE" });
                continue;
            }
            var attempt = hold == StockHold.Sell
                ? await allocator.TrySellBasketAsync(entry.BranchId, basket, "Order", orderId, orderNumber, ct)
                : await allocator.TryReserveBasketAsync(entry.BranchId, basket, "Order", orderId, orderNumber, ct);
            evaluations.Add(new { entry.Priority, entry.BranchCode, result = attempt.Success ? "FULFILLED" : "INSUFFICIENT_STOCK", shortfalls = attempt.Shortfalls });
            if (attempt.Success)
            {
                return (new BranchResolution(entry.BranchId, attempt), evaluations);
            }
        }
        return (null, evaluations);
    }
}

internal enum StockHold
{
    /// <summary>AVAILABLE → SOLD now (reseller wallet orders, late-payment recovery).</summary>
    Sell = 1,

    /// <summary>AVAILABLE → RESERVED for the payment window (online orders).</summary>
    Reserve = 2,
}

/// <summary>"No qualifying branch" (SPEC §12): no order and no payment/debit; a reference to share on WhatsApp.</summary>
internal sealed class InquiryService(ManokshaDbContext db, WhatsApp whatsApp, IAuditWriter audit, IOutbox outbox, ICurrentUser currentUser, IClock clock)
{
    public async Task<InquiryDto> CreateAsync(OrderChannel channel, Guid? resellerId, DeliveryDetails? contact, IReadOnlyList<CheckoutLineRequest> lines,
        List<object> evaluations, CancellationToken ct)
    {
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('orders.inquiry_number_seq') AS \"Value\"").SingleAsync(ct);
        var reference = $"MC-FUL-{seq:D8}";
        var inquiry = new FulfillmentInquiry(reference, channel, resellerId, currentUser.UserId, contact?.Name, contact?.Mobile,
            JsonSerializer.Serialize(lines, JsonDefaults.Options), JsonSerializer.Serialize(evaluations, JsonDefaults.Options), "NO_BRANCH_CAN_FULFIL_COMPLETE_BASKET", clock.UtcNow);
        db.Add(inquiry);
        await audit.RecordAsync(new AuditRecord("orders.fulfillment_inquiry.created", "FulfillmentInquiry", inquiry.Id.ToString(),
            After: new { reference, channel = channel.ToString(), resellerId, lines, evaluations }), ct);
        outbox.Enqueue(new FulfillmentInquiryCreated(inquiry.Id, reference));
        await db.SaveChangesAsync(ct);
        return await whatsApp.InquiryAsync(reference, ct);
    }
}

internal static class CheckoutRules
{
    public const int MaxLines = 50;

    public static List<CheckoutLineRequest> ValidateLines(IReadOnlyList<CheckoutLineRequest>? lines)
    {
        var list = (lines ?? []).ToList();
        if (list.Count is 0 or > MaxLines || list.Select(l => l.SkuId).Distinct().Count() != list.Count)
        {
            throw new BusinessRuleException("CART_INVALID", $"The cart must have 1–{MaxLines} different items.", 400);
        }
        if (list.Exists(l => l.Quantity is < 1 or > 1000))
        {
            throw new BusinessRuleException("QUANTITY_INVALID", "Each quantity must be between 1 and 1000.", 400);
        }
        return list;
    }

    public static async Task<string> NextOrderNumberAsync(ManokshaDbContext db, CancellationToken ct)
    {
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('orders.order_number_seq') AS \"Value\"").SingleAsync(ct);
        return $"MC-ORD-{seq:D6}";
    }
}
