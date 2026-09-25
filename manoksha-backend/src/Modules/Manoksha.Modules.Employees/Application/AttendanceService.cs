using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Branches.Contracts;
using Manoksha.Modules.Employees.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using P = Manoksha.Application.Security.Permissions;

namespace Manoksha.Modules.Employees.Application;

/// <summary>
/// V1 attendance = authenticated user + assigned branch (SPEC §25). The client never chooses the branch; QR, device,
/// Wi-Fi and geofence checks are future options and intentionally absent.
/// </summary>
internal sealed class AttendanceService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IPermissionService permissions,
    IBranchDirectory branches,
    ICurrentUser currentUser,
    IAuditWriter audit,
    IClock clock)
{
    public async Task<MyAttendanceDto> GetMineAsync(CancellationToken ct)
    {
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == currentUser.UserId, ct);
        if (employee is null)
        {
            return new MyAttendanceDto(false, false, null, [], null, null);
        }
        var records = await db.Set<AttendanceRecord>().AsNoTracking().Where(a => a.EmployeeId == employee.Id)
            .OrderByDescending(a => a.ClockInAt).Take(30).ToListAsync(ct);
        var dtos = await ToDtosAsync(records, ct);
        var open = dtos.FirstOrDefault(d => d.ClockOutAt is null);
        var branch = await branches.FindAsync(employee.AssignedBranchId, ct);
        return new MyAttendanceDto(true, open is not null, open, dtos, employee.AssignedBranchId, branch?.Name);
    }

    public async Task<AttendanceDto> ClockInAsync(string source, CancellationToken ct)
    {
        await permissions.EnsureGlobalOrAnyAsync(P.Attendance.Self, ct);
        var employee = await LoadMyEmployeeAsync(ct);
        if (employee.Status != EmployeeStatus.Active)
        {
            throw new ForbiddenException("EMPLOYEE_INACTIVE", "Your employee profile is inactive.");
        }
        var branch = await branches.FindAsync(employee.AssignedBranchId, ct) ?? throw new NotFoundException("BRANCH_NOT_FOUND", "Branch not found.");
        if (!branch.IsActive)
        {
            throw new BusinessRuleException("BRANCH_INACTIVE", $"Your assigned branch ({branch.Name}) is inactive.");
        }

        var record = new AttendanceRecord(employee.Id, employee.AssignedBranchId, clock.UtcNow, source);
        db.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("ALREADY_CLOCKED_IN", "You are already clocked in. Clock out first.");
        }
        return (await ToDtosAsync([record], ct))[0];
    }

    public async Task<AttendanceDto> ClockOutAsync(string source, CancellationToken ct)
    {
        var employee = await LoadMyEmployeeAsync(ct);
        var open = await db.Set<AttendanceRecord>().SingleOrDefaultAsync(a => a.EmployeeId == employee.Id && a.ClockOutAt == null, ct)
            ?? throw new BusinessRuleException("NOT_CLOCKED_IN", "You are not clocked in.");
        open.ClockOut(clock.UtcNow, source);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([open], ct))[0];
    }

    public async Task<IReadOnlyList<AttendanceDto>> ListAsync(Guid? branchId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var access = await permissions.GetEffectiveAccessAsync(ct);
        var allowed = access.BranchesWith(P.Attendance.View);
        var q = db.Set<AttendanceRecord>().AsNoTracking();
        if (allowed is not null)
        {
            q = q.Where(a => allowed.Contains(a.BranchId));
        }
        if (branchId is { } b)
        {
            q = q.Where(a => a.BranchId == b);
        }
        var start = from ?? clock.UtcNow.AddDays(-7);
        q = q.Where(a => a.ClockInAt >= start);
        if (to is { } end)
        {
            q = q.Where(a => a.ClockInAt < end);
        }
        return await ToDtosAsync(await q.OrderByDescending(a => a.ClockInAt).Take(500).ToListAsync(ct), ct);
    }

    /// <summary>Authorized correction with reason and full audit. Nobody may correct their own attendance.</summary>
    public Task<AttendanceDto> CorrectAsync(Guid recordId, CorrectAttendanceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required to correct attendance.", 400);
        }
        return unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var record = await db.Set<AttendanceRecord>().SingleOrDefaultAsync(a => a.Id == recordId, innerCt)
                ?? throw new NotFoundException("ATTENDANCE_NOT_FOUND", "Attendance record not found.");
            await permissions.EnsurePermissionForBranchAsync(P.Attendance.Correct, record.BranchId, innerCt);
            var owner = await db.Set<Employee>().AsNoTracking().SingleAsync(e => e.Id == record.EmployeeId, innerCt);
            if (owner.UserId == currentUser.UserId)
            {
                throw new ForbiddenException("SELF_CORRECTION_NOT_ALLOWED", "You cannot correct your own attendance. Ask your manager.");
            }
            var before = new { record.ClockInAt, record.ClockOutAt };
            record.Correct(request.ClockInAt, request.ClockOutAt, currentUser.UserId, request.Reason, clock.UtcNow);
            await audit.RecordAsync(new AuditRecord("employees.attendance.corrected", "AttendanceRecord", recordId.ToString(),
                before, new { record.ClockInAt, record.ClockOutAt }, request.Reason, record.BranchId), innerCt);
            return (await ToDtosAsync([record], innerCt))[0];
        }, ct);
    }

    private async Task<Employee> LoadMyEmployeeAsync(CancellationToken ct) =>
        await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == currentUser.UserId, ct)
        ?? throw new ForbiddenException("NOT_AN_EMPLOYEE", "Your account has no employee profile. Contact your manager.");

    private async Task<IReadOnlyList<AttendanceDto>> ToDtosAsync(IReadOnlyList<AttendanceRecord> records, CancellationToken ct)
    {
        var employeeIds = records.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => employeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var names = (await branches.ListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);
        return records.Select(r => new AttendanceDto(
            r.Id, r.EmployeeId, employees[r.EmployeeId].FullName, employees[r.EmployeeId].EmployeeCode, r.BranchId, names.GetValueOrDefault(r.BranchId, "?"),
            r.ClockInAt, r.ClockOutAt, r.ClockOutAt is { } o ? Math.Round((o - r.ClockInAt).TotalHours, 2) : null,
            r.ClockInSource, r.ClockOutSource, r.CorrectedBy, r.CorrectionReason)).ToList();
    }
}

internal static class PermissionServiceExtensions
{
    public static async Task EnsureGlobalOrAnyAsync(this IPermissionService permissions, string permission, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(permission, ct))
        {
            throw new ForbiddenException(ErrorCodes.Forbidden, "You do not have permission to perform this action.");
        }
    }
}
