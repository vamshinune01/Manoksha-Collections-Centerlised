using System.Net;
using System.Net.Http.Json;
using Manoksha.Application.Modules;
using Manoksha.Application.Security;
using Manoksha.IntegrationTests.Infrastructure;

namespace Manoksha.IntegrationTests;

/// <summary>Employees, branch assignment and attendance (SPEC §25).</summary>
[Collection(ApiCollection.Name)]
public class EmployeeAttendanceTests(ManokshaApiFactory factory)
{
    private static readonly Guid Karimnagar = DevelopmentSeedData.BranchKarimnagar;
    private static readonly Guid Hyderabad = DevelopmentSeedData.BranchHyderabad;

    [Fact]
    public async Task Clock_in_uses_the_assigned_branch_not_the_client()
    {
        var (_, email, password, _) = await factory.CreateEmployeeAsync(SystemRoles.SalesEmployee, Karimnagar);
        var sales = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);

        // Even if a client tries to send a branch, the backend ignores it.
        var clockIn = await sales.PostAsJsonAsync("/api/v1/admin/me/attendance/clock-in", new { branchId = Hyderabad }).OkJsonAsync();
        clockIn["branchId"]!.GetValue<Guid>().Should().Be(Karimnagar);

        var again = await sales.PostAsync("/api/v1/admin/me/attendance/clock-in", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ErrorCodeAsync()).Should().Be("ALREADY_CLOCKED_IN");

        var mine = await sales.GetAsync("/api/v1/admin/me/attendance").OkJsonAsync();
        mine["isClockedIn"]!.GetValue<bool>().Should().BeTrue();

        var clockOut = await sales.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();
        clockOut["clockOutAt"].Should().NotBeNull();
        (await (await sales.PostAsync("/api/v1/admin/me/attendance/clock-out", null)).ErrorCodeAsync()).Should().Be("NOT_CLOCKED_IN");
    }

    [Fact]
    public async Task Concurrent_clock_ins_create_exactly_one_open_record()
    {
        var (_, email, password, _) = await factory.CreateEmployeeAsync(SystemRoles.InventoryEmployee, Karimnagar);
        var token = (await factory.LoginAsync(email, password, "pos")).AccessToken;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => factory.Authorized(token).PostAsync("/api/v1/pos/me/attendance/clock-in", null)));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(7);
        var mine = await factory.Authorized(token).GetAsync("/api/v1/pos/me/attendance").OkJsonAsync();
        mine["recent"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Users_without_an_employee_profile_cannot_clock_in()
    {
        var (_, email, password) = await factory.CreateInternalUserAsync(SystemRoles.SalesEmployee, Karimnagar);
        var user = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        var response = await user.PostAsync("/api/v1/admin/me/attendance/clock-in", null);
        (await response.ErrorCodeAsync()).Should().Be("NOT_AN_EMPLOYEE");
    }

    [Fact]
    public async Task Manager_corrects_team_attendance_but_never_their_own()
    {
        var (_, empEmail, empPassword, _) = await factory.CreateEmployeeAsync(SystemRoles.SalesEmployee, Karimnagar);
        var (_, mgrEmail, mgrPassword, _) = await factory.CreateEmployeeAsync(SystemRoles.BranchManager, Karimnagar);
        var (_, otherEmail, otherPassword, _) = await factory.CreateEmployeeAsync(SystemRoles.BranchManager, Hyderabad);
        var employee = factory.Authorized((await factory.LoginAsync(empEmail, empPassword)).AccessToken);
        var manager = factory.Authorized((await factory.LoginAsync(mgrEmail, mgrPassword)).AccessToken);
        var otherManager = factory.Authorized((await factory.LoginAsync(otherEmail, otherPassword)).AccessToken);

        var record = await employee.PostAsync("/api/v1/admin/me/attendance/clock-in", null).OkJsonAsync();
        await employee.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();
        var recordId = record["id"]!.GetValue<Guid>();
        var clockIn = DateTimeOffset.UtcNow.AddHours(-8);
        var clockOut = DateTimeOffset.UtcNow.AddHours(-1);

        var corrected = await manager.PostAsJsonAsync($"/api/v1/admin/attendance/{recordId}/correct",
            new { clockInAt = clockIn, clockOutAt = clockOut, reason = "Forgot to clock in at opening" }).OkJsonAsync();
        corrected["hoursWorked"]!.GetValue<double>().Should().BeApproximately(7, 0.01);
        corrected["correctionReason"]!.GetValue<string>().Should().Be("Forgot to clock in at opening");

        // Another branch's manager has no authority here.
        (await otherManager.PostAsJsonAsync($"/api/v1/admin/attendance/{recordId}/correct",
            new { clockInAt = clockIn, clockOutAt = clockOut, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Nobody corrects their own attendance.
        var own = await manager.PostAsync("/api/v1/admin/me/attendance/clock-in", null).OkJsonAsync();
        var self = await manager.PostAsJsonAsync($"/api/v1/admin/attendance/{own["id"]!.GetValue<Guid>()}/correct",
            new { clockInAt = clockIn, clockOutAt = clockOut, reason = "x" });
        (await self.ErrorCodeAsync()).Should().Be("SELF_CORRECTION_NOT_ALLOWED");
        await manager.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();

        var owner = await factory.OwnerClientAsync();
        var audit = await owner.GetAsync($"/api/v1/admin/audit?action=employees.attendance.corrected&entityId={recordId}").OkJsonAsync();
        audit["items"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Manager_sees_only_their_branch_attendance()
    {
        var (_, hydEmail, hydPassword, _) = await factory.CreateEmployeeAsync(SystemRoles.SalesEmployee, Hyderabad);
        var hydEmployee = factory.Authorized((await factory.LoginAsync(hydEmail, hydPassword)).AccessToken);
        await hydEmployee.PostAsync("/api/v1/admin/me/attendance/clock-in", null).OkJsonAsync();
        await hydEmployee.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();

        var (_, email, password, _) = await factory.CreateEmployeeAsync(SystemRoles.BranchManager, Karimnagar);
        var manager = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        var own = await manager.PostAsync("/api/v1/admin/me/attendance/clock-in", null).OkJsonAsync();
        await manager.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();

        var list = (await manager.GetAsync("/api/v1/admin/attendance").OkJsonAsync()).AsArray();
        list.Should().Contain(a => a!["id"]!.GetValue<Guid>() == own["id"]!.GetValue<Guid>());
        list.Should().OnlyContain(a => a!["branchId"]!.GetValue<Guid>() == Karimnagar);
    }

    [Fact]
    public async Task Employee_management_is_branch_scoped_and_never_grants_roles()
    {
        var owner = await factory.OwnerClientAsync();
        var code = "HR_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        (await owner.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            code, name = "Branch HR", scope = "Branch",
            permissions = new[] { Permissions.Employees.View, Permissions.Employees.Manage }, reason = "delegate hiring",
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        var (_, hrEmail, hrPassword) = await factory.CreateInternalUserAsync(code, Karimnagar);
        var hr = factory.Authorized((await factory.LoginAsync(hrEmail, hrPassword)).AccessToken);

        var email = $"e{Guid.NewGuid():N}"[..13] + "@test.manoksha";
        var created = await hr.PostAsJsonAsync("/api/v1/admin/employees",
            new { fullName = "New Hire", email, assignedBranchId = Karimnagar, reason = "new sales staff" }).OkJsonAsync(HttpStatusCode.Created);
        created["temporaryPassword"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        created["employee"]!["employeeCode"]!.GetValue<string>().Should().StartWith("EMP-");
        var newUserId = created["employee"]!["userId"]!.GetValue<Guid>();

        // No role was granted by creating the employee — roles remain Owner-only.
        var user = await owner.GetAsync($"/api/v1/admin/users/{newUserId}").OkJsonAsync();
        user["roles"]!.AsArray().Should().BeEmpty();

        // Cannot hire into another branch.
        var other = await hr.PostAsJsonAsync("/api/v1/admin/employees",
            new { fullName = "Elsewhere", email = "x" + email, assignedBranchId = Hyderabad, reason = "x" });
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sees only Karimnagar employees.
        var list = await hr.GetAsync("/api/v1/admin/employees").OkJsonAsync();
        list.AsArray().Should().OnlyContain(e => e!["assignedBranchId"]!.GetValue<Guid>() == Karimnagar);

        // A default Branch Manager does not have employee management unless the Owner grants it.
        var (_, mgrEmail, mgrPassword) = await factory.CreateInternalUserAsync(SystemRoles.BranchManager, Karimnagar);
        var manager = factory.Authorized((await factory.LoginAsync(mgrEmail, mgrPassword)).AccessToken);
        (await manager.PostAsJsonAsync("/api/v1/admin/employees",
            new { fullName = "x", email = "y" + email, assignedBranchId = Karimnagar, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reassignment_keeps_history_and_requires_clock_out()
    {
        var (_, email, password, employeeId) = await factory.CreateEmployeeAsync(SystemRoles.SalesEmployee, Karimnagar);
        var employee = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        var owner = await factory.OwnerClientAsync();

        await employee.PostAsync("/api/v1/admin/me/attendance/clock-in", null).OkJsonAsync();
        var blocked = await owner.PostAsJsonAsync($"/api/v1/admin/employees/{employeeId}/reassign", new { branchId = Hyderabad, reason = "transfer" });
        (await blocked.ErrorCodeAsync()).Should().Be("EMPLOYEE_CLOCKED_IN");
        await employee.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();

        var moved = await owner.PostAsJsonAsync($"/api/v1/admin/employees/{employeeId}/reassign", new { branchId = Hyderabad, reason = "transfer to Hyderabad" }).OkJsonAsync();
        moved["assignedBranchId"]!.GetValue<Guid>().Should().Be(Hyderabad);

        var history = await owner.GetAsync($"/api/v1/admin/employees/{employeeId}/branch-history").OkJsonAsync();
        history.AsArray().Should().HaveCount(2);
        history[0]!["branchId"]!.GetValue<Guid>().Should().Be(Hyderabad);
        history[1]!["toAt"].Should().NotBeNull();

        var next = await employee.PostAsync("/api/v1/admin/me/attendance/clock-in", null).OkJsonAsync();
        next["branchId"]!.GetValue<Guid>().Should().Be(Hyderabad);
        await employee.PostAsync("/api/v1/admin/me/attendance/clock-out", null).OkJsonAsync();
    }

    [Fact]
    public async Task Cannot_clock_in_at_an_inactive_branch()
    {
        var owner = await factory.OwnerClientAsync();
        var code = "C" + Random.Shared.Next(1000, 9999);
        var branchId = (await owner.PostAsJsonAsync("/api/v1/admin/branches", new { code, name = "Temp " + code, reason = "x" }).OkJsonAsync(HttpStatusCode.Created))["id"]!.GetValue<Guid>();
        var (_, email, password, _) = await factory.CreateEmployeeAsync(SystemRoles.SalesEmployee, branchId);
        await owner.PostAsJsonAsync($"/api/v1/admin/branches/{branchId}/status", new { isActive = false, reason = "closed" }).OkJsonAsync();

        var employee = factory.Authorized((await factory.LoginAsync(email, password)).AccessToken);
        (await (await employee.PostAsync("/api/v1/admin/me/attendance/clock-in", null)).ErrorCodeAsync()).Should().Be("BRANCH_INACTIVE");
    }
}
