using Manoksha.IntegrationTests.Infrastructure;
using Npgsql;

namespace Manoksha.IntegrationTests;

/// <summary>Database conventions that keep the modular monolith consistent.</summary>
[Collection(ApiCollection.Name)]
public class SchemaConventionTests(ManokshaApiFactory factory)
{
    [Fact]
    public async Task Every_cross_module_foreign_key_is_checked_at_commit()
    {
        await using var c = new NpgsqlConnection(factory.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT con.conname
            FROM pg_constraint con
            JOIN pg_class src ON src.oid = con.conrelid
            JOIN pg_namespace srcns ON srcns.oid = src.relnamespace
            JOIN pg_class ref ON ref.oid = con.confrelid
            JOIN pg_namespace refns ON refns.oid = ref.relnamespace
            WHERE con.contype = 'f' AND srcns.nspname <> refns.nspname AND NOT (con.condeferrable AND con.condeferred)
            """, c);
        var offenders = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            offenders.Add(r.GetString(0));
        }
        offenders.Should().BeEmpty("cross-module FKs must be DEFERRABLE INITIALLY DEFERRED because EF cannot order inserts for them");
    }
}
