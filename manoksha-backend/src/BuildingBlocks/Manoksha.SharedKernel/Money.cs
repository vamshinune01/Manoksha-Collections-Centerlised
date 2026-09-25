namespace Manoksha.SharedKernel;

/// <summary>
/// INR money arithmetic. Money is always <see cref="decimal"/> (never floating point) and authoritative
/// results are rounded to paisa (2 dp) using HALF-UP business rounding (ADR-001 §11).
/// </summary>
public static class Money
{
    public const int Scale = 2;

    /// <summary>Rounds to 2 decimals, half away from zero (₹1,234.567 → ₹1,234.57; ₹0.005 → ₹0.01).</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, Scale, MidpointRounding.AwayFromZero);

    /// <summary>amount × (1 − percent/100), rounded to paisa.</summary>
    public static decimal ApplyPercentDiscount(decimal amount, decimal percent)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }
        if (percent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Percent must be between 0 and 100.");
        }
        return Round(amount * (1m - (percent / 100m)));
    }

    public static bool HasValidScale(decimal amount) => decimal.Round(amount, Scale) == amount;
}
