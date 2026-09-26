using Microsoft.EntityFrameworkCore;

namespace Manoksha.Persistence.Platform;

/// <summary>PostgreSQL advisory locks for singleton background work (safe with many worker instances).</summary>
public static class AdvisoryLocks
{
    public const long Housekeeping = 7_100_001;
    public const long WalletIntegrity = 7_100_002;
    public const long ReservationSweeper = 7_100_003;
    public const long PaymentPoller = 7_100_004;

    /// <summary>Transaction-scoped lock; released automatically at commit/rollback. Requires an open transaction.</summary>
    public static async Task<bool> TryAcquireTransactionLockAsync(ManokshaDbContext db, long key, CancellationToken cancellationToken)
    {
        var acquired = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"")
            .SingleAsync(cancellationToken);
        return acquired;
    }

    /// <summary>
    /// Session-scoped lock for jobs that run many short transactions. Opens (and keeps) the context's connection so the lock and
    /// every later statement use the same session; call <see cref="ReleaseSessionLockAsync"/> when done.
    /// </summary>
    public static async Task<bool> TryAcquireSessionLockAsync(ManokshaDbContext db, long key, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        return await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"").SingleAsync(cancellationToken);
    }

    public static async Task ReleaseSessionLockAsync(ManokshaDbContext db, long key)
    {
        await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({key}) AS \"Value\"").SingleAsync();
        await db.Database.CloseConnectionAsync();
    }
}
