using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Employees.Domain;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Employees.Application;

/// <summary>
/// Employee profiles. A manager with the (Owner-granted) employees.manage permission may manage employees of their own
/// branch. Creating an employee creates an individual login but never grants a role — roles stay Owner-only.
/// </summary>
internal sealed class EmployeeService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IPermissionService permissions,
    IBranchDirectory branches,
    IInternalUserAccounts accounts,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IClock clock)
{
    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(Guid? branchId, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var allowed = access.BranchesWith(P.Employees.View);
        var q = db.Set<Employee>().AsNoTracking();
        if (allowed is not null)
        {
            q = q.Where(e => allowed.Contains(e.AssignedBranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(e => e.AssignedBranchId == b);
        }
        return await ToDtosAsync(await q.OrderBy(e => e.FullName).ToListAsync(ct), ct);
    }

    public async Task<EmployeeDto> GetAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        await permissions.EnsurePermissionForBranchAsync(P.Employees.View, e.AssignedBranchId, ct);
        return (await ToDtosAsync([e], ct))[0];
    }

    public async Task<IReadOnlyList<BranchAssignmentDto>> GetAssignmentHistoryAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        await permissions.EnsurePermissionForBranchAsync(P.Employees.View, e.AssignedBranchId, ct);
        var names = (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return (await db.Set<EmployeeBranchAssignment>().AsNoTracking().Where(a => a.EmployeeId == id).OrderByDescending(a => a.FromAt).ToListAsync(ct))
            .Select(a => new BranchAssignmentDto(a.BranchId, names.GetValueOrDefault(a.BranchId, "?"), a.FromAt, a.ToAt, a.AssignedBy, a.Reason))
            .ToList();
    }

    public async Task<CreateEmployeeResponse> CreateAsync(CreateEmployeeRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new BusinessRuleException("EMPLOYEE_NAME_REQUIRED", "Full name is required.", 400);
        }
        if ((request.ExistingUserId is null) == string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BusinessRuleException("EMPLOYEE_ACCOUNT_REQUIRED", "Provide either a new work email or an existing internal user, not both.", 400);
        }
        await permissions.EnsurePermissionForBranchAsync(P.Employees.Manage, request.AssignedBranchId, ct);
        await EnsureActiveBranchAsync(request.AssignedBranchId, ct);
        var mobile = string.IsNullOrWhiteSpace(request.Mobile) ? null : MobileNumber.Normalize(request.Mobile);

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            Guid userId;
            string? temporaryPassword = null;
            if (request.ExistingUserId is { } existing)
            {
                var account = await accounts.FindAsync(existing, innerCt) ?? throw new NotFoundException("USER_NOT_FOUND", "Internal user not found.");
                userId = account.UserId;
            }
            else
            {
                var created = await accounts.CreateAsync(request.Email!, request.FullName, mobile, request.Reason, innerCt);
                userId = created.UserId;
                temporaryPassword = created.TemporaryPassword;
            }

            var seq = await db.Database.SqlQuery<long>($"SELECT nextval('employees.employee_code_seq') AS \"Value\"").SingleAsync(innerCt);
            var now = clock.UtcNow;
            var employee = new Employee(userId, $"EMP-{seq:D5}", request.FullName, mobile, request.AssignedBranchId,
                request.JoinedOn ?? DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(5.5)).DateTime), now);
            db.Add(employee);
            db.Add(new EmployeeBranchAssignment(employee.Id, request.AssignedBranchId, now, currentUser.UserIdOrNull, request.Reason));
            await audit.RecordAsync(new AuditRecord("employees.employee.created", "Employee", employee.Id.ToString(),
                After: new { employee.EmployeeCode, employee.FullName, employee.UserId, employee.AssignedBranchId }, Reason: request.Reason, BranchId: request.AssignedBranchId), innerCt);
            try
            {
                await db.SaveChangesAsync(innerCt);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                throw new ConflictException("EMPLOYEE_ALREADY_EXISTS", "This user already has an employee profile.");
            }
            return new CreateEmployeeResponse((await ToDtosAsync([employee], innerCt))[0], temporaryPassword);
        }, ct);
    }

    public Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        var mobile = string.IsNullOrWhiteSpace(request.Mobile) ? null : MobileNumber.Normalize(request.Mobile);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var e = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            await permissions.EnsurePermissionForBranchAsync(P.Employees.Manage, e.AssignedBranchId, innerCt);
            var before = new { e.FullName, e.MobileE164 };
            e.UpdateProfile(request.FullName, mobile);
            await audit.RecordAsync(new AuditRecord("employees.employee.updated", "Employee", id.ToString(), before, new { e.FullName, e.MobileE164 }, request.Reason, e.AssignedBranchId), innerCt);
            return (await ToDtosAsync([e], innerCt))[0];
        }, ct);
    }

    /// <summary>
    /// Moves an employee to another branch ("authorized reassignment", SPEC §25). Requires manage permission for both
    /// branches and no open attendance. Role assignments are separate and remain Owner-controlled.
    /// </summary>
    public Task<EmployeeDto> ReassignAsync(Guid id, ReassignBranchRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var e = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            if (e.AssignedBranchId == request.BranchId)
            {
                throw new BusinessRuleException("EMPLOYEE_ALREADY_AT_BRANCH", "The employee is already assigned to this branch.");
            }
            await permissions.EnsurePermissionForBranchAsync(P.Employees.Manage, e.AssignedBranchId, innerCt);
            await permissions.EnsurePermissionForBranchAsync(P.Employees.Manage, request.BranchId, innerCt);
            await EnsureActiveBranchAsync(request.BranchId, innerCt);
            if (await db.Set<AttendanceRecord>().AnyAsync(a => a.EmployeeId == id && a.ClockOutAt == null, innerCt))
            {
                throw new BusinessRuleException("EMPLOYEE_CLOCKED_IN", "The employee must clock out before being moved to another branch.");
            }

            var now = clock.UtcNow;
            var open = await db.Set<EmployeeBranchAssignment>().Where(a => a.EmployeeId == id && a.ToAt == null).ToListAsync(innerCt);
            open.ForEach(a => a.Close(now));
            var from = e.AssignedBranchId;
            e.Reassign(request.BranchId);
            db.Add(new EmployeeBranchAssignment(id, request.BranchId, now, currentUser.UserIdOrNull, request.Reason));
            await audit.RecordAsync(new AuditRecord("employees.employee.reassigned", "Employee", id.ToString(),
                new { branchId = from }, new { branchId = request.BranchId }, request.Reason, request.BranchId), innerCt);
            return (await ToDtosAsync([e], innerCt))[0];
        }, ct);
    }

    public Task<EmployeeDto> ChangeStatusAsync(Guid id, ChangeEmployeeStatusRequest request, CancellationToken ct)
    {
        RequireReason(request.Reason);
        if (!Enum.TryParse<EmployeeStatus>(request.Status, true, out var status))
        {
            throw new BusinessRuleException("STATUS_INVALID", "Status must be Active or Inactive.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var e = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == id, innerCt) ?? throw NotFound();
            await permissions.EnsurePermissionForBranchAsync(P.Employees.Manage, e.AssignedBranchId, innerCt);
            var before = e.Status;
            e.SetStatus(status);
            await audit.RecordAsync(new AuditRecord("employees.employee.status_changed", "Employee", id.ToString(),
                new { status = before.ToString() }, new { status = status.ToString() }, request.Reason, e.AssignedBranchId), innerCt);
            return (await ToDtosAsync([e], innerCt))[0];
        }, ct);
    }

    internal async Task<IReadOnlyList<EmployeeDto>> ToDtosAsync(IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var names = (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        var result = new List<EmployeeDto>(employees.Count);
        foreach (var e in employees)
        {
            var account = await accounts.FindAsync(e.UserId, ct);
            result.Add(new EmployeeDto(e.Id, e.UserId, e.EmployeeCode, e.FullName, e.MobileE164, account?.Email, e.AssignedBranchId,
                names.GetValueOrDefault(e.AssignedBranchId, "?"), e.Status.ToString(), e.JoinedOn, e.CreatedAt));
        }
        return result;
    }

    private async Task EnsureActiveBranchAsync(Guid branchId, CancellationToken ct)
    {
        var branch = await branches.FindAsync(branchId, ct) ?? throw new NotFoundException("BRANCH_NOT_FOUND", "Branch not found.");
        if (!branch.IsActive)
        {
            throw new BusinessRuleException("BRANCH_INACTIVE", $"Branch {branch.Name} is inactive.");
        }
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required for this action.", 400);
        }
    }

    private static NotFoundException NotFound() => new("EMPLOYEE_NOT_FOUND", "Employee not found.");
}
