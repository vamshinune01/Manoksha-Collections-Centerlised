using System.Data.Common;
using Manoksha.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Manoksha.Modules.Reporting.Application;

/// <summary>
/// Reporting reads across module schemas with plain SQL (design §3: read-only, never writes). Business dates are IST.
/// </summary>
internal static class Sql
{
    public const string Zone = "Asia/Kolkata";

    /// <summary>
    /// FIFO cost of an order's current allocation (alias <c>o</c>): the latest consumed reservation (online orders, reroutes) or the
    /// order lines (reseller and store sales) — the same rule as the order detail page.
    /// </summary>
    public const string OrderCost = """
        COALESCE(
          (SELECT SUM(rl.cost_amount) FROM orders.reservation_lines rl
            WHERE rl.reservation_id = (SELECT r.id FROM orders.reservations r WHERE r.order_id = o.id AND r.status = 'Consumed' ORDER BY r.created_at DESC LIMIT 1)),
          (SELECT SUM(ol.cost_amount) FROM orders.order_lines ol WHERE ol.order_id = o.id))
        """;

    /// <summary>True when every line of the order (alias <c>o</c>) has a recorded cost.</summary>
    public const string OrderCostComplete = """
        (EXISTS (SELECT 1 FROM orders.reservations r WHERE r.order_id = o.id AND r.status = 'Consumed')
         OR NOT EXISTS (SELECT 1 FROM orders.order_lines ol WHERE ol.order_id = o.id AND ol.cost_amount IS NULL))
        """;

    /// <summary>Orders that count as sales: confirmed (or completed at the POS) and not cancelled.</summary>
    public const string SaleFilter = "o.confirmed_at IS NOT NULL AND o.status <> 'Cancelled'";

    public static DateTimeOffset StartOfDay(DateOnly day)
    {
        var ist = TimeSpan.FromHours(5.5);
        return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), ist).ToUniversalTime();
    }

    public static DateOnly Today(DateTimeOffset utcNow) => DateOnly.FromDateTime(utcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

    public static async Task<List<T>> QueryAsync<T>(ManokshaDbContext db, string sql, IReadOnlyDictionary<string, object?> args, Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(ct);
            opened = true;
        }
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var (name, value) in args)
            {
                cmd.Parameters.Add(value switch
                {
                    Guid[] ids => new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = ids },
                    null => new NpgsqlParameter(name, DBNull.Value),
                    _ => new NpgsqlParameter(name, value),
                });
            }
            var result = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(map(reader));
            }
            return result;
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    public static decimal? NullableDecimal(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDecimal(i);

    public static string? NullableString(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public static Guid? NullableGuid(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);

    public static DateTimeOffset? NullableTime(this DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateTimeOffset>(i);

    public static DateTimeOffset Time(this DbDataReader r, int i) => r.GetFieldValue<DateTimeOffset>(i);
}
