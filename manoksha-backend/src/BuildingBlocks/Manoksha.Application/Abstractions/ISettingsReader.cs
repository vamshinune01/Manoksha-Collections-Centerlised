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
}
