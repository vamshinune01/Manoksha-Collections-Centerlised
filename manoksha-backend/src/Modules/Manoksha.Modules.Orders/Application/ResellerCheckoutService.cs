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
    FulfillmentRouter router,
    InquiryService inquiries,
    IWallets wallets,
    ISettingsReader settings,
    ResellerCustomerService customers,
    OrderQueryService orders,
    IAuditWriter audit,
    IOutbox outbox,
    ICurrentUser currentUser,
    IClock clock)
{
    public Task<CheckoutResult> CheckoutAsync(ResellerCheckoutRequest request, string idempotencyKey, CancellationToken ct) =>
        idempotency.ExecuteAsync("reseller.checkout", idempotencyKey, request, innerCt => CheckoutCoreAsync(request, innerCt), ct);

    private async Task<CheckoutResult> CheckoutCoreAsync(ResellerCheckoutRequest request, CancellationToken ct)
    {
        var reseller = await resellers.FindByUserAsync(currentUser.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");
        if (!reseller.CanTransact)
        {
            throw new ForbiddenException("RESELLER_CANNOT_ORDER", $"New orders are not available while your account is {reseller.Status}.");
        }
        var lines = CheckoutRules.ValidateLines(request.Lines);

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
        var orderNumber = await CheckoutRules.NextOrderNumberAsync(db, ct);

        // Owner priority: first ACTIVE branch that can fulfil the complete basket wins (SPEC §11).
        var basket = lines.Select(l => new BasketLine(l.SkuId, l.Quantity)).ToList();
        var (resolution, evaluations) = await router.ResolveAsync(basket, StockHold.Sell, orderId, orderNumber, ct);
        if (resolution is null)
        {
            return new CheckoutResult("UNFULFILLABLE", null, null, await inquiries.CreateAsync(OrderChannel.Reseller, reseller.ResellerId, delivery, lines, evaluations, ct));
        }
        var (branchId, allocation) = resolution;

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
}
