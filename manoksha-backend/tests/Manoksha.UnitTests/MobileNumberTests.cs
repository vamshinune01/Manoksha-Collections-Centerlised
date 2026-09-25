using Manoksha.SharedKernel;

namespace Manoksha.UnitTests;

public class MobileNumberTests
{
    [Theory]
    [InlineData("9741404304")]
    [InlineData("+91 97414 04304")]
    [InlineData("919741404304")]
    [InlineData("09741404304")]
    [InlineData("+91-974-140-4304")]
    public void Normalizes_indian_mobile_to_e164(string input)
    {
        MobileNumber.TryNormalize(input, out var e164).Should().BeTrue();
        e164.Should().Be("+919741404304");
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("5741404304")]      // Indian mobiles start 6–9
    [InlineData("+14155550123")]    // non-Indian
    [InlineData("97414O4304")]      // letter O
    public void Rejects_invalid_numbers(string input) => MobileNumber.TryNormalize(input, out _).Should().BeFalse();

    [Fact]
    public void Mask_hides_middle_digits() => MobileNumber.Mask("+919741404304").Should().Be("+91******4304");
}
