using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Catalog.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Catalog.Endpoints;

internal static class CatalogEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/catalog").WithTags("Catalog").RequireAudience(Audiences.Admin);
        var view = Permissions.Catalog.View;
        var manage = Permissions.Catalog.Manage;

        admin.MapGet("/categories", (CatalogSetupService s, CancellationToken ct) => s.ListCategoriesAsync(ct)).RequirePermission(view).WithName("ListCategories");
        admin.MapPost("/categories", (SaveCategoryRequest r, CatalogSetupService s, CancellationToken ct) => s.CreateCategoryAsync(r, ct)).RequirePermission(manage).WithName("CreateCategory");
        admin.MapPut("/categories/{id:guid}", (Guid id, SaveCategoryRequest r, CatalogSetupService s, CancellationToken ct) => s.UpdateCategoryAsync(id, r, ct)).RequirePermission(manage).WithName("UpdateCategory");

        admin.MapGet("/attributes", (CatalogSetupService s, CancellationToken ct) => s.ListAttributesAsync(ct)).RequirePermission(view).WithName("ListAttributes");
        admin.MapPost("/attributes", (CreateAttributeRequest r, CatalogSetupService s, CancellationToken ct) => s.CreateAttributeAsync(r, ct)).RequirePermission(manage).WithName("CreateAttribute");
        admin.MapPut("/attributes/{id:guid}", (Guid id, UpdateAttributeRequest r, CatalogSetupService s, CancellationToken ct) => s.UpdateAttributeAsync(id, r, ct)).RequirePermission(manage).WithName("UpdateAttribute");
        admin.MapPost("/attributes/{id:guid}/options", (Guid id, AddOptionRequest r, CatalogSetupService s, CancellationToken ct) => s.AddOptionAsync(id, r, ct)).RequirePermission(manage).WithName("AddAttributeOption");
        admin.MapPost("/attributes/{id:guid}/options/{optionId:guid}/status", (Guid id, Guid optionId, SetOptionStatusRequest r, CatalogSetupService s, CancellationToken ct) =>
            s.SetOptionStatusAsync(id, optionId, r, ct)).RequirePermission(manage).WithName("SetAttributeOptionStatus");

        admin.MapGet("/products", (string? q, Guid? categoryId, string? status, int? page, int? pageSize, ProductService s, CancellationToken ct) =>
            s.SearchAsync(q, categoryId, status, page, pageSize, ct)).RequirePermission(view).WithName("SearchProducts");
        admin.MapGet("/products/{id:guid}", (Guid id, ProductService s, CancellationToken ct) => s.GetAsync(id, ct)).RequirePermission(view).WithName("GetProduct");
        admin.MapPost("/products", async (CreateProductRequest r, ProductService s, CancellationToken ct) =>
        {
            var p = await s.CreateAsync(r, ct);
            return Results.Created($"/api/v1/admin/catalog/products/{p.Id}", p);
        }).RequirePermission(manage).WithName("CreateProduct");
        admin.MapPut("/products/{id:guid}", (Guid id, UpdateProductRequest r, ProductService s, CancellationToken ct) => s.UpdateAsync(id, r, ct)).RequirePermission(manage).WithName("UpdateProduct");
        admin.MapPost("/products/{id:guid}/status", (Guid id, ChangeStatusRequest r, ProductService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct)).RequirePermission(manage).WithName("ChangeProductStatus");
        admin.MapPost("/products/{id:guid}/variants", (Guid id, CreateVariantRequest r, ProductService s, BarcodeService b, CancellationToken ct) =>
            s.AddVariantAsync(id, r, b, ct)).RequirePermission(manage).WithName("AddVariant");
        admin.MapPost("/variants/{id:guid}/status", (Guid id, ChangeStatusRequest r, ProductService s, CancellationToken ct) => s.SetVariantStatusAsync(id, r, ct)).RequirePermission(manage).WithName("ChangeVariantStatus");

        admin.MapPost("/skus/{skuId:guid}/barcodes", (Guid skuId, GenerateBarcodeRequest r, BarcodeService s, CancellationToken ct) =>
            s.GenerateInternalAsync(skuId, r.Reason, ct)).RequirePermission(manage).WithName("GenerateBarcode");
        admin.MapPost("/skus/{skuId:guid}/barcodes/external", (Guid skuId, RegisterBarcodeRequest r, BarcodeService s, CancellationToken ct) =>
            s.RegisterExternalAsync(skuId, r, ct)).RequirePermission(manage).WithName("RegisterExternalBarcode");
        admin.MapPost("/barcodes/{id:guid}/retire", (Guid id, RetireBarcodeRequest r, BarcodeService s, CancellationToken ct) => s.RetireAsync(id, r, ct)).RequirePermission(manage).WithName("RetireBarcode");
        admin.MapPost("/barcodes/print", (PrintLabelsRequest r, BarcodeService s, CancellationToken ct) => s.PrintAsync(r, ct))
            .RequirePermission(Permissions.Catalog.BarcodesPrint).WithName("PrintBarcodeLabels");
        admin.MapGet("/barcodes/lookup/{code}", (string code, BarcodeService s, CancellationToken ct) => s.LookupAsync(code, ct)).RequirePermission(view).WithName("LookupBarcode");

        // POS / mobile scanning.
        endpoints.MapGet("/api/v1/pos/scan/{code}", (string code, BarcodeService s, CancellationToken ct) => s.LookupAsync(code, ct))
            .WithTags("POS").RequireAudience(Audiences.Pos).RequirePermission(view).WithName("PosScan");
    }
}
