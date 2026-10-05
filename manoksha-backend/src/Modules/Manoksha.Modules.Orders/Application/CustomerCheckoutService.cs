using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Orders.Domain;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Pricing.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Orders.Application;

/// <summary>
/// Online checkout (SPEC §13, §14, §19.1, §32). In ONE transaction: backend retail pricing and snapshot, ₹100 shipping, branch
/// resolution by Owner priority (complete basket at one branch), a timed reservation (AVAILABLE → RESERVED) and an INITIATED
/// payment attempt. The provider session is created only after that commits. No qualifying branch → inquiry reference, no order
/// and no payment (SPEC §12). Repeated Place Order clicks with the same Idempotency-Key are one logical attempt.
/// </summary>
internal sealed class CustomerCheckoutService(
    ManokshaDbContext db,
    IIdempotencyService idempotency,
    IPriceCalculator prices,
    ICatalogLookup catalog,
    FulfillmentRouter router,
    InquiryService inquiries,
    IPayments payments,
    ISettingsReader settings,
    OrderQueryService orders,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<CustomerCheckoutResult> CheckoutAsync(CustomerCheckoutRequest request, string idempotencyKey, CancellationToken ct)
    {
        var stage = await idempotency.ExecuteAsync("customer.checkout", idempotencyKey, request, innerCt => StageAsync(request, innerCt), ct);
        if (stage.Inquiry is not null)
        {
            return new CustomerCheckoutResult("UNFULFILLABLE", null, null, stage.Inquiry);
        }

        // After commit: create (or, on a repeat, return) the provider session. A failure here releases the reservation.
        var payment = await payments.StartSessionAsync(stage.PaymentAttemptId!.Value, ct);
        var order = await orders.GetForCustomerAsync(stage.OrderId!.Value, ct);
        return new CustomerCheckoutResult(OnlinePayments.Outcome(payment), order, OnlinePayments.ToDto(payment), null);
    }

    private async Task<CheckoutStage> StageAsync(CustomerCheckoutRequest request, CancellationToken ct)
    {
        if (currentUser.AccountType != AccountType.Customer)
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Online orders are placed from a customer account.");
        }
        var lines = CheckoutRules.ValidateLines(request.Lines);
        var delivery = ResellerCustomerService.Validate(request.Delivery);

        // Authoritative prices (never from the client), snapshotted now; the ₹100 per-order shipping fee (SPEC §15, §18).
        var ids = lines.Select(l => l.SkuId).ToList();
        var quote = (await prices.QuoteForRetailAsync(ids, ct)).ToDictionary(q => q.SkuId);
        var skus = await catalog.FindSkusAsync(ids, ct);
        var merchandise = lines.Sum(l => quote[l.SkuId].FinalPrice * l.Quantity);
        var vendorBasket = await VendorCheckout.GroupAsync(lines, skus, catalog, ct);
        var shipping = vendorBasket?.Shipping ?? await settings.GetAsync<decimal>(SettingKeys.ShippingFeePerOrder, ct);

        // The reservation keeps its own expiry; later setting changes never affect it (SPEC §13).
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(await settings.GetAsync<int>(SettingKeys.ReservationMinutes, ct));

        var orderId = Uuid7.NewGuid();
        var orderNumber = await CheckoutRules.NextOrderNumberAsync(db, ct);
        if (vendorBasket is not null)
        {
            return await StageVendorOrderAsync(orderId, orderNumber, delivery, lines, quote, skus, vendorBasket, merchandise, shipping, now, expiresAt, ct);
        }
        var basket = lines.Select(l => new BasketLine(l.SkuId, l.Quantity)).ToList();
        var (resolution, evaluations) = await router.ResolveAsync(basket, StockHold.Reserve, orderId, orderNumber, ct);
        if (resolution is null)
        {
            return new CheckoutStage(null, null, await inquiries.CreateAsync(OrderChannel.Online, null, delivery, lines, evaluations, ct));
        }

        var userId = currentUser.UserId;
        var order = new Order(orderId, orderNumber, OrderChannel.Online, null, null, resolution.BranchId, delivery, merchandise, shipping, userId, now, customerUserId: userId);
        order.AwaitPayment();
        db.Add(order);
        await db.SaveChangesAsync(ct);

        var reservation = new Reservation(order.Id, resolution.BranchId, expiresAt, now);
        db.Add(reservation);
        foreach (var l in lines)
        {
            var q = quote[l.SkuId];
            var sku = skus[l.SkuId];
            var line = NewLine(order.Id, l, q, sku);
            db.Add(line);
            var held = resolution.Allocation.Lines.Single(a => a.SkuId == l.SkuId);
            db.Add(new ReservationLine(reservation.Id, line.Id, l.SkuId, l.Quantity, [.. held.ItemIds]));
        }
        db.Add(new OrderStatusChange(order.Id, null, OrderStatus.PaymentPending, userId, $"Complete basket reserved until {expiresAt:yyyy-MM-dd HH:mm:ss} UTC", now));
        await db.SaveChangesAsync(ct);

        var attempt = await payments.CreateAttemptAsync(new NewPaymentAttempt(PaymentPurposes.Order, order.Id, order.Number, userId, order.GrandTotal, expiresAt,
            $"Manoksha Collections order {order.Number}", $"/orders/{order.Id}"), ct);
        await audit.RecordAsync(new AuditRecord("orders.online_order.reserved", "Order", order.Id.ToString(),
            After: new { order.Number, branchId = resolution.BranchId, order.MerchandiseTotal, order.ShippingFee, order.GrandTotal, expiresAt, paymentAttemptId = attempt.Id, evaluations },
            BranchId: resolution.BranchId), ct);
        await db.SaveChangesAsync(ct);
        return new CheckoutStage(order.Id, attempt.Id, null);
    }

    private static OrderLine NewLine(Guid orderId, CheckoutLineRequest l, RetailPriceLine q, SkuInfo sku) =>
        new(orderId, l.SkuId, sku.SkuCode, sku.ProductName, sku.VariantName, l.Quantity, q.RetailPriceId, q.RetailPrice,
            q.DiscountPct > 0 ? DiscountSources.OnlineProduct : DiscountSources.None, q.DiscountPct, q.FinalPrice, null, null, null, null, []);

    /// <summary>
    /// Vendor products (ADR-001 §43–46): nothing to reserve. The order waits for the UPI payment with one parcel per vendor; a
    /// payment — even a late one — confirms it.
    /// </summary>
    private async Task<CheckoutStage> StageVendorOrderAsync(Guid orderId, string orderNumber, DeliveryDetails delivery, IReadOnlyList<CheckoutLineRequest> lines,
        IReadOnlyDictionary<Guid, RetailPriceLine> quote, IReadOnlyDictionary<Guid, SkuInfo> skus, VendorBasket basket, decimal merchandise, decimal shipping,
        DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        var order = new Order(orderId, orderNumber, OrderChannel.Online, null, null, null, delivery, merchandise, shipping, userId, now, customerUserId: userId);
        order.AwaitPayment();
        db.Add(order);
        await db.SaveChangesAsync(ct);
        var orderLines = new Dictionary<Guid, OrderLine>();
        foreach (var l in lines)
        {
            var line = NewLine(order.Id, l, quote[l.SkuId], skus[l.SkuId]);
            db.Add(line);
            orderLines[l.SkuId] = line;
        }
        VendorCheckout.AddParcels(db, order, basket, skus, orderLines, now);
        db.Add(new OrderStatusChange(order.Id, null, OrderStatus.PaymentPending, userId,
            $"Waiting for payment; {basket.Groups.Count} vendor parcel(s): {string.Join(", ", basket.Groups.Select(g => g.Vendor.Name))}", now));
        await db.SaveChangesAsync(ct);
        var attempt = await payments.CreateAttemptAsync(new NewPaymentAttempt(PaymentPurposes.Order, order.Id, order.Number, userId, order.GrandTotal, expiresAt,
            $"Manoksha Collections order {order.Number}", $"/orders/{order.Id}"), ct);
        await audit.RecordAsync(new AuditRecord("orders.online_order.created", "Order", order.Id.ToString(),
            After: new { order.Number, vendors = basket.Groups.Select(g => new { g.Vendor.Name, g.Vendor.ShippingFee }), order.MerchandiseTotal, order.ShippingFee,
                order.GrandTotal, expiresAt, paymentAttemptId = attempt.Id }), ct);
        await db.SaveChangesAsync(ct);
        return new CheckoutStage(order.Id, attempt.Id, null);
    }
}

internal static class OnlinePayments
{
    public static string Outcome(PaymentAttemptInfo p) => p.Status switch
    {
        "INITIATED" or "PENDING" => "PAYMENT_PENDING",
        "SUCCESS" or "ORDER_RECOVERED" => "ORDER_PLACED",
        _ => "PAYMENT_NOT_COMPLETED",
    };

    public static OnlinePaymentDto ToDto(PaymentAttemptInfo p) =>
        new(p.Id, p.Status, p.Amount, p.Status == "PENDING" ? p.RedirectUrl : null, p.ExpiresAt, p.CompletedAt, p.Status switch
        {
            "INITIATED" => "Preparing your UPI payment…",
            "PENDING" => "Waiting for your UPI payment. Your items are reserved until the time shown.",
            "SUCCESS" => "Payment received. Your order is confirmed.",
            "ORDER_RECOVERED" => "Payment received after the reservation window; your order was confirmed with the same prices.",
            "FAILED" => "The payment was not completed. Nothing was charged and your items were released.",
            "EXPIRED" => "The payment window ended before a payment was confirmed. Your items were released.",
            "LATE_SUCCESS_RECHECK" => "Payment received late; we are checking stock for your order.",
            "PAYMENT_RECONCILIATION_REQUIRED" =>
                "We received your payment, but could not confirm this order. Our team will contact you; please share your order number on WhatsApp.",
            _ => p.Status,
        });
}
