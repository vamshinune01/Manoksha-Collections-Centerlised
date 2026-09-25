using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Identity.Endpoints;

internal static class AdminIdentityEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Users & Roles").RequireAudience(Audiences.Admin);

        admin.MapGet("/users", (UserAdministrationService s, CancellationToken ct) => s.ListInternalUsersAsync(ct))
            .RequirePermission(Permissions.Identity.UsersView).WithName("ListInternalUsers");

        admin.MapGet("/users/{userId:guid}", (Guid userId, UserAdministrationService s, CancellationToken ct) => s.GetAsync(userId, ct))
            .RequirePermission(Permissions.Identity.UsersView).WithName("GetInternalUser");

        admin.MapPost("/users", async (CreateInternalUserRequest request, UserAdministrationService s, CancellationToken ct) =>
            {
                var created = await s.CreateInternalUserAsync(request, ct);
                return Results.Created($"/api/v1/admin/users/{created.UserId}", created);
            })
            .RequirePermission(Permissions.Identity.UsersManage).WithName("CreateInternalUser");

        admin.MapPost("/users/{userId:guid}/status", async (Guid userId, ChangeUserStatusRequest request, UserAdministrationService s, CancellationToken ct) =>
            {
                await s.ChangeStatusAsync(userId, request, ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.Identity.UsersManage).WithName("ChangeInternalUserStatus");

        admin.MapPost("/users/{userId:guid}/role-assignments", (Guid userId, AssignRoleRequest request, UserAdministrationService s, CancellationToken ct) =>
                s.AssignRoleAsync(userId, request, ct))
            .RequirePermission(Permissions.Identity.UserRolesAssign).WithName("AssignRole");

        admin.MapPost("/users/{userId:guid}/role-assignments/{assignmentId:guid}/revoke",
                async (Guid userId, Guid assignmentId, ReasonRequest request, UserAdministrationService s, CancellationToken ct) =>
                {
                    await s.RevokeRoleAsync(userId, assignmentId, request, ct);
                    return Results.NoContent();
                })
            .RequirePermission(Permissions.Identity.UserRolesAssign).WithName("RevokeRole");

        admin.MapPost("/users/{userId:guid}/sessions/revoke", async (Guid userId, ReasonRequest request, UserAdministrationService s, CancellationToken ct) =>
                Results.Ok(new { revokedSessions = await s.RevokeSessionsAsync(userId, request, ct) }))
            .RequirePermission(Permissions.Identity.SessionsRevoke).WithName("RevokeUserSessions");

        admin.MapGet("/roles", (RoleAdministrationService s, CancellationToken ct) => s.ListAsync(ct))
            .RequirePermission(Permissions.Identity.RolesView).WithName("ListRoles");

        admin.MapGet("/roles/{roleId:guid}", (Guid roleId, RoleAdministrationService s, CancellationToken ct) => s.GetAsync(roleId, ct))
            .RequirePermission(Permissions.Identity.RolesView).WithName("GetRole");

        admin.MapPost("/roles", async (CreateRoleRequest request, RoleAdministrationService s, CancellationToken ct) =>
            {
                var role = await s.CreateAsync(request, ct);
                return Results.Created($"/api/v1/admin/roles/{role.Id}", role);
            })
            .RequirePermission(Permissions.Identity.RolesManage).WithName("CreateRole");

        admin.MapPatch("/roles/{roleId:guid}", (Guid roleId, UpdateRoleRequest request, RoleAdministrationService s, CancellationToken ct) =>
                s.UpdateAsync(roleId, request, ct))
            .RequirePermission(Permissions.Identity.RolesManage).WithName("UpdateRole");

        admin.MapPut("/roles/{roleId:guid}/permissions", (Guid roleId, SetRolePermissionsRequest request, RoleAdministrationService s, CancellationToken ct) =>
                s.SetPermissionsAsync(roleId, request, ct))
            .RequirePermission(Permissions.Identity.RolesManage).WithName("SetRolePermissions");

        admin.MapGet("/permissions", () => RoleAdministrationService.ListPermissions())
            .RequirePermission(Permissions.Identity.RolesView).WithName("ListPermissions");
    }
}
