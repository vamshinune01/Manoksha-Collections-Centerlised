using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Catalog.Contracts;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Modules.Inventory.Contracts;
using Manoksha.Modules.Notifications.Domain;
using Manoksha.Modules.Orders.Contracts;
using Manoksha.Modules.Payments.Contracts;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Wallet.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Notifications.Application;

/// <summary>
/// Maps one business event to recipients, channels and templates (design §17). Handlers are idempotent (dedupe keys) and only add
/// rows; the outbox dispatcher saves them with the processed marker.
/// </summary>
internal abstract class NotificationHandler<TEvent>(NotificationPublisher publisher) : IOutboxEventHandler
    where TEvent : IIntegrationEvent
{
    public string EventType => TEvent.EventType;

    protected NotificationPublisher Publisher => publisher;

    protected string Shop => publisher.Options.ShopName;

    public Task HandleAsync(string payloadJson, CancellationToken cancellationToken) =>
        HandleAsync(JsonSerializer.Deserialize<TEvent>(payloadJson, JsonDefaults.Options)
                    ?? throw new InvalidOperationException($"Empty {TEvent.EventType} payload."), cancellationToken);

    protected abstract Task HandleAsync(TEvent e, CancellationToken ct);
}

// ---------------- Orders ----------------

internal sealed class OrderConfirmedHandler(NotificationPublisher publisher, IOrderNotificationView orders, OrderRecipients recipients)
    : NotificationHandler<OrderConfirmed>(publisher)
{
    protected override async Task HandleAsync(OrderConfirmed e, CancellationToken ct)
    {
        if (e.Channel == "Store")
        {
            return;
        }
        var o = await orders.GetAsync(e.OrderId, ct);
        if (o is null)
        {
            return;
        }
        await Publisher.InAppAsync($"order_confirmed:{e.OrderId:N}", EventType, Category.Info, $"New {e.Channel.ToLowerInvariant()} order {e.OrderNumber} to pack",
            $"{o.Lines.Sum(l => l.Quantity)} item(s) · {EmailTemplates.Rs(o.GrandTotal)} · deliver to {o.DeliveryCity}", $"/orders/{e.OrderId}",
            P.Orders.Fulfill, e.FulfillmentBranchId, null, ct);

        var to = await recipients.ForAsync(o, ct);
        var blocks = new List<EmailBlock>
        {
            new Para($"Thank you{(to.Name is null ? string.Empty : ", " + to.Name)}. We have received your order and will pack it soon."),
            new Facts([("Order", o.Number), ("Deliver to", $"{o.DeliveryName}, {o.DeliveryCity}")]),
            new Items(o.Lines.Select(l => (l.Name, l.Quantity, l.LineTotal)).ToList()),
            new Facts([("Items", EmailTemplates.Rs(o.MerchandiseTotal)), ("Shipping", EmailTemplates.Rs(o.ShippingFee)), ("Total", EmailTemplates.Rs(o.GrandTotal))]),
            new Para("Need help with this order? Use \"Need help with this order\" on the order page."),
        };
        await Publisher.EmailAsync($"order_confirmed:{e.OrderId:N}:email", EventType, Category.Info, o.Number, to.Email, to.Name, to.Kind,
            EmailTemplates.Build(Shop, $"Order {o.Number} confirmed", $"Your order {o.Number} is confirmed", blocks, "View order", Publisher.CustomerLink(to.OrderPath)), ct);
    }
}

internal sealed class OrderStatusChangedHandler(NotificationPublisher publisher, IOrderNotificationView orders, OrderRecipients recipients)
    : NotificationHandler<OrderStatusChanged>(publisher)
{
    protected override async Task HandleAsync(OrderStatusChanged e, CancellationToken ct)
    {
        if (e.Channel == "Store" || e.Status is not ("Shipped" or "Delivered" or "Cancelled"))
        {
            return;
        }
        var o = await orders.GetAsync(e.OrderId, ct);
        if (o is null)
        {
            return;
        }
        var to = await recipients.ForAsync(o, ct);
        var (subject, heading, blocks) = e.Status switch
        {
            "Shipped" => ($"Order {o.Number} has shipped", $"Your order {o.Number} is on its way", new List<EmailBlock>
            {
                new Para("Your order has left our store."),
                new Facts(new List<(string, string)> { ("Order", o.Number), ("Courier", o.Courier ?? "—") }
                    .Concat(o.TrackingNumber is null ? [] : [("Tracking number", o.TrackingNumber)]).ToList()),
            }),
            "Delivered" => ($"Order {o.Number} delivered", $"Your order {o.Number} was delivered", new List<EmailBlock>
            {
                new Para($"Your order was delivered{(o.DeliveredOn is { } d ? " on " + d.ToString("dd MMM yyyy") : string.Empty)}. Thank you for shopping with us."),
            }),
            _ => ($"Order {o.Number} cancelled", $"Your order {o.Number} was cancelled", new List<EmailBlock>
            {
                new Para(o.ResellerId is null
                    ? "We are sorry — we had to cancel this order. If you paid online, our team will contact you about your refund."
                    : "We are sorry — we had to cancel this order. The amount is credited back to your wallet."),
                new Facts([("Order", o.Number), ("Total", EmailTemplates.Rs(o.GrandTotal))]),
            }),
        };
        await Publisher.EmailAsync($"order_{e.Status.ToLowerInvariant()}:{e.OrderId:N}:email", EventType, Category.Info, o.Number, to.Email, to.Name, to.Kind,
            EmailTemplates.Build(Shop, subject, heading, blocks, "View order", Publisher.CustomerLink(to.OrderPath)), ct);
    }
}

/// <summary>Who hears about an order: the customer (email given at checkout, else their account email) or the reseller.</summary>
internal sealed class OrderRecipients(IUserDirectory users, IResellerDirectory resellers)
{
    public sealed record Recipient(string? Email, string? Name, string Kind, string OrderPath);

    public async Task<Recipient> ForAsync(OrderNotificationSummary o, CancellationToken ct)
    {
        if (o.ResellerId is { } rid)
        {
            var r = await resellers.GetContactAsync(rid, ct);
            return new Recipient(r?.Email, r?.ContactName, "RESELLER", $"/reseller/orders/{o.OrderId}");
        }
        var email = o.ContactEmail;
        string? name = o.DeliveryName;
        if (string.IsNullOrWhiteSpace(email) && o.CustomerUserId is { } uid)
        {
            var u = await users.GetContactAsync(uid, ct);
            email = u?.Email;
            name = u?.DisplayName ?? name;
        }
        return new Recipient(email, name, "CUSTOMER", $"/orders/{o.OrderId}");
    }
}

internal sealed class FulfillmentExceptionHandler(NotificationPublisher publisher, IBranchDirectory branches) : NotificationHandler<FulfillmentExceptionRaised>(publisher)
{
    protected override async Task HandleAsync(FulfillmentExceptionRaised e, CancellationToken ct)
    {
        var branch = (await branches.FindAsync(e.BranchId, ct))?.Name ?? "a branch";
        var reason = e.Reason.Replace('_', ' ').ToLowerInvariant();
        await Publisher.InAppAsync($"fulfillment_exception:{e.ExceptionId:N}", EventType, Category.Critical, $"Order {e.OrderNumber} cannot be fulfilled as is",
            $"{branch} reported: {reason}. Reroute the whole order or resolve it.", $"/orders/{e.OrderId}", P.Orders.Reroute, e.BranchId, null, ct);
        await Publisher.EmailOwnersAsync($"fulfillment_exception:{e.ExceptionId:N}", EventType, Category.Critical, e.OrderNumber,
            EmailTemplates.Build(Shop, $"CRITICAL: order {e.OrderNumber} cannot be fulfilled", $"Order {e.OrderNumber} needs your decision",
                [new Para($"{branch} reported a fulfillment exception: {reason}."), new Para("Reroute the whole order to another branch, or resolve it in place.")],
                "Open order", Publisher.AdminLink($"/orders/{e.OrderId}")), ct);
    }
}

internal sealed class FulfillmentInquiryHandler(NotificationPublisher publisher) : NotificationHandler<FulfillmentInquiryCreated>(publisher)
{
    protected override Task HandleAsync(FulfillmentInquiryCreated e, CancellationToken ct) =>
        Publisher.InAppAsync($"fulfillment_inquiry:{e.InquiryId:N}", EventType, Category.Warning, $"Unfulfilled checkout {e.Reference}",
            $"No single branch could fulfil a {(e.Channel ?? "customer").ToLowerInvariant()} basket{(e.ContactName is null ? string.Empty : " for " + e.ContactName)}. Follow up with the customer.",
            "/exceptions?type=UNFULFILLED_CHECKOUT", P.Exceptions.View, null, null, ct);
}

// ---------------- Payments ----------------

internal sealed class ReconciliationRequiredHandler(NotificationPublisher publisher) : NotificationHandler<PaymentReconciliationRequired>(publisher)
{
    protected override async Task HandleAsync(PaymentReconciliationRequired e, CancellationToken ct)
    {
        var detail = $"{e.ReasonCode.Replace('_', ' ').ToLowerInvariant()} · {e.ReferenceNumber} · expected {EmailTemplates.Rs(e.ExpectedAmount)}"
                     + (e.PaidAmount is { } p ? $", paid {EmailTemplates.Rs(p)}" : string.Empty);
        await Publisher.InAppAsync($"reconciliation:{e.CaseId:N}", EventType, Category.Critical, $"Payment needs reconciliation ({e.CaseNumber})", detail,
            $"/payments/{e.CaseId}", P.Exceptions.ReconciliationManage, null, null, ct);
        await Publisher.EmailOwnersAsync($"reconciliation:{e.CaseId:N}", EventType, Category.Critical, e.CaseNumber,
            EmailTemplates.Build(Shop, $"CRITICAL: payment needs reconciliation ({e.CaseNumber})", "A payment was received but could not be applied",
                [new Facts([("Case", e.CaseNumber), ("Reference", e.ReferenceNumber), ("Reason", e.ReasonCode), ("Expected", EmailTemplates.Rs(e.ExpectedAmount)),
                    ("Paid", e.PaidAmount is { } paid ? EmailTemplates.Rs(paid) : "—")]),
                 new Para("Contact the customer and record the refund or resolution on the case. No refund is made automatically.")],
                "Open case", Publisher.AdminLink($"/payments/{e.CaseId}")), ct);
    }
}

// ---------------- Wallet ----------------

internal sealed class DepositSubmittedHandler(NotificationPublisher publisher, IResellerDirectory resellers) : NotificationHandler<DepositSubmittedEvent>(publisher)
{
    protected override async Task HandleAsync(DepositSubmittedEvent e, CancellationToken ct)
    {
        var r = await resellers.GetContactAsync(e.ResellerId, ct);
        await Publisher.InAppAsync($"deposit_submitted:{e.DepositId:N}", EventType, Category.Warning, $"Wallet deposit waiting for approval{(e.Number is null ? string.Empty : " (" + e.Number + ")")}",
            $"{r?.BusinessName ?? r?.ContactName ?? "A reseller"} submitted {EmailTemplates.Rs(e.Amount)} with payment proof.", "/wallet-deposits",
            P.Wallet.DepositApprove, null, null, ct);
    }
}

internal sealed class DepositDecidedHandler(NotificationPublisher publisher, IResellerDirectory resellers) : NotificationHandler<DepositDecidedEvent>(publisher)
{
    protected override async Task HandleAsync(DepositDecidedEvent e, CancellationToken ct)
    {
        var r = await resellers.GetContactAsync(e.ResellerId, ct);
        var approved = e.Status is "Credited" or "Approved";
        var amount = e.Amount is { } a ? EmailTemplates.Rs(a) : "your deposit";
        var blocks = new List<EmailBlock>
        {
            new Para(approved ? $"{amount} has been added to your wallet." : $"Your wallet deposit of {amount} was not approved."),
            new Facts(new List<(string, string)> { ("Deposit", e.Number ?? "—") }
                .Concat(!approved && !string.IsNullOrWhiteSpace(e.Note) ? [("Reason", e.Note!)] : []).ToList()),
        };
        await Publisher.EmailAsync($"deposit_decided:{e.DepositId:N}", EventType, Category.Info, e.Number, r?.Email, r?.ContactName, "RESELLER",
            EmailTemplates.Build(Shop, approved ? "Wallet deposit credited" : "Wallet deposit not approved",
                approved ? "Your wallet deposit is credited" : "Your wallet deposit was rejected", blocks, "Open wallet", Publisher.CustomerLink("/reseller/wallet")), ct);
    }
}

internal sealed class OnlineDepositCreditedHandler(NotificationPublisher publisher, IResellerDirectory resellers) : NotificationHandler<WalletDepositCredited>(publisher)
{
    protected override async Task HandleAsync(WalletDepositCredited e, CancellationToken ct)
    {
        var r = await resellers.GetContactAsync(e.ResellerId, ct);
        await Publisher.EmailAsync($"online_deposit_credited:{e.DepositId:N}", EventType, Category.Info, e.Number, r?.Email, r?.ContactName, "RESELLER",
            EmailTemplates.Build(Shop, "Wallet deposit credited", "Your UPI deposit is credited",
                [new Para($"{EmailTemplates.Rs(e.Amount)} has been added to your wallet."), new Facts([("Deposit", e.Number)])],
                "Open wallet", Publisher.CustomerLink("/reseller/wallet")), ct);
    }
}

internal sealed class WalletIntegrityHandler(NotificationPublisher publisher, IClock clock) : NotificationHandler<WalletIntegrityMismatch>(publisher)
{
    protected override async Task HandleAsync(WalletIntegrityMismatch e, CancellationToken ct)
    {
        var key = $"wallet_integrity:{clock.UtcNow:yyyyMMdd}";
        var detail = $"The nightly wallet check found {e.IssueCount} wallet(s) whose balance does not match the ledger.";
        await Publisher.AlertAsync(key, "WALLET_INTEGRITY", Category.Critical, "Wallet balance does not match the ledger", detail, null, null, ct);
        await Publisher.InAppAsync(key, EventType, Category.Critical, "Wallet balance does not match the ledger", detail, "/exceptions?type=SENSITIVE_ALERT",
            P.Exceptions.Manage, null, null, ct);
        await Publisher.EmailOwnersAsync(key, EventType, Category.Critical, null,
            EmailTemplates.Build(Shop, "CRITICAL: wallet integrity check failed", "Wallet balances need attention", [new Para(detail)],
                "Open Exception Center", Publisher.AdminLink("/exceptions?type=SENSITIVE_ALERT")), ct);
    }
}

// ---------------- Resellers ----------------

internal sealed class CommercialTermsChangedHandler(NotificationPublisher publisher, IResellerDirectory resellers) : NotificationHandler<CommercialTermsChanged>(publisher)
{
    protected override async Task HandleAsync(CommercialTermsChanged e, CancellationToken ct)
    {
        if (e.Version <= 1)
        {
            return; // the first terms are part of onboarding
        }
        var r = await resellers.GetContactAsync(e.ResellerId, ct);
        var terms = e.DiscountPct is null ? await resellers.GetCurrentTermsAsync(e.ResellerId, ct) : null;
        var facts = new List<(string, string)>
        {
            ("Terms version", e.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Reseller discount", $"{e.DiscountPct ?? terms!.DiscountPct:0.##}% off retail"),
        };
        if (e.EffectiveFrom is { } from)
        {
            facts.Add(("Effective from", from.ToOffset(TimeSpan.FromHours(5.5)).ToString("dd MMM yyyy, hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (!string.IsNullOrWhiteSpace(e.Notes))
        {
            facts.Add(("Notes", e.Notes!));
        }
        await Publisher.EmailAsync($"terms_changed:{e.ResellerId:N}:{e.Version}", EventType, Category.Info, r?.ResellerNumber, r?.Email, r?.ContactName, "RESELLER",
            EmailTemplates.Build(Shop, $"Your reseller terms changed (version {e.Version})", "Your commercial terms were updated",
                [new Para("New orders use these terms. Orders you already placed keep the prices they were placed at."), new Facts(facts),
                 new Para("Some products carry their own reseller discount instead of your general discount; the catalog shows your price.")],
                "View terms history", Publisher.CustomerLink("/reseller/terms")), ct);
    }
}

internal sealed class ResellerStatusChangedHandler(NotificationPublisher publisher, IResellerDirectory resellers, IClock clock) : NotificationHandler<ResellerStatusChanged>(publisher)
{
    protected override async Task HandleAsync(ResellerStatusChanged e, CancellationToken ct)
    {
        var r = await resellers.GetContactAsync(e.ResellerId, ct);
        var text = e.Status switch
        {
            "Active" => "Your reseller account is active. You can place orders and add money to your wallet.",
            "Frozen" => "Your reseller account is frozen. You can still sign in and see your orders and wallet, but new orders and deposits are paused.",
            "Suspended" => "Your reseller account is suspended and you cannot sign in. Please contact us.",
            "Closed" => "Your reseller account is closed. Please contact us if you have questions about your wallet balance.",
            _ => null,
        };
        if (text is null)
        {
            return;
        }
        await Publisher.EmailAsync($"reseller_status:{e.ResellerId:N}:{e.Status}:{clock.UtcNow:yyyyMMddHHmm}", EventType, Category.Info, r?.ResellerNumber, r?.Email,
            r?.ContactName, "RESELLER", EmailTemplates.Build(Shop, $"Your reseller account is {e.Status.ToLowerInvariant()}", $"Account {e.Status.ToLowerInvariant()}",
                [new Para(text)], e.Status is "Active" or "Frozen" ? "Open reseller portal" : null, Publisher.CustomerLink("/reseller")), ct);
    }
}

// ---------------- Inventory ----------------

internal sealed class DiscrepancyOpenedHandler(NotificationPublisher publisher, ICatalogLookup catalog) : NotificationHandler<InventoryDiscrepancyOpened>(publisher)
{
    protected override async Task HandleAsync(InventoryDiscrepancyOpened e, CancellationToken ct)
    {
        var sku = await catalog.FindSkuAsync(e.SkuId, ct);
        await Publisher.InAppAsync($"discrepancy:{e.DiscrepancyId:N}", EventType, Category.Warning, $"Stock discrepancy {e.Number} ({e.SourceType.ToLowerInvariant()} {e.SourceNumber})",
            $"{sku?.SkuCode ?? "SKU"}: expected {e.ExpectedQty}, found {e.ActualQty}. Investigate and resolve it.", "/inventory/discrepancies",
            P.Inventory.DiscrepancyResolve, e.BranchId, null, ct);
    }
}

internal sealed class TransferRequestedHandler(NotificationPublisher publisher, IBranchDirectory branches) : NotificationHandler<TransferRequested>(publisher)
{
    protected override async Task HandleAsync(TransferRequested e, CancellationToken ct)
    {
        var to = (await branches.FindAsync(e.DestinationBranchId, ct))?.Name ?? "another branch";
        await Publisher.InAppAsync($"transfer_requested:{e.TransferId:N}", EventType, Category.Info, $"Transfer {e.Number} waiting for approval",
            $"Stock requested for {to}.", $"/inventory/transfers/{e.TransferId}", P.Transfers.Approve, e.SourceBranchId, null, ct);
    }
}

internal sealed class AdjustmentRequestedHandler(NotificationPublisher publisher, ICatalogLookup catalog) : NotificationHandler<AdjustmentRequested>(publisher)
{
    protected override async Task HandleAsync(AdjustmentRequested e, CancellationToken ct)
    {
        var sku = await catalog.FindSkuAsync(e.SkuId, ct);
        await Publisher.InAppAsync($"adjustment_requested:{e.AdjustmentId:N}", EventType, Category.Info, $"Stock adjustment {e.Number} waiting for approval",
            $"{e.Kind} · {sku?.SkuCode ?? "SKU"} × {e.Quantity}.", "/inventory/adjustments", P.Inventory.AdjustApprove, e.BranchId, null, ct);
    }
}

// ---------------- Identity ----------------

internal sealed class SecurityAlertHandler(NotificationPublisher publisher, IClock clock) : NotificationHandler<SecurityAlertRaised>(publisher)
{
    protected override async Task HandleAsync(SecurityAlertRaised e, CancellationToken ct)
    {
        // One alert per person and kind per hour, however many times the lock repeats.
        var key = $"security:{e.Kind}:{e.UserId:N}:{clock.UtcNow:yyyyMMddHH}";
        var title = e.Kind == "APPROVAL_PIN_LOCKED" ? $"{e.DisplayName}'s approval PIN was locked" : $"{e.DisplayName}'s staff sign-in was locked";
        await Publisher.AlertAsync(key, e.Kind, Category.Critical, title, e.Detail, e.DisplayName, e.UserId, ct);
        await Publisher.InAppAsync(key, EventType, Category.Critical, title, e.Detail, "/exceptions?type=SENSITIVE_ALERT", P.Exceptions.Manage, null, null, ct);
        await Publisher.EmailOwnersAsync(key, EventType, Category.Critical, e.DisplayName,
            EmailTemplates.Build(Shop, $"CRITICAL: {title}", title,
                [new Para(e.Detail), new Para("If this was not the staff member, check the audit log and consider disabling the account.")],
                "Open Exception Center", Publisher.AdminLink("/exceptions?type=SENSITIVE_ALERT")), ct);
    }
}
