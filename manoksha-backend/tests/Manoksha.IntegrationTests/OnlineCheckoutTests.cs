using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Manoksha.IntegrationTests;

/// <summary>
/// Online checkout and UPI payment (SPEC §11–§14, §19.1, §32, §33, §36): complete-basket branch routing, 5-minute reservation,
/// payment success/failure/expiry, late success (recover or reconcile), webhook idempotency and customer data isolation.
/// </summary>
[Collection(ApiCollection.Name)]
public class OnlineCheckoutTests(ManokshaApiFactory factory)
{
    private static readonly Guid Knr = DevelopmentSeedData.BranchKarimnagar;
    private static readonly Guid Hyd = DevelopmentSeedData.BranchHyderabad;
    private static readonly Guid Mlg = DevelopmentSeedData.BranchMulugu;

    private async Task<Guid> StockedSkuAsync(decimal price, params (Guid Branch, int Qty)[] stock)
    {
        var (sku, _) = await factory.CreatePricedSkuAsync(price);
        foreach (var (branch, qty) in stock)
        {
            await factory.StockUpAsync(branch, sku, qty, Math.Round(price / 2, 2));
        }
        return sku;
    }

    [Fact]
    public async Task Checkout_reserves_the_basket_and_a_valid_payment_confirms_the_order()
    {
        var sku = await StockedSkuAsync(1000m, (Knr, 5));
        var customer = await factory.CustomerClientAsync();

        var (orderId, providerRef, result) = await customer.ReservedCheckoutAsync((sku, 2));
        var order = result["order"]!;
        order["status"]!.GetValue<string>().Should().Be("PaymentPending");
        order["channel"]!.GetValue<string>().Should().Be("Online");
        order["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Knr);
        order["merchandiseTotal"]!.GetValue<decimal>().Should().Be(2000m);
        order["shippingFee"]!.GetValue<decimal>().Should().Be(100m, "₹100 per separate order (SPEC §18)");
        order["grandTotal"]!.GetValue<decimal>().Should().Be(2100m);
        order["lines"]![0]!["discountSource"]!.GetValue<string>().Should().Be("NONE");
        var payment = result["payment"]!;
        payment["status"]!.GetValue<string>().Should().Be("PENDING");
        payment["amount"]!.GetValue<decimal>().Should().Be(2100m);
        payment["expiresAt"]!.GetValue<DateTimeOffset>().Should().BeCloseTo(factory.Clock.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30), "default 5-minute window (SPEC §13)");
        (await factory.StockAsync(Knr, sku)).Should().Be(3);
        (await factory.StockAsync(Knr, sku, "Reserved")).Should().Be(2);

        // A price change after checkout never changes what this order costs (SPEC §33).
        await factory.SetRetailPriceAsync(sku, 1500m);

        (await factory.SimulatorApproveAsync(providerRef)).EnsureSuccessStatusCode();
        var status = await customer.PaymentStatusAsync(orderId);
        status["orderStatus"]!.GetValue<string>().Should().Be("Confirmed");
        status["payment"]!["status"]!.GetValue<string>().Should().Be("SUCCESS");
        (await factory.StockAsync(Knr, sku, "Reserved")).Should().Be(0);
        (await factory.StockAsync(Knr, sku)).Should().Be(3);
        (await factory.LayersAsync(Knr, sku)).Sum(l => l.Remaining).Should().Be(3, "FIFO cost is consumed when the reservation is sold");

        var owner = await factory.OwnerClientAsync();
        var adminView = await owner.GetAsync($"/api/v1/admin/orders/{orderId}").OkJsonAsync();
        adminView["grandTotal"]!.GetValue<decimal>().Should().Be(2100m);
        adminView["costOfGoods"]!.GetValue<decimal>().Should().Be(1000m, "2 units × ₹500 FIFO cost");
        adminView["history"]!.AsArray().Select(h => h!["toStatus"]!.GetValue<string>()).Should().Equal("PaymentPending", "Confirmed");
    }

    [Fact]
    public async Task Complete_basket_routes_by_priority_and_is_never_split()
    {
        var p2 = await StockedSkuAsync(100m, (Hyd, 3));
        var p3 = await StockedSkuAsync(100m, (Knr, 1), (Hyd, 1), (Mlg, 3));
        var customer = await factory.CustomerClientAsync();

        (await customer.ReservedCheckoutAsync((p2, 2))).Result["order"]!["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Hyd);
        (await customer.ReservedCheckoutAsync((p3, 2))).Result["order"]!["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Mlg);
        (await factory.StockAsync(Knr, p3)).Should().Be(1, "partial stock at higher-priority branches is untouched");
        (await factory.StockAsync(Hyd, p3)).Should().Be(1);

        // Each branch holds only part of the basket → no order, no payment, an inquiry reference (SPEC §12).
        var a = await StockedSkuAsync(100m, (Knr, 2));
        var b = await StockedSkuAsync(100m, (Hyd, 2));
        var result = await customer.OnlineCheckoutAsync(null, (a, 1), (b, 1)).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("UNFULFILLABLE");
        result["order"].Should().BeNull();
        result["payment"].Should().BeNull();
        result["inquiry"]!["reference"]!.GetValue<string>().Should().MatchRegex("^MC-FUL-\\d{8}$");
        result["inquiry"]!["whatsAppUrl"]!.GetValue<string>().Should().StartWith("https://wa.me/919741404304");
        (await factory.StockAsync(Knr, a)).Should().Be(2);
        (await factory.StockAsync(Hyd, b)).Should().Be(2);
        var orders = await customer.GetAsync("/api/v1/customer/orders").OkJsonAsync();
        orders.AsArray().Should().HaveCount(2, "the unfulfillable attempt created no order");
    }

    [Fact]
    public async Task Two_customers_racing_for_the_last_item_only_one_reserves_it()
    {
        var sku = await StockedSkuAsync(500m, (Knr, 1));
        var customers = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => factory.CustomerClientAsync()));
        var results = await Task.WhenAll(customers.Select(c => c.OnlineCheckoutAsync(null, (sku, 1))));
        var outcomes = new List<string>();
        foreach (var r in results)
        {
            outcomes.Add((await r.ReadJsonAsync())["outcome"]!.GetValue<string>());
        }
        outcomes.Count(o => o == "PAYMENT_PENDING").Should().Be(1);
        outcomes.Count(o => o == "UNFULFILLABLE").Should().Be(4);
        (await factory.StockAsync(Knr, sku, "Reserved")).Should().Be(1);
        (await factory.StockAsync(Knr, sku)).Should().Be(0);
    }

    [Fact]
    public async Task Repeated_place_order_clicks_are_one_order_one_reservation_one_payment()
    {
        var sku = await StockedSkuAsync(300m, (Knr, 10));
        var customer = await factory.CustomerClientAsync();
        var key = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => customer.OnlineCheckoutAsync(key, (sku, 2))));
        var ids = new HashSet<Guid>();
        foreach (var r in results.Where(r => r.StatusCode == HttpStatusCode.OK))
        {
            ids.Add((await r.ReadJsonAsync())["order"]!["id"]!.GetValue<Guid>());
        }
        var again = await customer.OnlineCheckoutAsync(key, (sku, 2)).OkJsonAsync();
        ids.Add(again["order"]!["id"]!.GetValue<Guid>());
        ids.Should().HaveCount(1);
        (await factory.StockAsync(Knr, sku, "Reserved")).Should().Be(2);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM payments.payment_attempts WHERE reference_id = '{ids.Single()}'")).Should().Be(1);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM payments.simulator_transactions t JOIN payments.payment_attempts a ON a.id = t.merchant_attempt_id WHERE a.reference_id = '{ids.Single()}'"))
            .Should().Be(1, "the provider session is created once");
    }

    [Fact]
    public async Task Payment_failure_releases_the_reservation_immediately()
    {
        var sku = await StockedSkuAsync(400m, (Knr, 3));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 3));
        (await factory.StockAsync(Knr, sku)).Should().Be(0);

        (await factory.SimulatorDeclineAsync(providerRef)).EnsureSuccessStatusCode();
        var status = await customer.PaymentStatusAsync(orderId);
        status["orderStatus"]!.GetValue<string>().Should().Be("PaymentFailed");
        status["payment"]!["status"]!.GetValue<string>().Should().Be("FAILED");
        status["payment"]!["redirectUrl"].Should().BeNull();
        (await factory.StockAsync(Knr, sku)).Should().Be(3, "payment failure releases inventory immediately (SPEC §13)");
        (await factory.StockAsync(Knr, sku, "Reserved")).Should().Be(0);
        (await factory.ScalarAsync<string>($"SELECT status FROM orders.reservations WHERE order_id = '{orderId}'")).Should().Be("Released");
    }

    [Fact]
    public async Task Payment_initiation_failure_confirms_nothing_and_releases_the_reservation()
    {
        var sku = await StockedSkuAsync(250m, (Knr, 2));
        var failing = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IPaymentGateway>();
            s.AddScoped<IPaymentGateway, UnreachableGateway>();
        }));
        var customer = failing.CreateClient();
        var mobile = ApiClient.NewMobile();
        (await customer.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile, context = "customer" })).EnsureSuccessStatusCode();
        var verify = await (await customer.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, context = "customer", code = ApiClient.LastOtpFor(mobile) })).ReadJsonAsync();
        var registered = await customer.PostAsJsonAsync("/api/v1/auth/customer/register",
            new { registrationToken = verify["challengeToken"]!.GetValue<string>(), fullName = "Gateway Down", email = "down@example.com" }).OkJsonAsync();
        customer.DefaultRequestHeaders.Authorization = new("Bearer", ApiClient.ToTokens(registered).AccessToken);

        var result = await customer.OnlineCheckoutAsync(null, (sku, 2)).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("PAYMENT_NOT_COMPLETED");
        result["payment"]!["status"]!.GetValue<string>().Should().Be("FAILED");
        result["order"]!["status"]!.GetValue<string>().Should().Be("PaymentFailed");
        (await factory.StockAsync(Knr, sku)).Should().Be(2);
        (await factory.StockAsync(Knr, sku, "Reserved")).Should().Be(0);
    }

    [Fact]
    public async Task Reservation_expiry_releases_stock_and_the_poller_expires_the_payment()
    {
        var sku = await StockedSkuAsync(600m, (Knr, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, _, _) = await customer.ReservedCheckoutAsync((sku, 1));

        factory.PassPaymentWindow();
        (await factory.SweepReservationsAsync()).Should().BeGreaterThan(0);
        (await factory.StockAsync(Knr, sku)).Should().Be(1, "reservation expiry releases inventory to AVAILABLE (SPEC §13)");
        (await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("PaymentExpired");

        await factory.PollPaymentsAsync();
        (await factory.ScalarAsync<string>($"SELECT status FROM payments.payment_attempts WHERE reference_id = '{orderId}'")).Should().Be("Expired");
        (await factory.ScalarAsync<string>($"SELECT status FROM orders.reservations WHERE order_id = '{orderId}'")).Should().Be("Expired");
    }

    [Fact]
    public async Task Late_success_recovers_the_order_at_another_branch_with_the_original_prices()
    {
        var sku = await StockedSkuAsync(800m, (Knr, 1), (Hyd, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));

        factory.PassPaymentWindow();
        await factory.SweepReservationsAsync();
        // Meanwhile another customer buys the Karimnagar piece.
        var other = await factory.CustomerClientAsync();
        var (otherOrder, otherRef, _) = await other.ReservedCheckoutAsync((sku, 1));
        (await factory.SimulatorApproveAsync(otherRef)).EnsureSuccessStatusCode();
        (await other.PaymentStatusAsync(otherOrder))["orderStatus"]!.GetValue<string>().Should().Be("Confirmed");
        await factory.SetRetailPriceAsync(sku, 999m);

        // The delayed UPI success arrives: recheck → Hyderabad can fulfil the complete basket → recovered (SPEC §14.2).
        (await factory.SimulatorApproveAsync(providerRef)).EnsureSuccessStatusCode();
        var status = await customer.PaymentStatusAsync(orderId);
        status["payment"]!["status"]!.GetValue<string>().Should().Be("ORDER_RECOVERED");
        status["orderStatus"]!.GetValue<string>().Should().Be("Confirmed");
        var order = await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync();
        order["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Hyd);
        order["grandTotal"]!.GetValue<decimal>().Should().Be(900m, "late recovery keeps the original price snapshot (ADR-001 §12)");
        (await factory.StockAsync(Hyd, sku)).Should().Be(0);
        (await factory.StockAsync(Knr, sku)).Should().Be(0);
        (await factory.LayersAsync(Hyd, sku)).Sum(l => l.Remaining).Should().Be(0);
    }

    [Fact]
    public async Task Late_success_without_stock_is_never_confirmed_and_opens_a_reconciliation_case()
    {
        var sku = await StockedSkuAsync(700m, (Knr, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));

        factory.PassPaymentWindow();
        await factory.SweepReservationsAsync();
        await factory.PollPaymentsAsync(); // provider still pending → attempt EXPIRED
        var other = await factory.CustomerClientAsync();
        var (otherOrder, otherRef, _) = await other.ReservedCheckoutAsync((sku, 1));
        (await factory.SimulatorApproveAsync(otherRef)).EnsureSuccessStatusCode();
        await other.PaymentStatusAsync(otherOrder);

        (await factory.SimulatorApproveAsync(providerRef)).EnsureSuccessStatusCode();
        var status = await customer.PaymentStatusAsync(orderId);
        status["payment"]!["status"]!.GetValue<string>().Should().Be("PAYMENT_RECONCILIATION_REQUIRED");
        status["orderStatus"]!.GetValue<string>().Should().Be("PaymentExpired", "a late payment is never blindly confirmed (SPEC §37)");
        (await factory.StockAsync(Knr, sku)).Should().Be(0);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM inventory.inventory_movements WHERE sku_id = '{sku}' AND movement_type = 'SALE'"))
            .Should().Be(1, "the single piece was sold once");

        var owner = await factory.OwnerClientAsync();
        var cases = await owner.GetAsync("/api/v1/admin/payment-reconciliations").OkJsonAsync();
        var rec = cases.AsArray().Single(c => c!["referenceId"]!.GetValue<Guid>() == orderId)!;
        rec["reasonCode"]!.GetValue<string>().Should().Be("INVENTORY_UNAVAILABLE_AFTER_LATE_PAYMENT");
        rec["status"]!.GetValue<string>().Should().Be("Open");
        rec["expectedAmount"]!.GetValue<decimal>().Should().Be(800m);
        rec["paidAmount"]!.GetValue<decimal>().Should().Be(800m);
        rec["providerPaymentRef"]!.GetValue<string>().Should().StartWith("SIMPAY");
        rec["caseNumber"]!.GetValue<string>().Should().MatchRegex("^MC-REC-\\d{6}$");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM audit.audit_log WHERE action = 'payments.reconciliation.opened' AND entity_id = '{rec["id"]}'"))
            .Should().Be(1);

        // Repeated provider callbacks after the sink change nothing (SPEC §14.2).
        (await factory.PostWebhookAsync(providerRef, "late-dup-" + Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();
        await factory.PollPaymentsAsync();
        (await owner.GetAsync("/api/v1/admin/payment-reconciliations").OkJsonAsync()).AsArray().Count(c => c!["referenceId"]!.GetValue<Guid>() == orderId).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_and_concurrent_webhooks_confirm_once()
    {
        var sku = await StockedSkuAsync(350m, (Knr, 4));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 2));
        (await factory.SimulatorApproveAsync(providerRef, sendWebhook: false)).EnsureSuccessStatusCode();

        var eventId = "evt-" + Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => factory.PostWebhookAsync(providerRef, eventId)));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var bodies = new List<bool>();
        foreach (var r in responses)
        {
            bodies.Add((await r.ReadJsonAsync())["duplicate"]!.GetValue<bool>());
        }
        bodies.Count(d => !d).Should().Be(1, "the inbox stores each provider event once");
        // Different event ids for the same payment (provider retries) are also harmless.
        await Task.WhenAll(Enumerable.Range(0, 3).Select(i => factory.PostWebhookAsync(providerRef, $"{eventId}-{i}")));

        (await factory.ScalarAsync<long>($"SELECT count(*) FROM payments.provider_events WHERE provider_event_id = '{eventId}'")).Should().Be(1);
        (await customer.PaymentStatusAsync(orderId))["orderStatus"]!.GetValue<string>().Should().Be("Confirmed");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM orders.order_status_changes WHERE order_id = '{orderId}' AND to_status = 'Confirmed'")).Should().Be(1);
        (await factory.LayersAsync(Knr, sku)).Sum(l => l.Remaining).Should().Be(2, "stock was sold once");
        (await factory.StockAsync(Knr, sku)).Should().Be(2);
    }

    [Fact]
    public async Task Webhook_with_an_invalid_signature_is_rejected_without_effect()
    {
        var sku = await StockedSkuAsync(150m, (Knr, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));
        var eventId = "forged-" + Guid.NewGuid().ToString("N");
        (await factory.PostWebhookAsync(providerRef, eventId, secret: "not-the-secret")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM payments.provider_events WHERE provider_event_id = '{eventId}'")).Should().Be(0);
        (await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("PaymentPending");
    }

    [Fact]
    public async Task A_lost_webhook_is_discovered_by_the_poller()
    {
        var sku = await StockedSkuAsync(220m, (Knr, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));
        (await factory.SimulatorApproveAsync(providerRef, sendWebhook: false)).EnsureSuccessStatusCode();
        (await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("PaymentPending");

        factory.Clock.Advance(TimeSpan.FromSeconds(61)); // poll schedule due, still inside the 5-minute window
        await factory.PollPaymentsAsync();
        (await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Confirmed");
    }

    [Fact]
    public async Task Paid_amount_mismatch_is_not_confirmed_and_needs_reconciliation()
    {
        var sku = await StockedSkuAsync(500m, (Knr, 2));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));
        (await factory.SimulatorApproveAsync(providerRef, paidAmount: 1m)).EnsureSuccessStatusCode();

        var status = await customer.PaymentStatusAsync(orderId);
        status["payment"]!["status"]!.GetValue<string>().Should().Be("PAYMENT_RECONCILIATION_REQUIRED");
        status["orderStatus"]!.GetValue<string>().Should().Be("PaymentFailed");
        (await factory.StockAsync(Knr, sku)).Should().Be(2);
        var owner = await factory.OwnerClientAsync();
        var rec = (await owner.GetAsync("/api/v1/admin/payment-reconciliations").OkJsonAsync()).AsArray().Single(c => c!["referenceId"]!.GetValue<Guid>() == orderId)!;
        rec["reasonCode"]!.GetValue<string>().Should().Be("AMOUNT_MISMATCH");
        rec["paidAmount"]!.GetValue<decimal>().Should().Be(1m);
    }

    [Fact]
    public async Task Owner_records_reconciliation_actions_with_refund_markers_and_history()
    {
        var sku = await StockedSkuAsync(640m, (Knr, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));
        (await factory.SimulatorApproveAsync(providerRef, paidAmount: 700m)).EnsureSuccessStatusCode();
        var owner = await factory.OwnerClientAsync();
        var rec = (await owner.GetAsync("/api/v1/admin/payment-reconciliations").OkJsonAsync()).AsArray().Single(c => c!["referenceId"]!.GetValue<Guid>() == orderId)!;
        var id = rec["id"]!.GetValue<Guid>();

        // The Owner is alerted (CRITICAL event; shown on every admin page until the case is handled).
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM platform.outbox_messages WHERE type = 'payments.reconciliation_required' AND payload::text LIKE '%{rec["caseNumber"]}%'"))
            .Should().Be(1);

        var url = $"/api/v1/admin/payment-reconciliations/{id}/actions";
        var manager = await factory.UserClientAsync(SystemRoles.BranchManager, Knr);
        (await manager.PostAsJsonAsync(url, new { action = "NOTE", note = "Called the customer" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "reconciliation is Owner-only");

        (await (await owner.PostAsJsonAsync(url, new { action = "REFUND_COMPLETED", note = "Refunded", externalRefundRef = (string?)null })).ErrorCodeAsync())
            .Should().Be("REFUND_REFERENCE_REQUIRED");
        (await (await owner.PostAsJsonAsync(url, new { action = "REFUND_INITIATED", note = "" })).ErrorCodeAsync()).Should().Be("REASON_REQUIRED");
        await owner.PostAsJsonAsync(url, new { action = "NOTE", note = "Customer paid ₹700 instead of ₹740; called them" }).OkJsonAsync();
        (await owner.PostAsJsonAsync(url, new { action = "REFUND_INITIATED", note = "Refunding ₹700 by bank transfer" }).OkJsonAsync())["status"]!.GetValue<string>()
            .Should().Be("RefundInitiated");
        var done = await owner.PostAsJsonAsync(url, new { action = "REFUND_COMPLETED", note = "Refund sent", externalRefundRef = "NEFT12345" }).OkJsonAsync();
        done["status"]!.GetValue<string>().Should().Be("RefundCompleted");
        done["externalRefundRef"]!.GetValue<string>().Should().Be("NEFT12345");
        (await owner.PostAsJsonAsync(url, new { action = "RESOLVED", note = "Closed" }).OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Resolved");
        (await (await owner.PostAsJsonAsync(url, new { action = "REFUND_INITIATED", note = "again" })).ErrorCodeAsync()).Should().Be("RECONCILIATION_STATUS_INVALID");

        var history = await owner.GetAsync($"/api/v1/admin/payment-reconciliations/{id}/history").OkJsonAsync();
        history.AsArray().Select(h => h!["action"]!.GetValue<string>()).Should().Equal("NOTE", "REFUND_INITIATED", "REFUND_COMPLETED", "RESOLVED");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM audit.audit_log WHERE entity_id = '{id}' AND action LIKE 'payments.reconciliation.%'")).Should().Be(5, "opened + 4 actions");
        await using var c = new Npgsql.NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var tamper = new Npgsql.NpgsqlCommand($"UPDATE payments.reconciliation_history SET note = 'x' WHERE reconciliation_id = '{id}'", c);
        (await FluentActions.Awaiting(() => tamper.ExecuteNonQueryAsync()).Should().ThrowAsync<Npgsql.PostgresException>()).Which.MessageText.Should().Contain("append_only_violation");
    }

    [Fact]
    public async Task Customer_manages_own_profile_and_sees_order_charges()
    {
        var customer = await factory.CustomerClientAsync();
        var profile = await customer.GetAsync("/api/v1/customer/profile").OkJsonAsync();
        profile["mobile"]!.GetValue<string>().Should().StartWith("+91");
        (await (await customer.PutAsJsonAsync("/api/v1/customer/profile", new { fullName = "Ravi Kumar", email = "not-an-email" })).ErrorCodeAsync()).Should().Be("EMAIL_INVALID");
        var updated = await customer.PutAsJsonAsync("/api/v1/customer/profile", new { fullName = "Ravi Kumar", email = "ravi@example.com" }).OkJsonAsync();
        updated["fullName"]!.GetValue<string>().Should().Be("Ravi Kumar");
        (await customer.GetAsync("/api/v1/auth/me").OkJsonAsync())["displayName"]!.GetValue<string>().Should().Be("Ravi Kumar");

        var reseller = await factory.NewActiveResellerAsync();
        (await reseller.GetAsync("/api/v1/customer/profile")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/v1/catalog/order-charges").OkJsonAsync())["shippingFeePerOrder"]!.GetValue<decimal>().Should().Be(100m);
    }

    [Fact]
    public async Task Priority_change_during_payment_keeps_the_reserved_branch()
    {
        var sku = await StockedSkuAsync(450m, (Knr, 1), (Hyd, 1));
        var customer = await factory.CustomerClientAsync();
        var (orderId, providerRef, _) = await customer.ReservedCheckoutAsync((sku, 1));

        var owner = await factory.OwnerClientAsync();
        var current = await owner.GetAsync("/api/v1/admin/fulfillment-priority").OkJsonAsync();
        var version = current["version"]!.GetValue<int>();
        var ids = current["entries"]!.AsArray().Select(e => e!["branchId"]!.GetValue<Guid>()).ToList();
        await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = Enumerable.Reverse(ids).ToList(), expectedVersion = version, reason = "test" }).OkJsonAsync();
        try
        {
            (await factory.SimulatorApproveAsync(providerRef)).EnsureSuccessStatusCode();
            var order = await customer.GetAsync($"/api/v1/customer/orders/{orderId}").OkJsonAsync();
            order["status"]!.GetValue<string>().Should().Be("Confirmed");
            order["fulfillmentBranchId"]!.GetValue<Guid>().Should().Be(Knr, "an existing reservation keeps its branch (SPEC §11, §33)");
            (await factory.StockAsync(Hyd, sku)).Should().Be(1);
        }
        finally
        {
            await owner.PutAsJsonAsync("/api/v1/admin/fulfillment-priority", new { branchIds = ids, expectedVersion = version + 1, reason = "restore" }).OkJsonAsync();
        }
    }

    [Fact]
    public async Task Storefront_is_public_but_orders_are_private_to_each_customer()
    {
        var sku = await StockedSkuAsync(1200m, (Knr, 2));
        var (hiddenSku, _) = await factory.CreatePricedSkuAsync(0m); // never priced → not sellable
        var anonymous = factory.CreateClient();
        var quote = await anonymous.PostAsJsonAsync("/api/v1/catalog/cart-quote", new { skuIds = new[] { sku, hiddenSku } }).OkJsonAsync();
        quote[0]!["sellable"]!.GetValue<bool>().Should().BeTrue();
        quote[0]!["price"]!.GetValue<decimal>().Should().Be(1200m);
        quote[0]!["inStock"]!.GetValue<bool>().Should().BeTrue();
        quote[1]!["sellable"]!.GetValue<bool>().Should().BeFalse();
        (await anonymous.GetAsync("/api/v1/catalog/products?pageSize=100").OkJsonAsync())["total"]!.GetValue<int>().Should().BeGreaterThan(0);
        (await anonymous.GetAsync("/api/v1/catalog/categories")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Login is required to order (ADR-001 §10); resellers use their own checkout.
        (await anonymous.OnlineCheckoutAsync(null, (sku, 1))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var reseller = await factory.NewActiveResellerAsync();
        (await reseller.OnlineCheckoutAsync(null, (sku, 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var alice = await factory.CustomerClientAsync();
        var bob = await factory.CustomerClientAsync();
        var (aliceOrder, _, _) = await alice.ReservedCheckoutAsync((sku, 1));
        (await bob.GetAsync($"/api/v1/customer/orders/{aliceOrder}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.GetAsync($"/api/v1/customer/orders/{aliceOrder}/payment")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.GetAsync("/api/v1/customer/orders").OkJsonAsync()).AsArray().Should().BeEmpty();
        (await alice.GetAsync("/api/v1/customer/orders").OkJsonAsync()).AsArray().Should().ContainSingle();
        (await alice.GetAsync("/api/v1/customer/delivery-defaults").OkJsonAsync())["pin"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        (await alice.GetAsync("/api/v1/customer/orders").OkJsonAsync())[0]!["costOfGoods"].Should().BeNull("cost is Owner-only");
    }

    [Fact]
    public async Task Online_wallet_deposit_is_credited_once_after_provider_confirmation()
    {
        var (reseller, _, _) = await factory.FundedResellerAsync(100m);
        var key = Guid.NewGuid().ToString("N");
        var request = () =>
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reseller/wallet/deposits/online") { Content = JsonContent.Create(new { amount = 2500m }) };
            r.Headers.Add("Idempotency-Key", key);
            return reseller.SendAsync(r);
        };
        var deposit = await request().OkJsonAsync();
        (await request().OkJsonAsync())["id"]!.GetValue<Guid>().Should().Be(deposit["id"]!.GetValue<Guid>());
        deposit["status"]!.GetValue<string>().Should().Be("Pending");
        var providerRef = deposit["payment"]!["redirectUrl"]!.GetValue<string>().Split('/')[^1];
        (await reseller.BalanceAsync()).Should().Be(100m, "nothing is credited before the provider confirms");

        (await factory.SimulatorApproveAsync(providerRef, sendWebhook: false)).EnsureSuccessStatusCode();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i => factory.PostWebhookAsync(providerRef, $"dep-{providerRef}-{i % 2}")));
        await factory.PollPaymentsAsync();
        var status = await reseller.GetAsync($"/api/v1/reseller/wallet/deposits/online/{deposit["id"]}").OkJsonAsync();
        status["status"]!.GetValue<string>().Should().Be("Credited");
        (await reseller.BalanceAsync()).Should().Be(2600m, "credited exactly once");
        (await factory.ScalarAsync<long>($"SELECT count(*) FROM wallet.ledger_entries WHERE online_deposit_id = '{deposit["id"]}'")).Should().Be(1);

        // A declined deposit credits nothing.
        var declinedReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reseller/wallet/deposits/online") { Content = JsonContent.Create(new { amount = 700m }) };
        declinedReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var declined = await reseller.SendAsync(declinedReq).OkJsonAsync();
        (await factory.SimulatorDeclineAsync(declined["payment"]!["redirectUrl"]!.GetValue<string>().Split('/')[^1])).EnsureSuccessStatusCode();
        (await reseller.GetAsync($"/api/v1/reseller/wallet/deposits/online/{declined["id"]}").OkJsonAsync())["status"]!.GetValue<string>().Should().Be("Failed");
        (await reseller.BalanceAsync()).Should().Be(2600m);
    }

    [Fact]
    public async Task A_deposit_paid_after_its_window_is_still_credited_once()
    {
        var (reseller, _, mobile) = await factory.FundedResellerAsync(50m);
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reseller/wallet/deposits/online") { Content = JsonContent.Create(new { amount = 1000m }) };
        r.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var deposit = await reseller.SendAsync(r).OkJsonAsync();
        var providerRef = deposit["payment"]!["redirectUrl"]!.GetValue<string>().Split('/')[^1];

        factory.Clock.Advance(TimeSpan.FromMinutes(14));
        factory.Clock.Advance(TimeSpan.FromMinutes(2)); // past the 15-minute deposit window
        await factory.PollPaymentsAsync();
        (await factory.ScalarAsync<string>($"SELECT status FROM wallet.online_deposits WHERE id = '{deposit["id"]}'")).Should().Be("Expired");

        (await factory.SimulatorApproveAsync(providerRef)).EnsureSuccessStatusCode();
        var fresh = await factory.ResellerClientAsync(mobile); // the earlier access token has expired meanwhile
        (await fresh.BalanceAsync()).Should().Be(1050m, "money received late is credited, once (SPEC §17.1)");
        (await factory.ScalarAsync<string>($"SELECT status FROM payments.payment_attempts WHERE reference_id = '{deposit["id"]}'")).Should().Be("Success");
    }

    [Fact]
    public async Task Changing_the_reservation_window_affects_only_new_reservations()
    {
        var sku = await StockedSkuAsync(90m, (Knr, 5));
        var customer = await factory.CustomerClientAsync();
        var first = await customer.ReservedCheckoutAsync((sku, 1));
        var owner = await factory.OwnerClientAsync();
        var setting = (await owner.GetAsync("/api/v1/admin/settings").OkJsonAsync()).AsArray().Single(s => s!["key"]!.GetValue<string>() == SettingKeys.ReservationMinutes)!;
        var version = setting["version"]!.GetValue<int>();
        await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.ReservationMinutes}", new { value = 10, expectedVersion = version, reason = "busy season" }).OkJsonAsync();
        try
        {
            var second = await customer.ReservedCheckoutAsync((sku, 1));
            var firstExpiry = first.Result["payment"]!["expiresAt"]!.GetValue<DateTimeOffset>();
            var secondExpiry = second.Result["payment"]!["expiresAt"]!.GetValue<DateTimeOffset>();
            (secondExpiry - firstExpiry).Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));
            (await factory.ScalarAsync<DateTime>($"SELECT expires_at FROM orders.reservations WHERE order_id = '{first.OrderId}'"))
                .Should().BeCloseTo(firstExpiry.UtcDateTime, TimeSpan.FromSeconds(1), "an existing reservation keeps its own expiry");
        }
        finally
        {
            await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.ReservationMinutes}", new { value = 5, expectedVersion = version + 1, reason = "restore" }).OkJsonAsync();
        }
    }

    [Fact]
    public async Task Frozen_reseller_cannot_start_an_online_deposit()
    {
        var (reseller, resellerId, _) = await factory.FundedResellerAsync(10m);
        var owner = await factory.OwnerClientAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/resellers/{resellerId}/status", new { status = "Frozen", reason = "review" }).OkJsonAsync();
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reseller/wallet/deposits/online") { Content = JsonContent.Create(new { amount = 100m }) };
        r.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await reseller.SendAsync(r);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ErrorCodeAsync()).Should().Be("RESELLER_CANNOT_DEPOSIT");
    }

    /// <summary>A provider that cannot be reached (SPEC §33 "Payment initiation fails").</summary>
    internal sealed class UnreachableGateway : IPaymentGateway
    {
        public string Provider => "simulator";

        public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken) =>
            throw new PaymentGatewayException("Provider unreachable");

        public Task<ProviderPaymentStatus> GetStatusAsync(string providerOrderRef, CancellationToken cancellationToken) =>
            throw new PaymentGatewayException("Provider unreachable");

        public ProviderWebhook? ParseWebhook(IReadOnlyDictionary<string, string> headers, string body) => null;
    }
}
