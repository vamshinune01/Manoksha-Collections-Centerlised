using System.Text.Json;
using Manoksha.Application.Abstractions;
using Manoksha.Modules.Settings.Application;

namespace Manoksha.UnitTests;

public class SettingDefinitionsTests
{
    [Theory]
    [InlineData(SettingKeys.ReservationMinutes, "5")]
    [InlineData(SettingKeys.ShippingFeePerOrder, "100.00")]
    [InlineData(SettingKeys.SupportWhatsAppNumber, "\"9741404304\"")]
    public void Spec_defaults(string key, string json) => SettingDefinitions.Find(key)!.DefaultJson.Should().Be(json);

    [Theory]
    [InlineData(SettingKeys.ReservationMinutes, "0", false)]
    [InlineData(SettingKeys.ReservationMinutes, "10", true)]
    [InlineData(SettingKeys.ReservationMinutes, "2.5", false)]
    [InlineData(SettingKeys.ShippingFeePerOrder, "100.005", false)]
    [InlineData(SettingKeys.ShippingFeePerOrder, "-1", false)]
    [InlineData(SettingKeys.ShippingFeePerOrder, "120.50", true)]
    [InlineData(SettingKeys.SupportWhatsAppNumber, "\"12345\"", false)]
    public void Validation(string key, string json, bool valid) =>
        (SettingDefinitions.Find(key)!.Validate(JsonDocument.Parse(json).RootElement) is null).Should().Be(valid);
}
