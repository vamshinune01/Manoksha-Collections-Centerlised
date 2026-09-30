using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Resellers.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record ResellerCreated(Guid ResellerId, string ResellerNumber) : IIntegrationEvent
{
    public static string EventType => "resellers.reseller_created";
}

public sealed record CommercialTermsChanged(Guid ResellerId, int Version, decimal? DiscountPct = null, DateTimeOffset? EffectiveFrom = null, string? Notes = null) : IIntegrationEvent
{
    public static string EventType => "resellers.commercial_terms_changed";
}

public sealed record ResellerStatusChanged(Guid ResellerId, string Status, string? Reason = null) : IIntegrationEvent
{
    public static string EventType => "resellers.status_changed";
}
