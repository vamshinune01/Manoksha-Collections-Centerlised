using Manoksha.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Persistence;

internal sealed class UnitOfWork(ManokshaDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            // Join the ambient transaction; the outermost owner commits.
            var joined = await work(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return joined;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync<bool>(async ct =>
        {
            await work(ct);
            return true;
        }, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
