using Manoksha.Application.Abstractions;

namespace Manoksha.Modules.Payments.Contracts;

// Business events published through the transactional outbox (consumed by the Notifications module, design §17).

public sealed record PaymentConfirmed(Guid AttemptId, string Purpose, Guid ReferenceId, string ReferenceNumber, decimal Amount, string Status) : IIntegrationEvent
{
    public static string EventType => "payments.payment_confirmed";
}

/// <summary>CRITICAL: money received that could not be applied (SPEC §14.2, §36). The Owner is alerted from the outbox.</summary>
public sealed record PaymentReconciliationRequired(Guid CaseId, string CaseNumber, string ReasonCode, decimal ExpectedAmount, decimal? PaidAmount, string ReferenceNumber)
    : IIntegrationEvent
{
    public static string EventType => "payments.reconciliation_required";
}
