-- #11 - Foundation: preconditions, schemas, row-level security helpers, partition maintenance.
-- Static DDL only: no external input is ever concatenated into this script.

-- Fail fast: the grants of this migration reference the roles of db/roles.sql, and every object
-- must be owned by cr_owner (cr_migrator logs in with role = cr_owner).
DO $pre$
DECLARE
    v_missing text;
BEGIN
    SELECT string_agg(r, ', ' ORDER BY r) INTO v_missing
    FROM unnest(ARRAY[
        'cr_owner', 'cr_migrator', 'cr_reader', 'cr_writer', 'cr_limit_writer', 'cr_governance_writer',
        'cr_audit_writer', 'cr_audit_reader', 'cr_audit_verifier', 'cr_api_keys', 'cr_bff_keys',
        'cr_bff_session', 'cr_scheduler', 'cr_partition_job']) AS r
    WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = r);

    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'Database roles missing, apply db/roles.sql first: %', v_missing;
    END IF;

    IF current_user <> 'cr_owner' THEN
        RAISE EXCEPTION 'Migrations must run as cr_owner (log in as cr_migrator), not as %', current_user;
    END IF;
END
$pre$;

-- One schema per context (docs/security/data-classification.md).
CREATE SCHEMA IF NOT EXISTS ref;
CREATE SCHEMA IF NOT EXISTS portfolio;
CREATE SCHEMA IF NOT EXISTS financing;
CREATE SCHEMA IF NOT EXISTS market;
CREATE SCHEMA IF NOT EXISTS bench;
CREATE SCHEMA IF NOT EXISTS calc;
CREATE SCHEMA IF NOT EXISTS reporting;
CREATE SCHEMA IF NOT EXISTS meta;
CREATE SCHEMA IF NOT EXISTS staging;
CREATE SCHEMA IF NOT EXISTS audit;
CREATE SCHEMA IF NOT EXISTS dpkeys;
CREATE SCHEMA IF NOT EXISTS session;
CREATE SCHEMA IF NOT EXISTS quartz;

-- Functions are executable by PUBLIC by default: revoke it for everything cr_owner creates.
ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;

-- Legal entity scope of the current transaction, set by the application with
-- set_config('app.legal_entity_ids', '<uuid>,<uuid>', true). Unset or empty: empty scope, so every
-- policy below returns zero rows (fail closed). A malformed value raises an error (fail closed).
CREATE OR REPLACE FUNCTION meta.current_legal_entity_ids()
RETURNS uuid[]
LANGUAGE sql
STABLE
PARALLEL SAFE
AS $fn$
    SELECT pg_catalog.string_to_array(
        NULLIF(pg_catalog.current_setting('app.legal_entity_ids', true), ''), ',')::uuid[]
$fn$;

COMMENT ON FUNCTION meta.current_legal_entity_ids() IS
    'Legal entity scope of the transaction (app.legal_entity_ids). Empty when unset: RLS fails closed.';

-- Applies the legal entity isolation policy to a table carrying legal_entity_id. ENABLE + FORCE:
-- the owner is subject to the policy too. WITH CHECK: a row outside the scope can neither be
-- inserted nor moved out of the scope by an update.
CREATE OR REPLACE FUNCTION meta.apply_le_isolation(p_table regclass)
RETURNS void
LANGUAGE plpgsql
SET search_path = pg_catalog, pg_temp
AS $fn$
BEGIN
    EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY', p_table);
    EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY', p_table);
    EXECUTE format('DROP POLICY IF EXISTS le_isolation ON %s', p_table);
    EXECUTE format(
        'CREATE POLICY le_isolation ON %s AS PERMISSIVE FOR ALL '
        'USING (legal_entity_id = ANY (meta.current_legal_entity_ids())) '
        'WITH CHECK (legal_entity_id = ANY (meta.current_legal_entity_ids()))',
        p_table);
END
$fn$;

-- Registry of the tables partitioned by month (financing.* and audit.* register themselves in
-- their own migrations, #43 and #10).
CREATE TABLE IF NOT EXISTS meta.partitioned_table (
    table_schema varchar(63) NOT NULL,
    -- 55 characters at most: the longest partition suffix (_default, _pYYYYMM) adds 8 and the
    -- PostgreSQL identifier limit is 63; a longer name would be truncated silently and collide.
    table_name varchar(55) NOT NULL,
    purgeable boolean NOT NULL DEFAULT true,
    min_retention_months integer NOT NULL DEFAULT 13,
    registered_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_partitioned_table PRIMARY KEY (table_schema, table_name),
    CONSTRAINT ck_partitioned_table_identifiers
        CHECK (table_schema ~ '^[a-z][a-z0-9_]{0,62}$' AND table_name ~ '^[a-z][a-z0-9_]{0,54}$'),
    CONSTRAINT ck_partitioned_table_retention CHECK (min_retention_months BETWEEN 1 AND 600),
    -- Audit partitions are only detached by a traced operating procedure (TM-TB4-14).
    CONSTRAINT ck_partitioned_table_audit_not_purgeable CHECK (table_schema <> 'audit' OR NOT purgeable)
);

COMMENT ON TABLE meta.partitioned_table IS
    'Tables partitioned by month on as_of_date, maintained by meta.ensure_partitions / meta.purge_partitions.';

-- Creates the DEFAULT partition and the monthly partitions [month(p_from), +p_months) of every
-- registered table. Idempotent. SECURITY DEFINER owned by cr_owner: cr_partition_job only holds
-- EXECUTE on it and never runs DDL directly (TM-TB4-14). Returns the number of partitions created.
-- Rows that already landed in DEFAULT for a missing month are moved into the new partition in the
-- same transaction. A table that still fails is skipped (its changes rolled back), recorded in
-- meta.risk_event (PartitionMaintenanceFailed) and the other tables are processed.
CREATE OR REPLACE FUNCTION meta.ensure_partitions(p_from date, p_months integer)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
SET timezone = 'UTC'
AS $fn$
DECLARE
    t record;
    v_parent regclass;
    v_secure boolean;
    v_start date;
    v_month date;
    v_next date;
    v_name text;
    v_default text;
    v_scope text;
    v_table_created integer;
    v_created integer := 0;
BEGIN
    IF p_from IS NULL OR p_months IS NULL THEN
        RAISE EXCEPTION 'p_from and p_months are required' USING ERRCODE = '22004';
    END IF;

    IF p_months NOT BETWEEN 1 AND 36 THEN
        RAISE EXCEPTION 'p_months must be between 1 and 36' USING ERRCODE = '22023';
    END IF;

    -- Bounded window: history back-fill up to 15 years, creation at most 1 year ahead, so that the
    -- job identity cannot flood the catalogue with partitions.
    IF p_from < (current_date - interval '15 years')::date OR p_from > (current_date + interval '1 year')::date THEN
        RAISE EXCEPTION 'p_from must be between 15 years ago and 1 year ahead' USING ERRCODE = '22023';
    END IF;

    v_start := date_trunc('month', p_from)::date;

    FOR t IN SELECT table_schema, table_name FROM meta.partitioned_table ORDER BY table_schema, table_name
    LOOP
        v_table_created := 0;
        v_month := NULL;
        BEGIN
            v_parent := format('%I.%I', t.table_schema, t.table_name)::regclass;
            SELECT relrowsecurity INTO v_secure FROM pg_class WHERE oid = v_parent;

            -- DEFAULT partition: must stay empty, a row there means a missing monthly partition.
            v_default := t.table_name || '_default';
            IF to_regclass(format('%I.%I', t.table_schema, v_default)) IS NULL THEN
                EXECUTE format('CREATE TABLE %I.%I PARTITION OF %s DEFAULT', t.table_schema, v_default, v_parent);
                IF v_secure THEN
                    PERFORM meta.apply_le_isolation(format('%I.%I', t.table_schema, v_default)::regclass);
                END IF;
                v_table_created := v_table_created + 1;
            END IF;

            FOR i IN 0 .. p_months - 1
            LOOP
                v_month := (v_start + make_interval(months => i))::date;
                v_next := (v_month + interval '1 month')::date;
                v_name := t.table_name || '_p' || to_char(v_month, 'YYYYMM');
                CONTINUE WHEN to_regclass(format('%I.%I', t.table_schema, v_name)) IS NOT NULL;

                BEGIN
                    EXECUTE format(
                        'CREATE TABLE %I.%I PARTITION OF %s FOR VALUES FROM (%L) TO (%L)',
                        t.table_schema, v_name, v_parent, v_month, v_next);
                EXCEPTION WHEN check_violation THEN
                    -- DEFAULT already holds rows of this month: move them into a standalone table,
                    -- then attach it. The owner is subject to FORCE RLS, so the move runs with an
                    -- all-entities scope, restored right after (and by the rollback on failure).
                    v_scope := current_setting('app.legal_entity_ids', true);
                    PERFORM set_config(
                        'app.legal_entity_ids',
                        coalesce((SELECT string_agg(id::text, ',') FROM ref.legal_entity), ''),
                        true);
                    EXECUTE format(
                        'CREATE TABLE %I.%I (LIKE %s INCLUDING DEFAULTS INCLUDING CONSTRAINTS)',
                        t.table_schema, v_name, v_parent);
                    EXECUTE format(
                        'WITH moved AS (DELETE FROM %I.%I WHERE as_of_date >= %L AND as_of_date < %L RETURNING *) '
                        'INSERT INTO %I.%I SELECT * FROM moved',
                        t.table_schema, v_default, v_month, v_next, t.table_schema, v_name);
                    -- Still in the all-entities scope: validating the cloned foreign keys reads the
                    -- referenced rows under row-level security.
                    EXECUTE format(
                        'ALTER TABLE %s ATTACH PARTITION %I.%I FOR VALUES FROM (%L) TO (%L)',
                        v_parent, t.table_schema, v_name, v_month, v_next);
                    PERFORM set_config('app.legal_entity_ids', coalesce(v_scope, ''), true);
                END;

                -- Policies are not inherited by partitions: a direct read of a partition must
                -- not bypass the isolation of its parent.
                IF v_secure THEN
                    PERFORM meta.apply_le_isolation(format('%I.%I', t.table_schema, v_name)::regclass);
                END IF;
                v_table_created := v_table_created + 1;
            END LOOP;

            v_created := v_created + v_table_created;
        EXCEPTION WHEN OTHERS THEN
            -- No row content in the event: table, month and SQLSTATE only.
            INSERT INTO meta.risk_event (id, event_type, as_of_date, details)
            VALUES (gen_random_uuid(), 'PartitionMaintenanceFailed', v_month,
                    jsonb_build_object('table', t.table_schema || '.' || t.table_name, 'sqlstate', SQLSTATE));
            RAISE WARNING 'Partition maintenance failed for %.% (SQLSTATE %)', t.table_schema, t.table_name, SQLSTATE;
        END;
    END LOOP;

    RETURN v_created;
END
$fn$;
-- Detaches and drops the monthly partitions of one registered, purgeable table that end on or
-- before p_before, never inside the minimum retention of the table and never in audit.
-- Returns the number of partitions dropped.
CREATE OR REPLACE FUNCTION meta.purge_partitions(p_schema text, p_table text, p_before date)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
SET timezone = 'UTC'
AS $fn$
DECLARE
    t meta.partitioned_table%ROWTYPE;
    p record;
    v_parent regclass;
    v_limit date;
    v_month date;
    v_prefix text;
    v_dropped integer := 0;
BEGIN
    IF p_schema IS NULL OR p_table IS NULL OR p_before IS NULL THEN
        RAISE EXCEPTION 'p_schema, p_table and p_before are required' USING ERRCODE = '22004';
    END IF;

    IF p_schema = 'audit' THEN
        RAISE EXCEPTION 'audit partitions are never purged by this function' USING ERRCODE = '42501';
    END IF;

    SELECT * INTO t FROM meta.partitioned_table WHERE table_schema = p_schema AND table_name = p_table;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'table is not registered in meta.partitioned_table' USING ERRCODE = '42P01';
    END IF;

    IF NOT t.purgeable THEN
        RAISE EXCEPTION 'table is not purgeable' USING ERRCODE = '42501';
    END IF;

    v_limit := (date_trunc('month', current_date) - make_interval(months => t.min_retention_months))::date;
    IF p_before > v_limit THEN
        RAISE EXCEPTION 'p_before is inside the minimum retention of the table (latest allowed: %)', v_limit
            USING ERRCODE = '22023';
    END IF;

    v_parent := format('%I.%I', t.table_schema, t.table_name)::regclass;
    v_prefix := t.table_name || '_p';

    FOR p IN
        SELECT c.oid::regclass AS part, c.relname::text AS relname
        FROM pg_inherits i
        JOIN pg_class c ON c.oid = i.inhrelid
        WHERE i.inhparent = v_parent
          AND left(c.relname, length(v_prefix)) = v_prefix
          AND substr(c.relname, length(v_prefix) + 1) ~ '^[0-9]{6}$'
        ORDER BY c.relname
    LOOP
        v_month := to_date(right(p.relname, 6), 'YYYYMM');
        IF (v_month + interval '1 month')::date <= p_before THEN
            EXECUTE format('ALTER TABLE %s DETACH PARTITION %s', v_parent, p.part);
            EXECUTE format('DROP TABLE %s', p.part);
            v_dropped := v_dropped + 1;
        END IF;
    END LOOP;

    RETURN v_dropped;
END
$fn$;

REVOKE ALL ON FUNCTION meta.current_legal_entity_ids() FROM PUBLIC;
REVOKE ALL ON FUNCTION meta.apply_le_isolation(regclass) FROM PUBLIC;
REVOKE ALL ON FUNCTION meta.ensure_partitions(date, integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION meta.purge_partitions(text, text, date) FROM PUBLIC;
