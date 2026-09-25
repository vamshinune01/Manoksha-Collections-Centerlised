using System.Collections.Concurrent;
using Manoksha.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manoksha.Integrations.Sms;

public sealed class FakeSmsOptions
{
    /// <summary>E.164 numbers for which delivery is simulated as failed (to exercise the no-fallback path).</summary>
    public string[] FailNumbers { get; set; } = [];
}

/// <summary>
/// DEVELOPMENT/TEST ONLY. Logs the message and keeps the last one per number in memory. Refused in Production.
/// </summary>
public sealed class FakeSmsSender(IOptions<FakeSmsOptions> options, ILogger<FakeSmsSender> logger) : ISmsSender
{
    private static readonly ConcurrentDictionary<string, SmsMessage> LastByNumber = new(StringComparer.Ordinal);

    public static SmsMessage? LastMessageTo(string e164) => LastByNumber.TryGetValue(e164, out var m) ? m : null;

    public Task SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        if (options.Value.FailNumbers.Contains(message.ToE164))
        {
            throw new SmsDeliveryException("Simulated SMS provider failure.");
        }
        LastByNumber[message.ToE164] = message;
        logger.LogInformation("[FAKE SMS] to {To} template {Template}: {Body}", message.ToE164, message.TemplateId, message.Body);
        return Task.CompletedTask;
    }
}
