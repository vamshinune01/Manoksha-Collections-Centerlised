using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Orders.Application;

public sealed record OrderConfirmed(Guid OrderId, string OrderNumber, string Channel, Guid FulfillmentBranchId) : IIntegrationEvent
{
    public static string EventType => "orders.order_confirmed";
}

public sealed record FulfillmentInquiryCreated(Guid InquiryId, string Reference) : IIntegrationEvent
{
    public static string EventType => "orders.fulfillment_inquiry_created";
}

/// <summary>
/// Reseller checkout (SPEC §19.2, §32): in ONE transaction — backend pricing, branch resolution by Owner priority (complete basket
/// at one branch, never split), stock commit, wallet debit and order creation. Any failure rolls everything back. Idempotent by
/// Idempotency-Key, so repeated clicks create one order and one debit.
/// </summary>
internal sealed class ResellerCheckoutService(
    ManokshaDbContext db,
    IIdempotencyService idempotency,
    IResellerDirectory resellers,
    IPriceCalculator prices,
    ICatalogLookup catalog,
    IFulfillmentPriorityProvider priority,
    IStockAllocator allocator,
    IWallets wallets,
    ISettingsReader settings,
    ResellerCustomerService customers,
    OrderQueryService orders,
    WhatsApp whatsApp,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    private const int MaxLines = 50;

    public Task<CheckoutResult> CheckoutAsync(ResellerCheckoutRequest request, string idempotencyKey, CancellationToken ct) =>
        idempotency.ExecuteAsync("reseller.checkout", idempotencyKey, request, innerCt => CheckoutCoreAsync(request, innerCt), ct);

    private async Task<CheckoutResult> CheckoutCoreAsync(ResellerCheckoutRequest request, CancellationToken ct)
    {
        var reseller = await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");
        if (!reseller.CanTransact)
        {
            throw new ForbiddenException("RESELLER_CANNOT_ORDER", $"New orders are not available while your account is {reseller.Status}.");
        }
        var lines = ValidateLines(request.Lines);

        // Delivery details are always required (ADR-001 §21): a saved customer of THIS reseller, or entered now.
        DeliveryDetails delivery;
        Guid? customerId = null;
        if (request.ResellerCustomerId is { } savedId)
        {
            var saved = await customers.GetOwnAsync(reseller.ResellerId, savedId, ct);
            delivery = saved.Details;
            customerId = saved.Id;
        }
        else
        {
            delivery = ResellerCustomerService.Validate(request.Delivery);
            if (request.SaveCustomer)
            {
                customerId = (await customers.UpsertAsync(reseller.ResellerId, delivery, ct)).Id;
            }
        }

        // Authoritative prices (never from the client) and the ₹100 per-order shipping fee.
        var quote = (await prices.QuoteForResellerAsync(reseller.ResellerId, lines.Select(l => l.SkuId).ToList(), ct)).ToDictionary(q => q.SkuId);
        var skus = await catalog.FindSkusAsync(lines.Select(l => l.SkuId).ToList(), ct);
        var merchandise = lines.Sum(l => quote[l.SkuId].FinalUnitPrice * l.Quantity);
        var shipping = await settings.GetAsync<decimal>(SettingKeys.ShippingFeePerOrder, ct);
        var total = merchandise + shipping;

        // Fast, friendly check (the authoritative check is the locked debit below).
        var wallet = await wallets.FindAsync(reseller.ResellerId, ct) ?? throw new NotFoundException("WALLET_NOT_FOUND", "No wallet found.");
        if (wallet.Balance < total)
        {
            var ex = new BusinessRuleException("INSUFFICIENT_WALLET_BALANCE",
                $"Order total ₹{total:N2} (including ₹{shipping:N2} shipping) exceeds your wallet balance ₹{wallet.Balance:N2}. Please add money to your wallet.", 422);
            ex.Details["balance"] = wallet.Balance;
            ex.Details["required"] = total;
            throw ex;
        }

        var orderId = Uuid7.NewGuid();
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('orders.order_number_seq') AS \"Value\"").SingleAsync(ct);
        var orderNumber = $"MC-ORD-{seq:D6}";

        // Owner priority: first ACTIVE branch that can fulfil the complete basket wins (SPEC §11).
        var (_, entries) = await priority.GetCurrentAsync(ct);
        var basket = lines.Select(l => new BasketLine(l.SkuId, l.Quantity)).ToList();
        var evaluations = new List<object>();
        BasketAllocation? allocation = null;
        Guid branchId = default;
        foreach (var entry in entries)
        {
            if (!entry.IsActive)
            {
                evaluations.Add(new { entry.Priority, entry.BranchCode, result = "SKIPPED_INACTIVE" });
                continue;
            }
            var attempt = await allocator.TrySellBasketAsync(entry.BranchId, basket, "Order", orderId, orderNumber, ct);
            evaluations.Add(new { entry.Priority, entry.BranchCode, result = attempt.Success ? "FULFILLED" : "INSUFFICIENT_STOCK", shortfalls = attempt.Shortfalls });
            if (attempt.Success)
            {
                allocation = attempt;
                branchId = entry.BranchId;
                break;
            }
        }

        if (allocation is null)
        {
            return await CreateInquiryAsync(reseller, delivery, lines, evaluations, ct);
        }

        // Wallet debit + order creation, atomic with the stock commit above (SPEC §16, §32).
        var order = new Order(orderId, orderNumber, OrderChannel.Reseller, reseller.ResellerId, customerId, branchId, delivery, merchandise, shipping, currentUser.UserId, clock.UtcNow);
        db.Add(order);
        await db.SaveChangesAsync(ct);
        var debit = await wallets.DebitForOrderAsync(reseller.ResellerId, order.GrandTotal, order.Id, order.Number, ct);
        foreach (var l in lines)
        {
            var q = quote[l.SkuId];
            var sku = skus[l.SkuId];
            var alloc = allocation.Lines.Single(a => a.SkuId == l.SkuId);
            db.Add(new OrderLine(order.Id, l.SkuId, sku.SkuCode, sku.ProductName, sku.VariantName, l.Quantity, q.RetailPriceId, q.RetailPrice, q.DiscountSource, q.DiscountPct,
                q.FinalUnitPrice, q.CommercialTermId, q.CommercialTermVersion, q.ProductDiscountId, alloc.CostAmount, [.. alloc.ItemIds]));
        }
        order.ConfirmWalletPaid(debit.EntryId, clock.UtcNow);
        db.Add(new OrderStatusChange(order.Id, null, OrderStatus.Confirmed, currentUser.UserId, "Wallet debited and stock committed", clock.UtcNow));
        await audit.RecordAsync(new AuditRecord("orders.reseller_order.confirmed", "Order", order.Id.ToString(),
            After: new
            {
                order.Number,
                resellerId = reseller.ResellerId,
                branchId,
                order.MerchandiseTotal,
                order.ShippingFee,
                order.GrandTotal,
                walletBefore = debit.BalanceBefore,
                walletAfter = debit.BalanceAfter,
                evaluations,
            }, BranchId: branchId), ct);
        outbox.Enqueue(new OrderConfirmed(order.Id, order.Number, order.Channel.ToString(), branchId));
        await db.SaveChangesAsync(ct);

        return new CheckoutResult("CONFIRMED", await orders.GetAsync(order.Id, ct), debit.BalanceAfter, null);
    }

    private async Task<CheckoutResult> CreateInquiryAsync(ResellerInfo reseller, DeliveryDetails delivery, IReadOnlyList<CheckoutLineRequest> lines, List<object> evaluations, CancellationToken ct)
    {
        var seq = await db.Database.SqlQuery<long>($"SELECT nextval('orders.inquiry_number_seq') AS \"Value\"").SingleAsync(ct);
        var reference = $"MC-FUL-{seq:D8}";
        var inquiry = new FulfillmentInquiry(reference, OrderChannel.Reseller, reseller.ResellerId, currentUser.UserId, delivery.Name, delivery.Mobile,
            JsonSerializer.Serialize(lines, JsonDefaults.Options), JsonSerializer.Serialize(evaluations, JsonDefaults.Options), "NO_BRANCH_CAN_FULFIL_COMPLETE_BASKET", clock.UtcNow);
        db.Add(inquiry);
        await audit.RecordAsync(new AuditRecord("orders.fulfillment_inquiry.created", "FulfillmentInquiry", inquiry.Id.ToString(),
            After: new { reference, resellerId = reseller.ResellerId, lines, evaluations }), ct);
        outbox.Enqueue(new FulfillmentInquiryCreated(inquiry.Id, reference));
        await db.SaveChangesAsync(ct);
        return new CheckoutResult("UNFULFILLABLE", null, null, await whatsApp.InquiryAsync(reference, ct));
    }

    private static List<CheckoutLineRequest> ValidateLines(IReadOnlyList<CheckoutLineRequest>? lines)
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
}
