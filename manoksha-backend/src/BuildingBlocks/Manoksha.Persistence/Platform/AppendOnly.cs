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

    /// <summary>
    /// For effective-dated history (prices, discounts): rows can never be deleted, and the only permitted update is closing an
    /// open row (setting effective_to, plus end_reason where present). Past values can never be rewritten.
    /// </summary>
    public const string CreateCloseOnlyFunctionSql = """
        CREATE OR REPLACE FUNCTION platform.allow_only_closing_history() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                RAISE EXCEPTION 'append_only_violation: %.% does not allow DELETE', TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = 'P0001';
            END IF;
            IF OLD.effective_to IS NOT NULL
               OR (to_jsonb(NEW) - 'effective_to' - 'end_reason') <> (to_jsonb(OLD) - 'effective_to' - 'end_reason') THEN
                RAISE EXCEPTION 'append_only_violation: %.% rows can only be closed, not changed', TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = 'P0001';
            END IF;
            RETURN NEW;
        END;
        $$;
        """;

    public static string ProtectCloseOnly(string schema, string table) => $"""
        CREATE TRIGGER trg_{table}_close_only
            BEFORE UPDATE OR DELETE ON {schema}.{table}
            FOR EACH ROW EXECUTE FUNCTION platform.allow_only_closing_history();
        CREATE TRIGGER trg_{table}_no_truncate
            BEFORE TRUNCATE ON {schema}.{table}
            FOR EACH STATEMENT EXECUTE FUNCTION platform.reject_history_modification();
        """;

    public static string UnprotectCloseOnly(string schema, string table) => $"""
        DROP TRIGGER IF EXISTS trg_{table}_close_only ON {schema}.{table};
        DROP TRIGGER IF EXISTS trg_{table}_no_truncate ON {schema}.{table};
        """;

    public static string Unprotect(string schema, string table) => $"""
        DROP TRIGGER IF EXISTS trg_{table}_append_only ON {schema}.{table};
        DROP TRIGGER IF EXISTS trg_{table}_no_truncate ON {schema}.{table};
        """;
}
