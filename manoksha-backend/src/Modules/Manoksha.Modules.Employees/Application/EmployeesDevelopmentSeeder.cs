using Manoksha.Application.Modules;
using Manoksha.Modules.Employees.Domain;
using Manoksha.Modules.Identity.Contracts;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Employees.Application;

/// <summary>LOCAL/DEVELOPMENT ONLY: employee profiles for the seeded Karimnagar staff users.</summary>
internal sealed class EmployeesDevelopmentSeeder(ManokshaDbContext db, IInternalUserAccounts accounts, IClock clock) : IDevelopmentSeeder
{
    public int Order => 20;

    public async Task SeedAsync(DevelopmentSeedContext context, CancellationToken cancellationToken)
    {
        string[] emails = [DevelopmentSeedData.ManagerEmail, DevelopmentSeedData.SalesEmail, DevelopmentSeedData.InventoryEmail];
        foreach (var email in emails)
        {
            var user = await accounts.FindByEmailAsync(email, cancellationToken);
            if (user is null || await db.Set<Employee>().AnyAsync(e => e.UserId == user.UserId, cancellationToken))
            {
                continue;
            }
            var seq = await db.Database.SqlQuery<long>($"SELECT nextval('employees.employee_code_seq') AS \"Value\"").SingleAsync(cancellationToken);
            var employee = new Employee(user.UserId, $"EMP-{seq:D5}", user.DisplayName, null, DevelopmentSeedData.BranchKarimnagar, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), clock.UtcNow);
            db.Add(employee);
            db.Add(new EmployeeBranchAssignment(employee.Id, DevelopmentSeedData.BranchKarimnagar, clock.UtcNow, null, "Development seed"));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
