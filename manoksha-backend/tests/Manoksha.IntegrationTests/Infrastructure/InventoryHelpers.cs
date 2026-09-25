using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace Manoksha.IntegrationTests.Infrastructure;

/// <summary>Builds catalog/purchasing/stock scenarios through the public API (as the Owner unless stated).</summary>
public static class InventoryHelpers
{
    public static async Task<Guid> CreateSkuAsync(this ManokshaApiFactory factory, string trackingMode = "Quantity", string? name = null)
    {
        var owner = await factory.OwnerClientAsync();
        var category = await owner.PostAsJsonAsync("/api/v1/admin/catalog/categories", new { name = "Cat " + Guid.NewGuid().ToString("N")[..6], sortOrder = 1, isActive = true, reason = "setup" }).OkJsonAsync();
        var product = await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId = category["id"]!.GetValue<Guid>(), name = name ?? "Item " + Guid.NewGuid().ToString("N")[..6], trackingMode,
            availableForRetail = true, availableForReseller = true, reason = "setup",
        }).OkJsonAsync(HttpStatusCode.Created);
        var detail = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{product["id"]!.GetValue<Guid>()}/variants",
            new { generateBarcode = false, reason = "setup" }).OkJsonAsync();
        return detail["variants"]![0]!["skuId"]!.GetValue<Guid>();
    }

    public static async Task<Guid> CreateSupplierAsync(this ManokshaApiFactory factory)
    {
        var owner = await factory.OwnerClientAsync();
        var s = await owner.PostAsJsonAsync("/api/v1/admin/purchasing/suppliers", new { name = "Supplier " + Guid.NewGuid().ToString("N")[..6], isActive = true, reason = "setup" }).OkJsonAsync();
        return s["id"]!.GetValue<Guid>();
    }

    /// <summary>Creates and issues a PO. Lines: (skuId, qty, expected cost).</summary>
    public static async Task<JsonNode> CreateIssuedPoAsync(this ManokshaApiFactory factory, Guid branchId, params (Guid Sku, int Qty, decimal Cost)[] lines)
    {
        var owner = await factory.OwnerClientAsync();
        var supplierId = await factory.CreateSupplierAsync();
        var po = await owner.PostAsJsonAsync("/api/v1/admin/purchasing/purchase-orders", new
        {
            supplierId, receivingBranchId = branchId, lines = lines.Select(l => new { skuId = l.Sku, orderedQty = l.Qty, expectedUnitCost = l.Cost }), reason = "restock",
        }).OkJsonAsync();
        return await owner.PostAsJsonAsync($"/api/v1/admin/purchasing/purchase-orders/{po["id"]!.GetValue<Guid>()}/issue", new { reason = "send to supplier" }).OkJsonAsync();
    }

    public static Guid LineId(JsonNode po, Guid skuId) =>
        po["lines"]!.AsArray().Single(l => l!["skuId"]!.GetValue<Guid>() == skuId)!["id"]!.GetValue<Guid>();

    public static Task<HttpResponseMessage> ReceiveAsync(this HttpClient client, JsonNode po, string invoice, string? key, params (Guid Sku, int Received, int Damaged, decimal Cost)[] lines)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/purchasing/purchase-orders/{po["id"]!.GetValue<Guid>()}/receipts")
        {
            Content = JsonContent.Create(new
            {
                supplierInvoiceRef = invoice,
                lines = lines.Select(l => new { poLineId = LineId(po, l.Sku), receivedQty = l.Received, damagedQty = l.Damaged, unitCost = l.Cost }),
            }),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }

    /// <summary>Puts stock at a branch through a real purchase order + goods receipt.</summary>
    public static async Task<JsonNode> StockUpAsync(this ManokshaApiFactory factory, Guid branchId, Guid skuId, int qty, decimal cost)
    {
        var po = await factory.CreateIssuedPoAsync(branchId, (skuId, qty, cost));
        var owner = await factory.OwnerClientAsync();
        return await owner.ReceiveAsync(po, "INV-" + Guid.NewGuid().ToString("N")[..6], null, (skuId, qty, 0, cost)).OkJsonAsync();
    }

    public static async Task<int> StockAsync(this ManokshaApiFactory factory, Guid branchId, Guid skuId, string status = "Available")
    {
        var owner = await factory.OwnerClientAsync();
        var rows = await owner.GetAsync($"/api/v1/admin/inventory/stock?branchId={branchId}&skuId={skuId}").OkJsonAsync();
        var row = rows.AsArray().SingleOrDefault();
        return row?["byStatus"]?[status]?.GetValue<int>() ?? 0;
    }

    /// <summary>Remaining FIFO layers (unit cost, remaining qty, layer date) for a SKU at a branch, oldest first.</summary>
    public static async Task<List<(decimal Cost, int Remaining)>> LayersAsync(this ManokshaApiFactory factory, Guid branchId, Guid skuId)
    {
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT unit_cost, remaining_qty FROM inventory.cost_layers WHERE sku_id = @s AND branch_id = @b AND remaining_qty > 0 ORDER BY layer_date, seq", c);
        cmd.Parameters.AddWithValue("s", skuId);
        cmd.Parameters.AddWithValue("b", branchId);
        var result = new List<(decimal, int)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            result.Add((r.GetDecimal(0), r.GetInt32(1)));
        }
        return result;
    }

    public static async Task<HttpClient> UserClientAsync(this ManokshaApiFactory factory, string role, Guid branchId, string client = "admin")
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(role, branchId);
        return factory.Authorized((await factory.LoginAsync(email, password, client)).AccessToken);
    }
}
