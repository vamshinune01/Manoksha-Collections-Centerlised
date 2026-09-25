using Manoksha.SharedKernel;

namespace Manoksha.IntegrationTests.Infrastructure;

/// <summary>Real time plus a controllable offset, so tokens stay valid while tests can "advance" time.</summary>
public sealed class TestClock : IClock
{
    private long _offsetTicks;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}
