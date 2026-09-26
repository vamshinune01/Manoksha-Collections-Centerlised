using Microsoft.EntityFrameworkCore;

namespace Manoksha.Persistence;

public static class TrackingExtensions
{
    /// <summary>
    /// Stops tracking matching unchanged entities so a following locking query (<c>SELECT … FOR UPDATE</c>) materialises fresh database
    /// values — EF identity resolution would otherwise hand back the already-tracked, possibly stale instance.
    /// </summary>
    public static void Forget<T>(this DbContext db, Func<T, bool> match)
        where T : class
    {
        foreach (var entry in db.ChangeTracker.Entries<T>().Where(e => e.State == EntityState.Unchanged && match(e.Entity)).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
