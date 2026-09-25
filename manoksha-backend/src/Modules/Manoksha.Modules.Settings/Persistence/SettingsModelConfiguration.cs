using Manoksha.Modules.Settings.Domain;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Settings.Persistence;

public sealed class SettingsModelConfiguration : IModuleModelConfiguration
{
    public const string SchemaName = "settings";

    public string Schema => SchemaName;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>(b =>
        {
            b.ToTable("system_settings", SchemaName);
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(100);
            b.Property(x => x.ValueJson).HasColumnName("value").HasColumnType("jsonb").Unbounded();
            b.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<SystemSettingChange>(b =>
        {
            b.ToTable("system_setting_changes", SchemaName);
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(100);
            b.Property(x => x.OldValueJson).HasColumnName("old_value").HasColumnType("jsonb").Unbounded();
            b.Property(x => x.NewValueJson).HasColumnName("new_value").HasColumnType("jsonb").Unbounded();
            b.Property(x => x.Reason).HasMaxLength(2000);
            b.HasIndex(x => new { x.Key, x.ChangedAt });
        });
    }
}
