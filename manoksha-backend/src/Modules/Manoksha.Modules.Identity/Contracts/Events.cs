using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Identity.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record InternalUserCreated(Guid UserId, string Email) : IIntegrationEvent
{
    public static string EventType => "identity.internal_user_created";
}

public sealed record UserAccessChanged(Guid UserId, string Change) : IIntegrationEvent
{
    public static string EventType => "identity.user_access_changed";
}

/// <summary>A security event the Owner must see (Exception Center "Sensitive Alert"): ACCOUNT_LOCKED or APPROVAL_PIN_LOCKED.</summary>
public sealed record SecurityAlertRaised(string Kind, Guid UserId, string DisplayName, string Detail) : IIntegrationEvent
{
    public static string EventType => "identity.security_alert";
}
