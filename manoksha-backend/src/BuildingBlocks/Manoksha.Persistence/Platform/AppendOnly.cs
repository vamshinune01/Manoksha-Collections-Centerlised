namespace Manoksha.Persistence.Platform;

/// <summary>
/// SQL used by migrations to make history tables (audit log, wallet ledger, inventory movements, …)
/// append-only at the database level: UPDATE, DELETE and TRUNCATE raise an error for every role.
/// </summary>
public static class AppendOnly
{
    public const string CreateGuardFunctionSql = """
        CREATE OR REPLACE FUNCTION platform.reject_history_modification() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'append_only_violation: %.% does not allow %', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP
                USING ERRCODE = 'P0001';
        END;
        $$;
        """;

    public const string DropGuardFunctionSql = "DROP FUNCTION IF EXISTS platform.reject_history_modification();";

    public static string Protect(string schema, string table) => $"""
        CREATE TRIGGER trg_{table}_append_only
            BEFORE UPDATE OR DELETE ON {schema}.{table}
            FOR EACH ROW EXECUTE FUNCTION platform.reject_history_modification();
        CREATE TRIGGER trg_{table}_no_truncate
            BEFORE TRUNCATE ON {schema}.{table}
            FOR EACH STATEMENT EXECUTE FUNCTION platform.reject_history_modification();
        """;

    public static string Unprotect(string schema, string table) => $"""
        DROP TRIGGER IF EXISTS trg_{table}_append_only ON {schema}.{table};
        DROP TRIGGER IF EXISTS trg_{table}_no_truncate ON {schema}.{table};
        """;
}
