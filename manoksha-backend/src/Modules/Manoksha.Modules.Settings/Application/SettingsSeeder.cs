using Manoksha.Application.Modules;
using Manoksha.Modules.Settings.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Settings.Application;

/// <summary>Inserts missing settings with their SPEC defaults. Never overwrites Owner changes.</summary>
internal sealed class SettingsSeeder(ManokshaDbContext db, IClock clock) : IStartupSeeder
{
    public int Order => 20;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Set<SystemSetting>().Select(s => s.Key).ToListAsync(cancellationToken);
        foreach (var definition in SettingDefinitions.All.Where(d => !existing.Contains(d.Key)))
        {
            db.Add(new SystemSetting(definition.Key, definition.DefaultJson, clock.UtcNow));
            db.Add(new SystemSettingChange(definition.Key, null, definition.DefaultJson, 1, null, "Initial default (SPEC)", clock.UtcNow));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
