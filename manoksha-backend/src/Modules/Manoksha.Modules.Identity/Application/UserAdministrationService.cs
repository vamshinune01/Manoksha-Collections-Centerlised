using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Domain;
using Manoksha.Modules.Identity.Infrastructure;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Manoksha.Modules.Identity.Application;

public sealed record InternalUserCreated(Guid UserId, string Email) : IIntegrationEvent
{
    public static string EventType => "identity.internal_user_created";
}

public sealed record UserAccessChanged(Guid UserId, string Change) : IIntegrationEvent
{
    public static string EventType => "identity.user_access_changed";
}

/// <summary>
/// Internal user administration. Role assignment and every role/permission change is Owner-only (SPEC §26)
/// — enforced by owner-only permissions at the endpoint and re-checked here.
/// </summary>
internal sealed class UserAdministrationService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    PasswordService passwords,
    SessionService sessions,
    IPermissionService permissions,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IOutbox outbox,
    IClock clock)
{
    public async Task<IReadOnlyList<UserSummaryDto>> ListInternalUsersAsync(CancellationToken ct)
    {
        var users = await db.Set<User>().AsNoTracking()
            .Where(u => u.AccountType == AccountType.Internal)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);
        var roles = await LoadAssignmentsAsync(users.Select(u => u.Id).ToList(), ct);
        return users.Select(u => ToDto(u, roles)).ToList();
    }

    public async Task<UserSummaryDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.AccountType == AccountType.Internal, ct)
            ?? throw UserNotFound();
        return ToDto(user, await LoadAssignmentsAsync([userId], ct));
    }

    public Task<CreateInternalUserResponse> CreateInternalUserAsync(CreateInternalUserRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        if (!EmailAddress.IsValid(request.Email))
        {
            throw new BusinessRuleException("EMAIL_INVALID", "Enter a valid email address.", 400);
        }
        string? mobile = string.IsNullOrWhiteSpace(request.Mobile) ? null : MobileNumber.Normalize(request.Mobile);

        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var user = User.CreateInternal(request.Email, request.DisplayName, mobile, clock.UtcNow, currentUser.UserId);
            var temporary = PasswordService.GenerateTemporaryPassword();
            user.SetPassword(passwords.Hash(user, temporary), mustChange: true);
            db.Add(user);
            await audit.RecordAsync(new AuditRecord("identity.internal_user.created", "User", user.Id.ToString(),
                After: new { user.Id, user.Email, user.DisplayName, mobile = mobile is null ? null : MobileNumber.Mask(mobile) },
                Reason: request.Reason), innerCt);
            outbox.Enqueue(new InternalUserCreated(user.Id, user.Email!));
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("EMAIL_ALREADY_EXISTS", "An internal user with this email already exists.");
            }
            return new CreateInternalUserResponse(user.Id, temporary);
        }, ct);
    }

    public Task ChangeStatusAsync(Guid userId, ChangeUserStatusRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        if (!Enum.TryParse<UserStatus>(request.Status, ignoreCase: true, out var status) || status == UserStatus.Pending)
        {
            throw new BusinessRuleException("STATUS_INVALID", "Status must be Active, Locked or Disabled.", 400);
        }

        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var user = await LoadInternalForUpdateAsync(userId, innerCt);
            if (user.Id == currentUser.UserId && status != UserStatus.Active)
            {
                throw new BusinessRuleException("CANNOT_DEACTIVATE_SELF", "You cannot lock or disable your own account.");
            }
            if (status != UserStatus.Active && await IsLastActiveOwnerAsync(user.Id, innerCt))
            {
                throw new BusinessRuleException("LAST_OWNER", "The last active Owner account cannot be locked or disabled.");
            }
            var before = user.Status;
            user.ChangeStatus(status);
            if (status != UserStatus.Active)
            {
                await sessions.RevokeAllAsync(user.Id, "USER_" + status.ToString().ToUpperInvariant(), innerCt);
            }
            await audit.RecordAsync(new AuditRecord("identity.user.status_changed", "User", user.Id.ToString(),
                Before: new { status = before.ToString() }, After: new { status = status.ToString() }, Reason: request.Reason), innerCt);
            outbox.Enqueue(new UserAccessChanged(user.Id, "status"));
        }, ct);
    }

    public Task<RoleAssignmentDto> AssignRoleAsync(Guid userId, AssignRoleRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureOwnerAsync(innerCt);
            var user = await LoadInternalForUpdateAsync(userId, innerCt);
            var role = await db.Set<Role>().SingleOrDefaultAsync(r => r.Id == request.RoleId, innerCt)
                ?? throw new NotFoundException("ROLE_NOT_FOUND", "Role not found.");
            if (!role.IsActive)
            {
                throw new BusinessRuleException("ROLE_INACTIVE", "This role is inactive.");
            }
            switch (role.Scope)
            {
                case RoleScope.Global when request.BranchId is not null:
                    throw new BusinessRuleException("ROLE_SCOPE_MISMATCH", $"Role '{role.Name}' applies to all branches; do not specify a branch.", 400);
                case RoleScope.Branch when request.BranchId is null:
                    throw new BusinessRuleException("ROLE_SCOPE_MISMATCH", $"Role '{role.Name}' is branch-scoped; a branch is required.", 400);
            }

            var assignment = new UserRoleAssignment(user.Id, role.Id, request.BranchId, currentUser.UserId, request.Reason, clock.UtcNow);
            db.Add(assignment);
            user.RotateSecurityStamp();
            await audit.RecordAsync(new AuditRecord("identity.role_assignment.granted", "User", user.Id.ToString(),
                After: new { assignmentId = assignment.Id, roleId = role.Id, roleCode = role.Code, branchId = request.BranchId },
                Reason: request.Reason, BranchId: request.BranchId), innerCt);
            outbox.Enqueue(new UserAccessChanged(user.Id, "role_granted"));
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("ROLE_ALREADY_ASSIGNED", "The user already has this role for this scope.");
            }
            return new RoleAssignmentDto(assignment.Id, role.Id, role.Code, role.Name, assignment.BranchId, assignment.AssignedAt);
        }, ct);
    }

    public Task RevokeRoleAsync(Guid userId, Guid assignmentId, ReasonRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await EnsureOwnerAsync(innerCt);
            var user = await LoadInternalForUpdateAsync(userId, innerCt);
            var assignment = await db.Set<UserRoleAssignment>().SingleOrDefaultAsync(a => a.Id == assignmentId && a.UserId == userId, innerCt)
                ?? throw new NotFoundException("ROLE_ASSIGNMENT_NOT_FOUND", "Role assignment not found.");
            var role = await db.Set<Role>().SingleAsync(r => r.Id == assignment.RoleId, innerCt);
            if (role.IsOwnerRole && await IsLastActiveOwnerAsync(userId, innerCt))
            {
                throw new BusinessRuleException("LAST_OWNER", "The last active Owner cannot lose the Owner role.");
            }
            assignment.Revoke(currentUser.UserId, request.Reason, clock.UtcNow);
            user.RotateSecurityStamp();
            await audit.RecordAsync(new AuditRecord("identity.role_assignment.revoked", "User", user.Id.ToString(),
                Before: new { assignmentId, roleId = role.Id, roleCode = role.Code, branchId = assignment.BranchId },
                Reason: request.Reason, BranchId: assignment.BranchId), innerCt);
            outbox.Enqueue(new UserAccessChanged(user.Id, "role_revoked"));
        }, ct);
    }

    public Task<int> RevokeSessionsAsync(Guid userId, ReasonRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var user = await LoadInternalForUpdateAsync(userId, innerCt);
            var count = await sessions.RevokeAllAsync(user.Id, "ADMIN_REVOKED", innerCt);
            user.RotateSecurityStamp();
            await audit.RecordAsync(new AuditRecord("identity.sessions.revoked", "User", user.Id.ToString(),
                After: new { revokedSessions = count }, Reason: request.Reason), innerCt);
            return count;
        }, ct);
    }

    private async Task EnsureOwnerAsync(CancellationToken ct)
    {
        // Defence in depth: the endpoint already requires an owner-only permission.
        if (!(await permissions.GetEffectiveAccessAsync(ct)).IsOwner)
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "Only the Owner can change roles and permissions.");
        }
    }

    private async Task<User> LoadInternalForUpdateAsync(Guid userId, CancellationToken ct) =>
        await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId && u.AccountType == AccountType.Internal, ct)
        ?? throw UserNotFound();

    private async Task<bool> IsLastActiveOwnerAsync(Guid userId, CancellationToken ct)
    {
        var owners = await (
            from a in db.Set<UserRoleAssignment>()
            join r in db.Set<Role>() on a.RoleId equals r.Id
            join u in db.Set<User>() on a.UserId equals u.Id
            where r.Code == SystemRoles.Owner && a.RevokedAt == null && u.Status == UserStatus.Active
            select a.UserId).Distinct().ToListAsync(ct);
        return owners.Count == 1 && owners[0] == userId;
    }

    private async Task<Dictionary<Guid, List<RoleAssignmentDto>>> LoadAssignmentsAsync(List<Guid> userIds, CancellationToken ct)
    {
        var rows = await (
            from a in db.Set<UserRoleAssignment>()
            join r in db.Set<Role>() on a.RoleId equals r.Id
            where userIds.Contains(a.UserId) && a.RevokedAt == null
            orderby a.AssignedAt
            select new { a.UserId, Dto = new RoleAssignmentDto(a.Id, r.Id, r.Code, r.Name, a.BranchId, a.AssignedAt) })
            .AsNoTracking()
            .ToListAsync(ct);
        return rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList());
    }

    private static UserSummaryDto ToDto(User u, Dictionary<Guid, List<RoleAssignmentDto>> roles) =>
        new(u.Id, u.DisplayName, u.Email, u.MobileE164, u.Status.ToString(), u.MfaEnabled, u.CreatedAt, u.LastLoginAt,
            roles.TryGetValue(u.Id, out var list) ? list : []);

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    private static NotFoundException UserNotFound() => new("USER_NOT_FOUND", "User not found.");
}
