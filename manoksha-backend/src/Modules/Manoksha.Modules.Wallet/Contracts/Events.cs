using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Wallet.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record WalletDepositCredited(Guid ResellerId, Guid DepositId, string Number, decimal Amount) : IIntegrationEvent
{
    public static string EventType => "wallet.online_deposit_credited";
}

public sealed record DepositSubmittedEvent(Guid DepositId, Guid ResellerId, decimal Amount, string? Number = null) : IIntegrationEvent
{
    public static string EventType => "wallet.deposit_submitted";
}

public sealed record DepositDecidedEvent(Guid DepositId, Guid ResellerId, string Status, string? Number = null, decimal? Amount = null, string? Note = null) : IIntegrationEvent
{
    public static string EventType => "wallet.deposit_decided";
}

public sealed record WalletIntegrityMismatch(int IssueCount) : IIntegrationEvent
{
    public static string EventType => "wallet.integrity_mismatch";
}
