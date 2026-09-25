using Manoksha.Modules.Identity.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Persistence;

public sealed class IdentityModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "identity";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.Email).HasMaxLength(254);
            b.Property(x => x.EmailNormalized).HasMaxLength(254);
            b.Property(x => x.MobileE164).HasMaxLength(20);
            b.Property(x => x.PasswordHash).HasMaxLength(500);
            b.Property(x => x.MfaSecretProtected).HasMaxLength(500);
            b.Property(x => x.MfaPendingSecretProtected).HasMaxLength(500);
            b.Property(x => x.SecurityStamp).HasMaxLength(64);
            b.Property(x => x.RowVersion).IsRowVersion();
            // Uniqueness is per account context: one mobile may be both a customer and a reseller (ADR-001 §2).
            b.HasIndex(x => new { x.AccountType, x.MobileE164 }).IsUnique().HasFilter("mobile_e164 IS NOT NULL");
            b.HasIndex(x => new { x.AccountType, x.EmailNormalized }).IsUnique().HasFilter("account_type = 'Internal' AND email_normalized IS NOT NULL");
        });

        modelBuilder.Entity<AuthSession>(b =>
        {
            b.ToTable("auth_sessions", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Audience).HasMaxLength(20);
            b.Property(x => x.RevokeReason).HasMaxLength(200);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => x.UserId);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenHash).HasMaxLength(64);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.SessionId);
            b.HasOne<AuthSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginEvent>(b =>
        {
            b.ToTable("login_events", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.AccountType).HasMaxLength(20);
            b.Property(x => x.IdentifierMasked).HasMaxLength(254);
            b.Property(x => x.Audience).HasMaxLength(20);
            b.Property(x => x.Result).HasMaxLength(40);
            b.Property(x => x.Reason).HasMaxLength(100);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => new { x.UserId, x.OccurredAt });
            b.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<OtpChallenge>(b =>
        {
            b.ToTable("otp_challenges", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.MobileE164).HasMaxLength(20);
            b.Property(x => x.Context).HasMaxLength(20);
            b.Property(x => x.CodeHash).HasMaxLength(64);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => new { x.MobileE164, x.Context, x.CreatedAt });
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.ToTable("permissions", SchemaName);
            b.HasKey(x => x.Code);
            b.Property(x => x.Code).HasMaxLength(100);
            b.Property(x => x.Module).HasMaxLength(50);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(50);
            b.Property(x => x.Name).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
            b.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.ToTable("role_permissions", SchemaName);
            b.HasKey(x => new { x.RoleId, x.PermissionCode });
            b.Property(x => x.PermissionCode).HasMaxLength(100);
            b.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionCode).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRoleAssignment>(b =>
        {
            b.ToTable("user_role_assignments", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.Property(x => x.RevokeReason).HasMaxLength(2000);
            b.Ignore(x => x.IsActive);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => new { x.UserId, x.RoleId, x.BranchId })
                .IsUnique()
                .HasFilter("revoked_at IS NULL")
                .AreNullsDistinct(false);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
