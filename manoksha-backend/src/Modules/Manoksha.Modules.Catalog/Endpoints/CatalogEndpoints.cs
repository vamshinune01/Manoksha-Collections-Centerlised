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
        // Public storefront (SPEC §19.1): browsing is anonymous; only active categories are listed.
        endpoints.MapGet("/api/v1/catalog/categories", async (CatalogSetupService s, CancellationToken ct) =>
                (await s.ListCategoriesAsync(ct)).Where(c => c.IsActive).Select(c => new PublicCategoryDto(c.Id, c.ParentId, c.Name, c.Slug)).ToList())
            .AllowAnonymous().WithTags("Storefront").WithName("StorefrontCategories");

        var admin = endpoints.MapGroup("/api/v1/admin/catalog").WithTags("Catalog").RequireAudience(Audiences.Admin);
        var view = Permissions.Catalog.View;
        var manage = Permissions.Catalog.Manage;

        admin.MapGet("/categories", (CatalogSetupService s, CancellationToken ct) => s.ListCategoriesAsync(ct)).RequirePermission(view).WithName("ListCategories");
        admin.MapPost("/categories", (SaveCategoryRequest r, CatalogSetupService s, CancellationToken ct) => s.CreateCategoryAsync(r, ct)).RequirePermission(manage).WithName("CreateCategory");
        admin.MapPut("/categories/{id:guid}", (Guid id, SaveCategoryRequest r, CatalogSetupService s, CancellationToken ct) => s.UpdateCategoryAsync(id, r, ct)).RequirePermission(manage).WithName("UpdateCategory");

        // Product media (ADR-001 §29): upload URL → browser PUTs the original straight to storage → complete (optimize).
        admin.MapGet("/products/{productId:guid}/media", (Guid productId, ProductMediaService s, CancellationToken ct) => s.ListAsync(productId, ct))
            .RequirePermission(view).WithName("ListProductMedia");
        admin.MapPost("/products/{productId:guid}/media/uploads", (Guid productId, StartMediaUploadRequest r, ProductMediaService s, CancellationToken ct) =>
            s.StartUploadAsync(productId, r, ct)).RequirePermission(manage).WithName("StartProductMediaUpload");
        admin.MapPost("/products/{productId:guid}/media/{mediaId:guid}/complete", (Guid productId, Guid mediaId, ProductMediaService s, CancellationToken ct) =>
            s.CompleteAsync(productId, mediaId, ct)).RequirePermission(manage).WithName("CompleteProductMediaUpload");
        admin.MapPut("/products/{productId:guid}/media/{mediaId:guid}", (Guid productId, Guid mediaId, UpdateMediaRequest r, ProductMediaService s, CancellationToken ct) =>
            s.UpdateAsync(productId, mediaId, r, ct)).RequirePermission(manage).WithName("UpdateProductMedia");
        admin.MapPut("/products/{productId:guid}/media/order", (Guid productId, ReorderMediaRequest r, ProductMediaService s, CancellationToken ct) =>
            s.ReorderAsync(productId, r, ct)).RequirePermission(manage).WithName("ReorderProductMedia");
        admin.MapDelete("/products/{productId:guid}/media/{mediaId:guid}", async (Guid productId, Guid mediaId, ProductMediaService s, CancellationToken ct) =>
            {
                await s.DeleteAsync(productId, mediaId, ct);
                return Results.NoContent();
            }).RequirePermission(manage).WithName("DeleteProductMedia");

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

        admin.MapGet("/skus", (string? q, int? limit, ProductService s, CancellationToken ct) => s.SearchSkusAsync(q, limit, ct))
            .RequirePermission(view).WithName("SearchSkus");
    }
}
