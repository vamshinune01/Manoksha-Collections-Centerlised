using Manoksha.SharedKernel;

namespace Manoksha.Modules.Employees.Domain;

internal enum EmployeeStatus
{
    Active = 1,
    Inactive = 2,
}

/// <summary>An employee profile linked 1:1 to an individual internal login (no shared credentials, SPEC §5.1).</summary>
internal sealed class Employee : Entity
{
    private Employee()
    {
    }

    public Employee(Guid userId, string employeeCode, string fullName, string? mobileE164, Guid assignedBranchId, DateOnly joinedOn, DateTimeOffset now)
    {
        UserId = userId;
        EmployeeCode = employeeCode;
        FullName = fullName.Trim();
        MobileE164 = mobileE164;
        AssignedBranchId = assignedBranchId;
        JoinedOn = joinedOn;
        Status = EmployeeStatus.Active;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public string EmployeeCode { get; private set; } = default!;

    public string FullName { get; private set; } = default!;

    public string? MobileE164 { get; private set; }

    /// <summary>The branch where this employee works; attendance is always recorded here (SPEC §25).</summary>
    public Guid AssignedBranchId { get; private set; }

    public DateOnly JoinedOn { get; private set; }

    public EmployeeStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint RowVersion { get; private set; }

    public void UpdateProfile(string fullName, string? mobileE164)
    {
        FullName = fullName.Trim();
        MobileE164 = mobileE164;
    }

    public void Reassign(Guid branchId) => AssignedBranchId = branchId;

    public void SetStatus(EmployeeStatus status) => Status = status;
}

/// <summary>History of branch assignments (who moved whom, when, why).</summary>
internal sealed class EmployeeBranchAssignment : Entity
{
    private EmployeeBranchAssignment()
    {
    }

    public EmployeeBranchAssignment(Guid employeeId, Guid branchId, DateTimeOffset from, Guid? assignedBy, string reason)
    {
        EmployeeId = employeeId;
        BranchId = branchId;
        FromAt = from;
        AssignedBy = assignedBy;
        Reason = reason;
    }

    public Guid EmployeeId { get; private set; }

    public Guid BranchId { get; private set; }

    public DateTimeOffset FromAt { get; private set; }

    public DateTimeOffset? ToAt { get; private set; }

    public Guid? AssignedBy { get; private set; }

    public string Reason { get; private set; } = default!;

    public void Close(DateTimeOffset at) => ToAt ??= at;
}

/// <summary>One clock-in/clock-out. The branch comes from the assignment, never from the client (SPEC §25).</summary>
internal sealed class AttendanceRecord : Entity
{
    private AttendanceRecord()
    {
    }

    public AttendanceRecord(Guid employeeId, Guid branchId, DateTimeOffset clockInAt, string source)
    {
        EmployeeId = employeeId;
        BranchId = branchId;
        ClockInAt = clockInAt;
        ClockInSource = source;
    }

    public Guid EmployeeId { get; private set; }

    public Guid BranchId { get; private set; }

    public DateTimeOffset ClockInAt { get; private set; }

    public DateTimeOffset? ClockOutAt { get; private set; }

    public string ClockInSource { get; private set; } = default!;

    public string? ClockOutSource { get; private set; }

    public Guid? CorrectedBy { get; private set; }

    public DateTimeOffset? CorrectedAt { get; private set; }

    public string? CorrectionReason { get; private set; }

    public uint RowVersion { get; private set; }

    public void ClockOut(DateTimeOffset at, string source)
    {
        if (ClockOutAt is not null)
        {
            throw new BusinessRuleException("NOT_CLOCKED_IN", "You are not clocked in.");
        }
        ClockOutAt = at;
        ClockOutSource = source;
    }

    public void Correct(DateTimeOffset clockIn, DateTimeOffset? clockOut, Guid correctedBy, string reason, DateTimeOffset now)
    {
        if (clockOut is { } o && o <= clockIn)
        {
            throw new BusinessRuleException("ATTENDANCE_TIMES_INVALID", "Clock-out must be after clock-in.", 400);
        }
        if (clockIn > now || clockOut > now)
        {
            throw new BusinessRuleException("ATTENDANCE_TIMES_INVALID", "Attendance times cannot be in the future.", 400);
        }
        ClockInAt = clockIn;
        ClockOutAt = clockOut;
        CorrectedBy = correctedBy;
        CorrectedAt = now;
        CorrectionReason = reason;
    }
}
