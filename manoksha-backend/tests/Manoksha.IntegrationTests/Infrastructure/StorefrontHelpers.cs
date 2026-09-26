using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Manoksha.Modules.Orders;
using Manoksha.Modules.Payments;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Manoksha.IntegrationTests.Infrastructure;

public static class StorefrontHelpers
{
    public static async Task<HttpClient> CustomerClientAsync(this ManokshaApiFactory factory)
    {
        var tokens = await factory.RegisterCustomerAsync(ApiClient.NewMobile());
        return factory.Authorized(tokens.AccessToken);
    }

    public static Task<HttpResponseMessage> OnlineCheckoutAsync(this HttpClient customer, string? key, params (Guid Sku, int Qty)[] lines)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customer/checkout")
        {
            Content = JsonContent.Create(new { lines = lines.Select(l => new { skuId = l.Sku, quantity = l.Qty }), delivery = CheckoutHelpers.Delivery("+919848012345") }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return customer.SendAsync(request);
    }

    /// <summary>Checkout expected to reserve stock and start a payment.</summary>
    public static async Task<(Guid OrderId, string ProviderRef, JsonNode Result)> ReservedCheckoutAsync(this HttpClient customer, params (Guid Sku, int Qty)[] lines)
    {
        var result = await customer.OnlineCheckoutAsync(null, lines).OkJsonAsync();
        result["outcome"]!.GetValue<string>().Should().Be("PAYMENT_PENDING", result.ToJsonString());
        return (result["order"]!["id"]!.GetValue<Guid>(), ProviderRef(result), result);
    }

    public static string ProviderRef(JsonNode checkoutResult) => checkoutResult["payment"]!["redirectUrl"]!.GetValue<string>().Split('/')[^1];

    public static Task<HttpResponseMessage> SimulatorApproveAsync(this ManokshaApiFactory factory, string providerRef, decimal? paidAmount = null, bool sendWebhook = true) =>
        factory.CreateClient().PostAsJsonAsync($"/api/v1/payment-simulator/{providerRef}/approve", new { paidAmount, sendWebhook });

    public static Task<HttpResponseMessage> SimulatorDeclineAsync(this ManokshaApiFactory factory, string providerRef, bool sendWebhook = true) =>
        factory.CreateClient().PostAsJsonAsync($"/api/v1/payment-simulator/{providerRef}/decline", new { sendWebhook });

    /// <summary>Posts a webhook exactly as the simulated provider signs it.</summary>
    public static Task<HttpResponseMessage> PostWebhookAsync(this ManokshaApiFactory factory, string providerRef, string eventId, string? secret = null)
    {
        var body = $$"""{"eventId":"{{eventId}}","providerOrderRef":"{{providerRef}}","eventType":"payment.succeeded","amount":null}""";
        var key = Encoding.UTF8.GetBytes(secret ?? ManokshaApiFactory.SimulatorWebhookSecret);
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments/simulator") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Simulator-Signature", signature);
        return factory.CreateClient().SendAsync(request);
    }

    public static async Task<JsonNode> PaymentStatusAsync(this HttpClient customer, Guid orderId) =>
        await customer.GetAsync($"/api/v1/customer/orders/{orderId}/payment").OkJsonAsync();

    public static async Task<int> PollPaymentsAsync(this ManokshaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await PaymentJobs.PollAsync(scope.ServiceProvider, CancellationToken.None);
    }

    public static async Task<int> SweepReservationsAsync(this ManokshaApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await OrderJobs.ReleaseExpiredReservationsAsync(scope.ServiceProvider, CancellationToken.None);
    }

    /// <summary>Lets the 5-minute order payment window pass (and makes poll schedules due) while access tokens stay valid.</summary>
    public static void PassPaymentWindow(this ManokshaApiFactory factory) => factory.Clock.Advance(TimeSpan.FromMinutes(6));

    public static async Task<T?> ScalarAsync<T>(this ManokshaApiFactory factory, string sql)
    {
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<List<string>> ColumnAsync(this ManokshaApiFactory factory, string sql)
    {
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await r.ReadAsync())
        {
            list.Add(r.GetValue(0).ToString()!);
        }
        return list;
    }

    public static async Task<HttpStatusCode> StatusAsync(this Task<HttpResponseMessage> call) => (await call).StatusCode;
}
