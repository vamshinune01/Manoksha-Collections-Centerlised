using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.SharedKernel;

namespace Manoksha.Modules.Settings.Application;

internal enum SettingValueKind
{
    Integer,
    Money,
    String,
}

internal sealed record SettingDefinition(string Key, string Description, SettingValueKind Kind, string DefaultJson, Func<JsonElement, string?> Validate);

/// <summary>
/// Known business settings. Values the SPEC fixes are seeded with the SPEC value; they are stored as
/// audited settings so the rule is data, not code (design §21). Later phases add their thresholds here.
/// </summary>
internal static class SettingDefinitions
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new(SettingKeys.ReservationMinutes,
            "Inventory reservation / online payment window in minutes. Applies to new reservations only (SPEC §13).",
            SettingValueKind.Integer, "5",
            v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var m) && m is >= 1 and <= 60 ? null : "Must be a whole number of minutes between 1 and 60."),
        new(SettingKeys.ShippingFeePerOrder,
            "Shipping fee (INR) charged once per separate online/reseller order (SPEC §18).",
            SettingValueKind.Money, "100.00",
            v => v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) && d >= 0 && Money.HasValidScale(d) ? null : "Must be a non-negative amount with at most 2 decimals."),
        new(SettingKeys.SupportWhatsAppNumber,
            "WhatsApp number used for customer order-help deep links (SPEC §12, §21).",
            SettingValueKind.String, "\"9741404304\"",
            v => v.ValueKind == JsonValueKind.String && MobileNumber.TryNormalize(v.GetString(), out _) ? null : "Must be a valid 10-digit Indian mobile number."),
        new(SettingKeys.AdjustmentManagerMaxValue,
            "Largest inventory adjustment (value at cost, INR) a branch approver may approve. Above it only the Owner may approve. 0 = Owner approves all (ADR-001 §14).",
            SettingValueKind.Money, "0.00",
            v => v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) && d >= 0 && Money.HasValidScale(d) ? null : "Must be a non-negative amount with at most 2 decimals."),
        new(SettingKeys.OnlineDepositMinutes,
            "Minutes a reseller has to complete an online UPI wallet deposit; later payments are still credited once confirmed (SPEC §17.1).",
            SettingValueKind.Integer, "15",
            v => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var m) && m is >= 5 and <= 60 ? null : "Must be a whole number of minutes between 5 and 60."),
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}
