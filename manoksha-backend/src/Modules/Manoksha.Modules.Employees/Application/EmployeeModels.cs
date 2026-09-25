namespace Manoksha.Modules.Employees.Application;

public sealed record EmployeeDto(
    Guid Id,
    Guid UserId,
    string EmployeeCode,
    string FullName,
    string? Mobile,
    string? Email,
    Guid AssignedBranchId,
    string AssignedBranchName,
    string Status,
    DateOnly JoinedOn,
    DateTimeOffset CreatedAt);

public sealed record CreateEmployeeRequest(
    string FullName,
    string? Email,
    Guid? ExistingUserId,
    string? Mobile,
    Guid AssignedBranchId,
    DateOnly? JoinedOn,
    string Reason);

public sealed record CreateEmployeeResponse(EmployeeDto Employee, string? TemporaryPassword);

public sealed record UpdateEmployeeRequest(string FullName, string? Mobile, string Reason);

public sealed record ReassignBranchRequest(Guid BranchId, string Reason);

public sealed record ChangeEmployeeStatusRequest(string Status, string Reason);

public sealed record BranchAssignmentDto(Guid BranchId, string BranchName, DateTimeOffset FromAt, DateTimeOffset? ToAt, Guid? AssignedBy, string Reason);

public sealed record AttendanceDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    Guid BranchId,
    string BranchName,
    DateTimeOffset ClockInAt,
    DateTimeOffset? ClockOutAt,
    double? HoursWorked,
    string ClockInSource,
    string? ClockOutSource,
    Guid? CorrectedBy,
    string? CorrectionReason);

public sealed record MyAttendanceDto(bool IsEmployee, bool IsClockedIn, AttendanceDto? Open, IReadOnlyList<AttendanceDto> Recent, Guid? AssignedBranchId, string? AssignedBranchName);

public sealed record CorrectAttendanceRequest(DateTimeOffset ClockInAt, DateTimeOffset? ClockOutAt, string Reason);
