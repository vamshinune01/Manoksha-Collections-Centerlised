using Manoksha.SharedKernel;

namespace Manoksha.UnitTests;

public class Uuid7Tests
{
    [Fact]
    public void Generates_version_7_rfc_variant()
    {
        var bytes = Uuid7.NewGuid().ToByteArray(bigEndian: true);
        (bytes[6] >> 4).Should().Be(7);
        (bytes[8] >> 6).Should().Be(2);
    }

    [Fact]
    public void Is_monotonic_and_unique_under_rapid_generation()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => Uuid7.NewGuid()).ToList();
        ids.Distinct().Should().HaveCount(ids.Count);
        var ordered = ids.Select(g => Convert.ToHexString(g.ToByteArray(bigEndian: true))).ToList();
        ordered.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }
}
