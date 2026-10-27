-- Run once as a Postgres superuser, before the first `dbt build`.
-- Roles belong to the database server, not to dbt, so they live here.
--
--   sudo -u postgres psql -d pulse -f analytics/setup_roles.sql
--
-- pulse        owns the raw tables and runs dbt.
-- pulse_agent  is what an AI agent connects as. It can log in and read the
--              marts schema, nothing else: no raw events, no staging, no writes.
--              dbt grants it SELECT on each mart (see dbt_project.yml).

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'pulse_agent') THEN
        CREATE ROLE pulse_agent LOGIN PASSWORD 'pulse_agent';  -- local development only
    END IF;
END $$;

-- Read-only by default for every session of this role, as a second safety net.
ALTER ROLE pulse_agent SET default_transaction_read_only = on;
-- A runaway query from an agent gets cut off.
ALTER ROLE pulse_agent SET statement_timeout = '5s';

-- No access to anything in public (raw events, products) unless granted.
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM pulse_agent;
