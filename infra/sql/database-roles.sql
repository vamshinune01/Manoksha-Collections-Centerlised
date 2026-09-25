-- Database roles for Cloud SQL (dev/staging/prod). Run once per environment by an administrator.
-- Passwords are set out-of-band from Secret Manager; none are stored in this repository.
--
--   manoksha_migrator : owns schemas, runs EF migrations (Cloud Run Job "migrate").
--   manoksha_app      : used by API and Worker. May read/write business tables but cannot run DDL.
--                       History tables are additionally protected by append-only triggers.
--   manoksha_readonly : reporting / support read access.

CREATE ROLE manoksha_migrator LOGIN;
CREATE ROLE manoksha_app LOGIN;
CREATE ROLE manoksha_readonly LOGIN;

GRANT CONNECT ON DATABASE manoksha TO manoksha_app, manoksha_readonly;
GRANT CREATE ON DATABASE manoksha TO manoksha_migrator;

-- After the first migration has created the schemas, run as manoksha_migrator:
--
-- DO $$
-- DECLARE s text;
-- BEGIN
--   FOREACH s IN ARRAY ARRAY['platform','identity','audit','settings'] LOOP
--     EXECUTE format('GRANT USAGE ON SCHEMA %I TO manoksha_app, manoksha_readonly', s);
--     EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO manoksha_app', s);
--     EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO manoksha_app', s);
--     EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO manoksha_readonly', s);
--     EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO manoksha_app', s);
--     EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT ON TABLES TO manoksha_readonly', s);
--   END LOOP;
-- END $$;
--
-- -- Append-only history: the application role may only INSERT/SELECT.
-- REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_log, settings.system_setting_changes, identity.login_events FROM manoksha_app;
