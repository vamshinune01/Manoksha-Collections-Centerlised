using Manoksha.Application.Security;

namespace Manoksha.Modules.Identity.Application;

public sealed record CreateInternalUserRequest(string Email, string DisplayName, string? Mobile, string Reason);

public sealed record CreateInternalUserResponse(Guid UserId, string TemporaryPassword);

public sealed record ChangeUserStatusRequest(string Status, string Reason);

public sealed record AssignRoleRequest(Guid RoleId, Guid? BranchId, string Reason);

public sealed record ReasonRequest(string Reason);

public sealed record UserSummaryDto(
    Guid Id,
    string DisplayName,
    string? Email,
    string? Mobile,
    string Status,
    bool MfaEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<RoleAssignmentDto> Roles);

public sealed record RoleAssignmentDto(Guid AssignmentId, Guid RoleId, string RoleCode, string RoleName, Guid? BranchId, DateTimeOffset AssignedAt);

public sealed record CreateRoleRequest(string Code, string Name, string? Description, RoleScope Scope, IReadOnlyList<string> Permissions, string Reason);

public sealed record UpdateRoleRequest(string Name, string? Description, bool IsActive, string Reason);

public sealed record SetRolePermissionsRequest(IReadOnlyList<string> Permissions, string Reason);

public sealed record RoleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string Scope,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> Permissions);

public sealed record PermissionDto(string Code, string Module, bool OwnerOnly);
