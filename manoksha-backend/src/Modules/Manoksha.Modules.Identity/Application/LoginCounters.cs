using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Modules.Identity.Application;

/// <summary>
/// Sign-in and approval-PIN failure counters as single atomic UPDATEs. They are never written through the version-checked user row,
/// so simultaneous attempts (the Owner on two devices, or an attacker guessing in parallel) can neither fail with a conflict nor lose
/// a count. PostgreSQL evaluates every SET expression against the old row, so the lock decision is consistent.
/// </summary>
internal static class LoginCounters
{
    /// <returns>True when this failure locked the account.</returns>
    public static async Task<bool> RecordFailureAsync(ManokshaDbContext db, Guid userId, int maxAttempts, DateTimeOffset lockUntil, CancellationToken ct) =>
        (await db.Database.SqlQuery<bool>($"""
            UPDATE identity.users SET
                lockout_until = CASE WHEN failed_login_count + 1 >= {maxAttempts} THEN {lockUntil} ELSE lockout_until END,
                failed_login_count = CASE WHEN failed_login_count + 1 >= {maxAttempts} THEN 0 ELSE failed_login_count + 1 END
            WHERE id = {userId}
            RETURNING lockout_until IS NOT DISTINCT FROM {lockUntil} AS "Value"
            """).ToListAsync(ct)).SingleOrDefault();

    public static Task RecordSuccessAsync(ManokshaDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE identity.users SET failed_login_count = 0, lockout_until = NULL, last_login_at = {now} WHERE id = {userId}", ct);

    /// <returns>True when this failure locked the approval PIN.</returns>
    public static async Task<bool> RecordPinFailureAsync(ManokshaDbContext db, Guid userId, int maxFailures, DateTimeOffset lockUntil, CancellationToken ct) =>
        (await db.Database.SqlQuery<bool>($"""
            UPDATE identity.users SET
                approval_pin_locked_until = CASE WHEN approval_pin_failures + 1 >= {maxFailures} THEN {lockUntil} ELSE approval_pin_locked_until END,
                approval_pin_failures = CASE WHEN approval_pin_failures + 1 >= {maxFailures} THEN 0 ELSE approval_pin_failures + 1 END
            WHERE id = {userId}
            RETURNING approval_pin_locked_until IS NOT DISTINCT FROM {lockUntil} AS "Value"
            """).ToListAsync(ct)).SingleOrDefault();

    public static Task RecordPinSuccessAsync(ManokshaDbContext db, Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET approval_pin_failures = 0 WHERE id = {userId}", ct);
}
