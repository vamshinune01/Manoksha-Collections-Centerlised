using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Application.Security;
using Manoksha.Modules.Settings.Domain;
using Manoksha.Persistence;
using Manoksha.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Settings.Application;

internal sealed record SettingDto(string Key, string Description, string Kind, JsonElement Value, int Version, DateTimeOffset UpdatedAt, Guid? UpdatedBy);

internal sealed record UpdateSettingRequest(JsonElement Value, int ExpectedVersion, string Reason);

internal sealed class SettingsService(
    ManokshaDbContext db,
    IUnitOfWork unitOfWork,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock) : ISettingsReader
{
    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Set<SystemSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, cancellationToken);
        return SettingDefinitions.All
            .Where(d => rows.ContainsKey(d.Key))
            .Select(d => ToDto(d, rows[d.Key]))
            .ToList();
    }

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var definition = SettingDefinitions.Find(key) ?? throw new InvalidOperationException($"Unknown setting '{key}'.");
        var row = await db.Set<SystemSetting>().AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        return JsonSerializer.Deserialize<T>(row?.ValueJson ?? definition.DefaultJson, JsonDefaults.Options)!;
    }

    public Task<SettingDto> UpdateAsync(string key, UpdateSettingRequest request, CancellationToken cancellationToken)
    {
        var definition = SettingDefinitions.Find(key) ?? throw new NotFoundException("SETTING_NOT_FOUND", $"Unknown setting '{key}'.");
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessRuleException("REASON_REQUIRED", "A reason is required to change a business setting.", 400);
        }
        var error = definition.Validate(request.Value);
        if (error is not null)
        {
            throw new BusinessRuleException("SETTING_VALUE_INVALID", error, 400);
        }

        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var setting = await db.Set<SystemSetting>()
                .FromSqlInterpolated($"SELECT *, xmin FROM settings.system_settings WHERE key = {key} FOR UPDATE")
                .SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException("SETTING_NOT_FOUND", $"Setting '{key}' is not initialised.");

            var old = setting.ValueJson;
            var newJson = request.Value.GetRawText();
            setting.Change(newJson, request.ExpectedVersion, currentUser.UserId, clock.UtcNow);
            db.Add(new SystemSettingChange(key, old, newJson, setting.Version, currentUser.UserId, request.Reason, clock.UtcNow));
            await audit.RecordAsync(new AuditRecord("settings.changed", "SystemSetting", key,
                Before: new { value = JsonDocument.Parse(old).RootElement },
                After: new { value = request.Value, version = setting.Version },
                Reason: request.Reason), ct);
            return ToDto(definition, setting);
        }, cancellationToken);
    }

    private static SettingDto ToDto(SettingDefinition d, SystemSetting s) =>
        new(d.Key, d.Description, d.Kind.ToString(), JsonDocument.Parse(s.ValueJson).RootElement.Clone(), s.Version, s.UpdatedAt, s.UpdatedBy);
}
