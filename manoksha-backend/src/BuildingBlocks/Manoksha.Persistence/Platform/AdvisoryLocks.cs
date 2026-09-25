using Microsoft.EntityFrameworkCore;

namespace Manoksha.Persistence.Platform;

/// <summary>PostgreSQL advisory locks for singleton background work (safe with many worker instances).</summary>
public static class AdvisoryLocks
{
    public const long Housekeeping = 7_100_001;
    public const long WalletIntegrity = 7_100_002;

    /// <summary>Transaction-scoped lock; released automatically at commit/rollback. Requires an open transaction.</summary>
    public static async Task<bool> TryAcquireTransactionLockAsync(ManokshaDbContext db, long key, CancellationToken cancellationToken)
    {
        var acquired = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({key}) AS \"Value\"")
            .SingleAsync(cancellationToken);
        return acquired;
    }
}
