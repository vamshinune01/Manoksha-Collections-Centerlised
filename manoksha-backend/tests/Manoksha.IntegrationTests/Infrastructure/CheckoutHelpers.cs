using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Manoksha.IntegrationTests.Infrastructure;

public static class CheckoutHelpers
{
    /// <summary>Minimal valid PNG (8-byte signature + IHDR start) for proof uploads.</summary>
    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    public static object Delivery(string? mobile = null) => new
    {
        name = "Anitha Rao", mobile = mobile ?? ApiClient.NewMobile(), email = (string?)null,
        addressLine = "4-2-11, Temple Street", city = "Hanamkonda", state = "Telangana", pin = "506001",
    };

    public static async Task<(HttpClient Client, Guid ResellerId, string Mobile)> FundedResellerAsync(this ManokshaApiFactory factory, decimal balance, decimal discountPct = 10m)
    {
        var mobile = ApiClient.NewMobile();
        var id = (await factory.CreateResellerAsync(mobile, discountPct))["id"]!.GetValue<Guid>();
        var client = await factory.ResellerClientAsync(mobile);
        if (balance > 0)
        {
            var owner = await factory.OwnerClientAsync();
            await owner.PostAsJsonAsync($"/api/v1/admin/wallet/resellers/{id}/adjustments", new { direction = "Credit", amount = balance, reason = "test funding" }).OkJsonAsync();
        }
        return (client, id, mobile);
    }

    public static Task<HttpResponseMessage> CheckoutAsync(this HttpClient reseller, string? key, object delivery, params (Guid Sku, int Qty)[] lines)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reseller/checkout")
        {
            Content = JsonContent.Create(new { lines = lines.Select(l => new { skuId = l.Sku, quantity = l.Qty }), delivery, saveCustomer = true }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return reseller.SendAsync(request);
    }

    public static async Task<decimal> BalanceAsync(this HttpClient reseller) =>
        (await reseller.GetAsync("/api/v1/reseller/wallet").OkJsonAsync())["balance"]!.GetValue<decimal>();

    public static async Task<JsonNode> SubmitDepositAsync(this HttpClient reseller, decimal amount, string? reference, bool withProof = true, byte[]? proof = null)
    {
        var response = await reseller.SubmitDepositRawAsync(amount, reference, withProof, proof);
        return await Task.FromResult(response).OkJsonAsync();
    }

    public static Task<HttpResponseMessage> SubmitDepositRawAsync(this HttpClient reseller, decimal amount, string? reference, bool withProof = true, byte[]? proof = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "amount" },
            { new StringContent("PHONEPE"), "method" },
            { new StringContent(reference ?? string.Empty), "reference" },
        };
        if (withProof)
        {
            var file = new ByteArrayContent(proof ?? PngBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "proof", "payment.png");
        }
        return reseller.PostAsync("/api/v1/reseller/wallet/deposits", form);
    }

    public static string Utr() => "T" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
