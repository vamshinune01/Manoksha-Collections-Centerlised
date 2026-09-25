using Manoksha.SharedKernel;

namespace Manoksha.Modules.Settings.Domain;

/// <summary>An Owner-managed business setting. Value is JSON; every change is versioned and audited.</summary>
internal sealed class SystemSetting
{
    private SystemSetting()
    {
    }

    public SystemSetting(string key, string valueJson, DateTimeOffset now)
    {
        Key = key;
        ValueJson = valueJson;
        Version = 1;
        UpdatedAt = now;
    }

    public string Key { get; private set; } = default!;

    public string ValueJson { get; private set; } = default!;

    public int Version { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public uint RowVersion { get; private set; }

    public void Change(string valueJson, int expectedVersion, Guid changedBy, DateTimeOffset now)
    {
        if (expectedVersion != Version)
        {
            throw new ConflictException(ErrorCodes.ConcurrencyConflict,
                $"Setting '{Key}' was changed by someone else (current version {Version}). Reload and try again.");
        }
        ValueJson = valueJson;
        Version++;
        UpdatedAt = now;
        UpdatedBy = changedBy;
    }
}

/// <summary>Append-only history of setting values.</summary>
internal sealed class SystemSettingChange : Entity
{
    private SystemSettingChange()
    {
    }

    public SystemSettingChange(string key, string? oldValueJson, string newValueJson, int newVersion, Guid? changedBy, string reason, DateTimeOffset changedAt)
    {
        Key = key;
        OldValueJson = oldValueJson;
        NewValueJson = newValueJson;
        NewVersion = newVersion;
        ChangedBy = changedBy;
        Reason = reason;
        ChangedAt = changedAt;
    }

    public string Key { get; private set; } = default!;

    public string? OldValueJson { get; private set; }

    public string NewValueJson { get; private set; } = default!;

    public int NewVersion { get; private set; }

    public Guid? ChangedBy { get; private set; }

    public string Reason { get; private set; } = default!;

    public DateTimeOffset ChangedAt { get; private set; }
}
