namespace Manoksha.Modules.Payments.Contracts;

public static class PaymentPurposes
{
    /// <summary>Online customer order (SPEC §14).</summary>
    public const string Order = "ORDER";

    /// <summary>Provider-confirmed reseller wallet deposit (SPEC §17.1).</summary>
    public const string WalletDeposit = "WALLET_DEPOSIT";
}

/// <param name="ReferenceId">The order / online deposit this payment is for.</param>
/// <param name="ReturnPath">Relative web path the payer returns to after paying.</param>
public sealed record NewPaymentAttempt(
    string Purpose,
    Guid ReferenceId,
    string ReferenceNumber,
    Guid PayerUserId,
    decimal Amount,
    DateTimeOffset ExpiresAt,
    string Description,
    string ReturnPath);

public sealed record PaymentAttemptInfo(
    Guid Id,
    string Purpose,
    Guid ReferenceId,
    string ReferenceNumber,
    Guid PayerUserId,
    string Provider,
    decimal Amount,
    string Status,
    string? ProviderOrderRef,
    string? ProviderPaymentRef,
    string? RedirectUrl,
    DateTimeOffset InitiatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason);

/// <summary>Payment attempts for other modules (Orders, Wallet). The attempt amount is set by the backend, never by the client.</summary>
public interface IPayments
{
    /// <summary>Creates an INITIATED attempt inside the caller's transaction. No provider call is made here.</summary>
    Task<PaymentAttemptInfo> CreateAttemptAsync(NewPaymentAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>
    /// After the caller's transaction commits: creates the provider session (idempotent). If the provider fails, the attempt is
    /// marked FAILED and the purpose handler releases whatever it held (SPEC §33 "Payment initiation fails").
    /// </summary>
    Task<PaymentAttemptInfo> StartSessionAsync(Guid attemptId, CancellationToken cancellationToken = default);

    /// <summary>Re-queries the provider (never trusting the browser) and applies the answer.</summary>
    Task<PaymentAttemptInfo> SyncAsync(Guid attemptId, CancellationToken cancellationToken = default);

    Task<PaymentAttemptInfo?> FindLatestAsync(string purpose, Guid referenceId, CancellationToken cancellationToken = default);
}

public enum PaymentOutcome
{
    /// <summary>The purpose was fulfilled with what was held for it (normal success).</summary>
    Confirmed = 1,

    /// <summary>Paid after the window; the purpose was recovered by re-acquiring what it needs (SPEC §14.2).</summary>
    Recovered = 2,

    /// <summary>Paid, but cannot be fulfilled — the payment needs Owner reconciliation (SPEC §36).</summary>
    NotFulfillable = 3,
}

public sealed record PaymentHandlingResult(PaymentOutcome Outcome, string? ReasonCode = null, string? Detail = null);

/// <param name="WithinWindow">True when the success arrived while the attempt was live and before its expiry.</param>
public sealed record PaymentSuccess(Guid AttemptId, Guid ReferenceId, decimal Amount, string? ProviderPaymentRef, bool WithinWindow);

/// <summary>
/// Implemented by each module that takes payments (Orders: ORDER, Wallet: WALLET_DEPOSIT). Called inside the payment-processing
/// transaction while the attempt row is locked, so effects happen exactly once and atomically with the payment status.
/// </summary>
public interface IPaymentPurposeHandler
{
    string Purpose { get; }

    Task<PaymentHandlingResult> OnPaymentSucceededAsync(PaymentSuccess success, CancellationToken cancellationToken);

    /// <summary>Payment failed (or could not start): release what was held immediately (SPEC §13).</summary>
    Task OnPaymentFailedAsync(Guid referenceId, string reason, CancellationToken cancellationToken);

    /// <summary>The window ended without a confirmed payment.</summary>
    Task OnPaymentExpiredAsync(Guid referenceId, CancellationToken cancellationToken);
}
