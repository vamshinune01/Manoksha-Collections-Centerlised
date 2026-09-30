using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Reporting.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Reporting.Endpoints;

internal static class ReportingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Reporting").RequireAudience(Audiences.Admin);
        admin.MapGet("/dashboard", (Guid? branchId, ReportingService s, CancellationToken ct) => s.DashboardAsync(branchId, ct))
            .RequirePermission(Permissions.Reports.View).WithName("GetDashboard");
        admin.MapGet("/reports/sales", (DateOnly? from, DateOnly? to, Guid? branchId, string? groupBy, ReportingService s, CancellationToken ct) =>
                s.SalesReportAsync(from, to, branchId, groupBy, ct))
            .RequirePermission(Permissions.Reports.View).WithName("SalesReport");
        admin.MapGet("/reports/products", (DateOnly? from, DateOnly? to, Guid? branchId, ReportingService s, CancellationToken ct) =>
                s.ProductSalesAsync(from, to, branchId, ct))
            .RequirePermission(Permissions.Reports.View).WithName("ProductSalesReport");
        admin.MapGet("/reports/low-stock", (Guid? branchId, ReportingService s, CancellationToken ct) => s.LowStockReportAsync(branchId, ct))
            .RequirePermission(Permissions.Reports.View).WithName("LowStockReport");
        admin.MapGet("/reports/inventory-valuation", (Guid? branchId, ReportingService s, CancellationToken ct) => s.InventoryValuationAsync(branchId, ct))
            .RequirePermission(Permissions.Reports.Global).WithName("InventoryValuationReport");
        admin.MapGet("/reports/resellers", (DateOnly? from, DateOnly? to, ReportingService s, CancellationToken ct) => s.ResellerReportAsync(from, to, ct))
            .RequirePermission(Permissions.Reports.Global).WithName("ResellerReport");
        admin.MapGet("/exceptions", (string? type, ExceptionCenterService s, CancellationToken ct) => s.ListAsync(type, ct))
            .RequirePermission(Permissions.Exceptions.View).WithName("ExceptionCenter");

        endpoints.MapGroup("/api/v1/reseller").WithTags("Reseller portal").RequireAudience(Audiences.Reseller)
            .MapGet("/dashboard", (ResellerDashboardService s, CancellationToken ct) => s.GetAsync(ct)).WithName("ResellerDashboard");
    }
}
