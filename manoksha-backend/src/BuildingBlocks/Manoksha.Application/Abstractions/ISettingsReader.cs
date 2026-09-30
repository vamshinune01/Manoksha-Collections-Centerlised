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

    /// <summary>Largest POS discount (%) a seller with price-override permission may give without approval (Phase 8: 5).</summary>
    public const string PosStaffMaxDiscountPct = "pos.staff_max_discount_pct";

    /// <summary>Largest POS discount (%) a branch approver may give/approve; above it only the Owner (Phase 8: 15).</summary>
    public const string PosManagerMaxDiscountPct = "pos.manager_max_discount_pct";

    /// <summary>Comma-separated store payment methods accepted at the POS: CASH, UPI, CARD, OTHER (Phase 8: UPI).</summary>
    public const string PosPaymentMethods = "pos.payment_methods";

    /// <summary>A SKU is low on stock at a branch when its available quantity is at or below this number (ADR-001 §38: one global threshold).</summary>
    public const string LowStockThreshold = "inventory.low_stock_threshold";

    /// <summary>Hour of the day (IST, 0–23) when the Owner's daily summary email is sent (ADR-001 §39).</summary>
    public const string DailySummaryHour = "notifications.daily_summary_hour";
}
