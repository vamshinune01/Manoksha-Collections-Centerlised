using Manoksha.Modules.Employees.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Employees.Persistence;

public sealed class EmployeesModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "employees";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("employee_code_seq", SchemaName);

        modelBuilder.Entity<Employee>(b =>
        {
            b.ToTable("employees", SchemaName);
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => x.EmployeeCode).IsUnique();
            b.HasIndex(x => x.AssignedBranchId);
            b.Property(x => x.EmployeeCode).HasMaxLength(20);
            b.Property(x => x.FullName).HasMaxLength(200);
            b.Property(x => x.MobileE164).HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<EmployeeBranchAssignment>(b =>
        {
            b.ToTable("employee_branch_assignments", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.EmployeeId, x.FromAt });
            b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceRecord>(b =>
        {
            b.ToTable("attendance_records", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.ClockInSource).HasMaxLength(20);
            b.Property(x => x.ClockOutSource).HasMaxLength(20);
            b.Property(x => x.CorrectionReason).HasMaxLength(2000);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.BranchId, x.ClockInAt });
            b.HasIndex(x => new { x.EmployeeId, x.ClockInAt });
            // At most one open attendance per employee — makes concurrent double clock-in impossible.
            b.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("clock_out_at IS NULL").HasDatabaseName("ux_attendance_one_open_per_employee");
            b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
