namespace Manoksha.Application.Abstractions;

/// <summary>Typed read access to Owner-managed business settings (reservation minutes, thresholds, …).</summary>
public interface ISettingsReader
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);
}

public static class SettingKeys
{
    /// <summary>Default inventory reservation / payment window in minutes (SPEC §13; default 5).</summary>
    public const string ReservationMinutes = "reservation.minutes";

    /// <summary>Shipping fee per separate online/reseller order in INR (SPEC §18; ₹100).</summary>
    public const string ShippingFeePerOrder = "shipping.fee_per_order";

    /// <summary>WhatsApp number for customer order-help deep links (SPEC §12, §21; 9741404304).</summary>
    public const string SupportWhatsAppNumber = "support.whatsapp_number";

    /// <summary>
    /// Max value at cost (INR) of an inventory adjustment a branch approver may approve; above it only the Owner may
    /// (ADR-001 §14). Default 0 = every adjustment needs the Owner.
    /// </summary>
    public const string AdjustmentManagerMaxValue = "inventory.adjustment.manager_max_value";

    /// <summary>Minutes a reseller has to complete an online (provider-confirmed) wallet deposit payment (SPEC §17.1).</summary>
    public const string OnlineDepositMinutes = "payments.online_deposit_minutes";
}
