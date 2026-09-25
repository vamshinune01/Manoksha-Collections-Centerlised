using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Orders.Application;

/// <summary>WhatsApp deep links (no bot/automation in V1 — SPEC §21, §29). The number comes from settings (9741404304).</summary>
internal sealed class WhatsApp(ISettingsReader settings)
{
    public async Task<string> OrderHelpUrlAsync(string orderNumber, CancellationToken ct) =>
        Build(await settings.GetAsync<string>(SettingKeys.SupportWhatsAppNumber, ct),
            $"Hello Manoksha Collections,\nI need help with my order.\nOrder ID: {orderNumber}\nIssue: CANCEL / ITEM CHANGE / OTHER\nPlease assist me.");

    public async Task<InquiryDto> InquiryAsync(string reference, CancellationToken ct)
    {
        var number = await settings.GetAsync<string>(SettingKeys.SupportWhatsAppNumber, ct);
        var message = $"We are unable to complete this order online with currently available branch inventory. Reference: {reference}. " +
                      $"Please contact Manoksha Collections on WhatsApp at {number} and share this reference number.";
        return new InquiryDto(reference, message, Build(number, $"Hello Manoksha Collections,\nI could not complete an order.\nReference: {reference}\nPlease assist me."));
    }

    private static string Build(string number, string text) => $"https://wa.me/91{number}?text={Uri.EscapeDataString(text)}";
}
