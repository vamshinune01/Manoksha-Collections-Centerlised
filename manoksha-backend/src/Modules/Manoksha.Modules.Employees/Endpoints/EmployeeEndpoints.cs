using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Employees.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Employees.Endpoints;

internal static class EmployeeEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Employees & Attendance").RequireAudience(Audiences.Admin);

        admin.MapGet("/employees", (Guid? branchId, EmployeeService s, CancellationToken ct) => s.ListAsync(branchId, ct))
            .RequirePermission(Permissions.Employees.View).WithName("ListEmployees");
        admin.MapGet("/employees/{id:guid}", (Guid id, EmployeeService s, CancellationToken ct) => s.GetAsync(id, ct))
            .RequirePermission(Permissions.Employees.View).WithName("GetEmployee");
        admin.MapGet("/employees/{id:guid}/branch-history", (Guid id, EmployeeService s, CancellationToken ct) => s.GetAssignmentHistoryAsync(id, ct))
            .RequirePermission(Permissions.Employees.View).WithName("GetEmployeeBranchHistory");
        admin.MapPost("/employees", async (CreateEmployeeRequest r, EmployeeService s, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(r, ct);
                return Results.Created($"/api/v1/admin/employees/{created.Employee.Id}", created);
            })
            .RequirePermission(Permissions.Employees.Manage).WithName("CreateEmployee");
        admin.MapPut("/employees/{id:guid}", (Guid id, UpdateEmployeeRequest r, EmployeeService s, CancellationToken ct) => s.UpdateAsync(id, r, ct))
            .RequirePermission(Permissions.Employees.Manage).WithName("UpdateEmployee");
        admin.MapPost("/employees/{id:guid}/reassign", (Guid id, ReassignBranchRequest r, EmployeeService s, CancellationToken ct) => s.ReassignAsync(id, r, ct))
            .RequirePermission(Permissions.Employees.Manage).WithName("ReassignEmployee");
        admin.MapPost("/employees/{id:guid}/status", (Guid id, ChangeEmployeeStatusRequest r, EmployeeService s, CancellationToken ct) => s.ChangeStatusAsync(id, r, ct))
            .RequirePermission(Permissions.Employees.Manage).WithName("ChangeEmployeeStatus");

        admin.MapGet("/attendance", (Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, AttendanceService s, CancellationToken ct) => s.ListAsync(branchId, from, to, ct))
            .RequirePermission(Permissions.Attendance.View).WithName("ListAttendance");
        admin.MapPost("/attendance/{id:guid}/correct", (Guid id, CorrectAttendanceRequest r, AttendanceService s, CancellationToken ct) => s.CorrectAsync(id, r, ct))
            .RequirePermission(Permissions.Attendance.Correct).WithName("CorrectAttendance");

        // Self-service attendance from the admin web and the POS app.
        foreach (var (prefix, audience) in new[] { ("/api/v1/admin/me/attendance", Audiences.Admin), ("/api/v1/pos/me/attendance", Audiences.Pos) })
        {
            var self = endpoints.MapGroup(prefix).WithTags("Employees & Attendance").RequireAudience(audience);
            self.MapGet("/", (AttendanceService s, CancellationToken ct) => s.GetMineAsync(ct)).WithName($"MyAttendance_{audience}");
            self.MapPost("/clock-in", (AttendanceService s, CancellationToken ct) => s.ClockInAsync(audience, ct))
                .RequirePermission(Permissions.Attendance.Self).WithName($"ClockIn_{audience}");
            self.MapPost("/clock-out", (AttendanceService s, CancellationToken ct) => s.ClockOutAsync(audience, ct))
                .WithName($"ClockOut_{audience}");
        }
    }
}
