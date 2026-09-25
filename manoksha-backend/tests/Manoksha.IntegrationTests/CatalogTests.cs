using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>Products, configurable variants, SKUs and barcodes (SPEC §7).</summary>
[Collection(ApiCollection.Name)]
public class CatalogTests(ManokshaApiFactory factory)
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<(HttpClient Owner, Guid CategoryId)> SetupAsync()
    {
        var owner = await factory.OwnerClientAsync();
        var category = await owner.PostAsJsonAsync("/api/v1/admin/catalog/categories",
            new { name = "Sarees " + Unique(""), sortOrder = 1, isActive = true, reason = "setup" }).OkJsonAsync();
        return (owner, category["id"]!.GetValue<Guid>());
    }

    private static async Task<JsonNode> CreateAttributeAsync(HttpClient owner, string name, params string[] options) =>
        await owner.PostAsJsonAsync("/api/v1/admin/catalog/attributes", new { code = Unique(name.ToUpperInvariant() + "_"), name, options, reason = "setup" }).OkJsonAsync();

    private static Guid OptionId(JsonNode attribute, string value) =>
        attribute["options"]!.AsArray().Single(o => o!["value"]!.GetValue<string>() == value)!["id"]!.GetValue<Guid>();

    [Fact]
    public async Task Variants_use_configurable_attributes_and_combinations_are_unique()
    {
        var (owner, categoryId) = await SetupAsync();
        var colour = await CreateAttributeAsync(owner, "Colour", "Red", "Green");
        var design = await CreateAttributeAsync(owner, "Design", "Kanchi Border", "Plain");   // not size/colour — configurable

        var product = await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId, name = "Silk Saree", description = "Pure silk", trackingMode = "Serialized",
            variantAttributeIds = new[] { colour["id"]!.GetValue<Guid>(), design["id"]!.GetValue<Guid>() },
            availableForRetail = true, availableForReseller = false, reason = "new arrival",
        }).OkJsonAsync(HttpStatusCode.Created);
        var productId = product["id"]!.GetValue<Guid>();
        product["status"]!.GetValue<string>().Should().Be("Draft");

        var withVariant = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/variants", new
        {
            optionIds = new[] { OptionId(colour, "Red"), OptionId(design, "Kanchi Border") }, generateBarcode = true, reason = "add",
        }).OkJsonAsync();
        var variant = withVariant["variants"]![0]!;
        variant["name"]!.GetValue<string>().Should().Be("Red / Kanchi Border");
        variant["skuCode"]!.GetValue<string>().Should().MatchRegex("^MC-\\d{6}$");
        variant["barcodes"]!.AsArray().Should().ContainSingle();

        var duplicate = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/variants", new
        {
            optionIds = new[] { OptionId(design, "Kanchi Border"), OptionId(colour, "Red") }, generateBarcode = false, reason = "dup",
        });
        (await duplicate.ErrorCodeAsync()).Should().Be("VARIANT_ALREADY_EXISTS");

        var missing = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{productId}/variants", new
        {
            optionIds = new[] { OptionId(colour, "Green") }, generateBarcode = false, reason = "x",
        });
        (await missing.ErrorCodeAsync()).Should().Be("VARIANT_OPTIONS_MISMATCH");
    }

    [Fact]
    public async Task Product_without_variant_attributes_has_a_single_standard_variant()
    {
        var (owner, categoryId) = await SetupAsync();
        var product = await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId, name = "Kids Frock", trackingMode = "Quantity", availableForRetail = true, availableForReseller = true, reason = "x",
        }).OkJsonAsync(HttpStatusCode.Created);
        var id = product["id"]!.GetValue<Guid>();

        var first = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/variants", new { skuCode = Unique("FRK-"), generateBarcode = false, reason = "x" }).OkJsonAsync();
        first["variants"]![0]!["name"]!.GetValue<string>().Should().Be("Standard");
        var second = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/variants", new { generateBarcode = false, reason = "x" });
        (await second.ErrorCodeAsync()).Should().Be("VARIANT_ALREADY_EXISTS");
    }

    [Fact]
    public async Task Activation_requires_a_variant_and_locks_tracking_mode()
    {
        var (owner, categoryId) = await SetupAsync();
        var id = (await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId, name = "Necklace", trackingMode = "Serialized", availableForRetail = true, availableForReseller = true, reason = "x",
        }).OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();

        (await (await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/status", new { status = "Active", reason = "x" })).ErrorCodeAsync())
            .Should().Be("PRODUCT_HAS_NO_VARIANTS");

        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/variants", new { generateBarcode = false, reason = "x" }).OkJsonAsync();
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/status", new { status = "Active", reason = "launch" }).OkJsonAsync();

        var change = await owner.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", new
        {
            categoryId, name = "Necklace", trackingMode = "Quantity", availableForRetail = true, availableForReseller = true, reason = "x",
        });
        (await change.ErrorCodeAsync()).Should().Be("TRACKING_MODE_LOCKED");
    }

    [Fact]
    public async Task Retail_and_reseller_availability_changes_are_audited()
    {
        var (owner, categoryId) = await SetupAsync();
        var id = (await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId, name = "Bangles", trackingMode = "Quantity", availableForRetail = true, availableForReseller = true, reason = "x",
        }).OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();

        var updated = await owner.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", new
        {
            categoryId, name = "Bangles", trackingMode = "Quantity", availableForRetail = true, availableForReseller = false, reason = "store exclusive",
        }).OkJsonAsync();
        updated["availableForReseller"]!.GetValue<bool>().Should().BeFalse();

        var audit = await owner.GetAsync($"/api/v1/admin/audit?action=catalog.product.updated&entityId={id}").OkJsonAsync();
        var entry = audit["items"]!.AsArray().Single()!;
        entry["before"]!.GetValue<string>().Should().Contain("\"availableForReseller\": true");
        entry["after"]!.GetValue<string>().Should().Contain("\"availableForReseller\": false");
        entry["reason"]!.GetValue<string>().Should().Be("store exclusive");
    }

    [Fact]
    public async Task Sku_codes_are_unique()
    {
        var (owner, categoryId) = await SetupAsync();
        var sku = Unique("SKU-");
        foreach (var expected in new[] { HttpStatusCode.OK, HttpStatusCode.Conflict })
        {
            var id = (await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
            {
                categoryId, name = "Earrings", trackingMode = "Quantity", availableForRetail = true, availableForReseller = true, reason = "x",
            }).OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();
            var response = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/variants", new { skuCode = sku, generateBarcode = false, reason = "x" });
            response.StatusCode.Should().Be(expected);
        }
    }

    [Fact]
    public async Task Barcode_scan_reprint_and_retire()
    {
        var (owner, categoryId) = await SetupAsync();
        var colour = await CreateAttributeAsync(owner, "Colour", "Gold");
        var id = (await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId, name = "Temple Necklace", trackingMode = "Serialized", variantAttributeIds = new[] { colour["id"]!.GetValue<Guid>() },
            availableForRetail = true, availableForReseller = true, reason = "x",
        }).OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();
        var detail = await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/variants",
            new { optionIds = new[] { OptionId(colour, "Gold") }, generateBarcode = true, reason = "x" }).OkJsonAsync();
        var barcode = detail["variants"]![0]!["barcodes"]![0]!;
        var code = barcode["code"]!.GetValue<string>();
        var barcodeId = barcode["id"]!.GetValue<Guid>();

        code.Should().MatchRegex("^29\\d{11}$");
        CheckDigitOk(code).Should().BeTrue();

        // A sales employee scans from the POS app.
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, DevelopmentSeedData.BranchKarimnagar);
        var pos = factory.Authorized((await factory.LoginAsync(email, password, "pos")).AccessToken);
        var scan = await pos.GetAsync($"/api/v1/pos/scan/{code}").OkJsonAsync();
        scan["productName"]!.GetValue<string>().Should().Be("Temple Necklace");
        scan["variantName"]!.GetValue<string>().Should().Be("Gold");
        scan["attributes"]![0]!["value"]!.GetValue<string>().Should().Be("Gold");
        scan["trackingMode"]!.GetValue<string>().Should().Be("Serialized");
        (await pos.GetAsync("/api/v1/pos/scan/2900000000000")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Printing twice is a reprint of the same identity — no new barcode.
        var first = await owner.PostAsJsonAsync("/api/v1/admin/catalog/barcodes/print", new { barcodeIds = new[] { barcodeId }, copies = 2 }).OkJsonAsync();
        var second = await owner.PostAsJsonAsync("/api/v1/admin/catalog/barcodes/print", new { barcodeIds = new[] { barcodeId }, copies = 1 }).OkJsonAsync();
        first[0]!["isReprint"]!.GetValue<bool>().Should().BeFalse();
        second[0]!["isReprint"]!.GetValue<bool>().Should().BeTrue();
        second[0]!["code"]!.GetValue<string>().Should().Be(code);
        var after = await owner.GetAsync($"/api/v1/admin/catalog/products/{id}").OkJsonAsync();
        var barcodes = after["variants"]![0]!["barcodes"]!.AsArray();
        barcodes.Should().ContainSingle();
        barcodes[0]!["printCount"]!.GetValue<int>().Should().Be(2);

        // Retired codes cannot be scanned or printed, and are never reused.
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/barcodes/{barcodeId}/retire", new { reason = "label damaged batch" }).OkJsonAsync();
        (await pos.GetAsync($"/api/v1/pos/scan/{code}")).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await (await owner.PostAsJsonAsync("/api/v1/admin/catalog/barcodes/print", new { barcodeIds = new[] { barcodeId }, copies = 1 })).ErrorCodeAsync())
            .Should().Be("BARCODE_RETIRED");

        // Sales staff cannot manage the catalog.
        (await pos.PostAsJsonAsync("/api/v1/admin/catalog/products", new { categoryId, name = "x", trackingMode = "Quantity", reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task External_barcodes_are_validated_and_unique()
    {
        var (owner, categoryId) = await SetupAsync();
        var id = (await owner.PostAsJsonAsync("/api/v1/admin/catalog/products", new
        {
            categoryId, name = "Hair Clip", trackingMode = "Quantity", availableForRetail = true, availableForReseller = true, reason = "x",
        }).OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();
        var skuId = (await owner.PostAsJsonAsync($"/api/v1/admin/catalog/products/{id}/variants", new { generateBarcode = false, reason = "x" }).OkJsonAsync())
            ["variants"]![0]!["skuId"]!.GetValue<Guid>();

        var body = "890" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var valid = body + CheckDigit(body);
        var invalid = body + ((CheckDigit(body) - '0' + 1) % 10);

        (await (await owner.PostAsJsonAsync($"/api/v1/admin/catalog/skus/{skuId}/barcodes/external", new { code = invalid, reason = "supplier" })).ErrorCodeAsync())
            .Should().Be("BARCODE_CHECK_DIGIT_INVALID");
        (await (await owner.PostAsJsonAsync($"/api/v1/admin/catalog/skus/{skuId}/barcodes/external", new { code = "2900000000018", reason = "x" })).ErrorCodeAsync())
            .Should().Be("BARCODE_RESERVED_RANGE");
        await owner.PostAsJsonAsync($"/api/v1/admin/catalog/skus/{skuId}/barcodes/external", new { code = valid, reason = "supplier EAN" }).OkJsonAsync();
        (await owner.PostAsJsonAsync($"/api/v1/admin/catalog/skus/{skuId}/barcodes/external", new { code = valid, reason = "again" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Search finds the product by barcode.
        var page = await owner.GetAsync($"/api/v1/admin/catalog/products?q={valid}").OkJsonAsync();
        page["items"]!.AsArray().Should().ContainSingle(p => p!["id"]!.GetValue<Guid>() == id);
    }

    private static char CheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var d = body[body.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? d * 3 : d;
        }
        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    private static bool CheckDigitOk(string code) => CheckDigit(code[..^1]) == code[^1];
}
