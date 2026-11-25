-- Service identities of the application database (#11).
-- Single reference: docs/security/data-flow.md, section "Identities". One role per permission set,
-- one connection per role; privileges are never merged into one role.
--
-- Run ONCE per database, BEFORE the migration bundle, by the platform bootstrap or a DBA holding
-- CREATEROLE (local: the container superuser). The script is idempotent: running it again changes
-- nothing. It contains no password: LOGIN roles receive their credential from the secret store
-- (user-secrets locally, vault elsewhere) with ALTER ROLE ... PASSWORD, outside the repository.
--
-- Object privileges (schemas, tables, functions, RLS) are granted by the migrations, which run as
-- cr_owner through cr_migrator. This script only creates the roles and locks the database down.
--
-- cr_keycloak is not created here: it owns the separate Keycloak database and has no access to
-- this one (TM-TB4-15).

DO $roles$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT name, can_login
        FROM (VALUES
            ('cr_owner', false),
            ('cr_migrator', true),
            ('cr_reader', true),
            ('cr_writer', true),
            ('cr_limit_writer', true),
            ('cr_governance_writer', true),
            ('cr_audit_writer', true),
            ('cr_audit_reader', true),
            ('cr_audit_verifier', true),
            ('cr_api_keys', true),
            ('cr_bff_keys', true),
            ('cr_bff_session', true),
            ('cr_scheduler', true),
            ('cr_partition_job', true)
        ) AS v(name, can_login)
    LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r.name) THEN
            EXECUTE format('CREATE ROLE %I', r.name);
        END IF;

        -- Re-asserted on every run so that a manual drift (BYPASSRLS, SUPERUSER...) is reverted.
        EXECUTE format(
            'ALTER ROLE %I %s NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS',
            r.name,
            CASE WHEN r.can_login THEN 'LOGIN' ELSE 'NOLOGIN' END);
    END LOOP;
END
$roles$;

-- cr_migrator acts as cr_owner only: every object it creates is owned by cr_owner (NOLOGIN), and it
-- holds no privilege of its own. SET TRUE allows SET ROLE, INHERIT FALSE keeps the privileges explicit.
GRANT cr_owner TO cr_migrator WITH INHERIT FALSE, SET TRUE;
ALTER ROLE cr_migrator SET role = 'cr_owner';

-- Database lock-down: nobody connects or creates anything unless granted below.
DO $database$
BEGIN
    EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database());
    EXECUTE format(
        'GRANT CONNECT ON DATABASE %I TO cr_migrator, cr_reader, cr_writer, cr_limit_writer, '
        'cr_governance_writer, cr_audit_writer, cr_audit_reader, cr_audit_verifier, cr_api_keys, '
        'cr_bff_keys, cr_bff_session, cr_scheduler, cr_partition_job',
        current_database());
    -- Schemas are created by the migrations, as cr_owner.
    EXECUTE format('GRANT CREATE ON DATABASE %I TO cr_owner', current_database());
END
$database$;

-- The public schema is not used by the application.
REVOKE ALL ON SCHEMA public FROM PUBLIC;
