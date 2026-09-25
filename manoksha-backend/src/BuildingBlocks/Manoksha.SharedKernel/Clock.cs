namespace Manoksha.SharedKernel;

/// <summary>Single source of "now" so time-dependent rules (OTP, reservations) are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
