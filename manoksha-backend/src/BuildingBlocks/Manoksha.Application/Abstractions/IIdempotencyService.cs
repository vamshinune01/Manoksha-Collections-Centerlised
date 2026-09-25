namespace Manoksha.Application.Abstractions;

/// <summary>
/// Server-side idempotency for client-repeatable operations (Place Order, Pay, POS finalize, approvals).
/// The record is written in the same transaction as the operation, so: a repeat with the same key and
/// payload returns the original response without re-executing; a repeat with a different payload is
/// rejected; a failed (rolled-back) attempt leaves no record and may be retried (design §13).
/// </summary>
public interface IIdempotencyService
{
    Task<TResponse> ExecuteAsync<TResponse>(
        string scope,
        string key,
        object request,
        Func<CancellationToken, Task<TResponse>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs work in one database transaction (joins an ambient transaction when one exists).</summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
