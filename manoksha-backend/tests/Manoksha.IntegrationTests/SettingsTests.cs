using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Manoksha.Application.Abstractions;
using Manoksha.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SettingsTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Spec_defaults_are_seeded()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsReader>();
        (await settings.GetAsync<decimal>(SettingKeys.ShippingFeePerOrder)).Should().Be(100.00m);
        (await settings.GetAsync<string>(SettingKeys.SupportWhatsAppNumber)).Should().Be("9741404304");
        (await settings.GetAsync<int>(SettingKeys.ReservationMinutes)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Owner_changes_setting_with_reason_and_version_and_it_is_audited()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var list = await (await owner.GetAsync("/api/v1/admin/settings")).ReadJsonAsync();
        var current = list.AsArray().Single(s => s!["key"]!.GetValue<string>() == SettingKeys.ReservationMinutes)!;
        var version = current["version"]!.GetValue<int>();

        (await (await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.ReservationMinutes}",
            new { value = 7, expectedVersion = version, reason = "" })).ErrorCodeAsync()).Should().Be("REASON_REQUIRED");
        (await (await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.ReservationMinutes}",
            new { value = 0, expectedVersion = version, reason = "x" })).ErrorCodeAsync()).Should().Be("SETTING_VALUE_INVALID");

        var ok = await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.ReservationMinutes}",
            new { value = 5, expectedVersion = version, reason = "confirm default" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.ReadJsonAsync())["version"]!.GetValue<int>().Should().Be(version + 1);

        var stale = await owner.PutAsJsonAsync($"/api/v1/admin/settings/{SettingKeys.ReservationMinutes}",
            new { value = 6, expectedVersion = version, reason = "stale" });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var audit = await (await owner.GetAsync($"/api/v1/admin/audit?entityType=SystemSetting&entityId={SettingKeys.ReservationMinutes}")).ReadJsonAsync();
        audit["items"]!.AsArray().Should().Contain(e => e!["reason"]!.GetValue<string>() == "confirm default");
    }

    [Fact]
    public async Task Unknown_setting_is_not_found()
    {
        var owner = factory.Authorized((await factory.LoginOwnerAsync()).AccessToken);
        var response = await owner.PutAsJsonAsync("/api/v1/admin/settings/made.up", new { value = JsonValue.Create(1), expectedVersion = 1, reason = "x" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
