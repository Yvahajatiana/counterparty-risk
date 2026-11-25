-- #11 - Partitions, row-level security and least-privilege grants
-- (docs/security/data-flow.md, "Identities", is the single reference for the grants below).

-- ---------------------------------------------------------------- partitioned tables

INSERT INTO meta.partitioned_table (table_schema, table_name)
VALUES
    ('portfolio', 'trade'),
    ('portfolio', 'collateral_position'),
    ('market', 'fx_rate'),
    ('market', 'yield_curve_point'),
    ('market', 'yield_curve_param'),
    ('calc', 'exposure_result'),
    ('calc', 'saccr_result'),
    ('calc', 'profile_point'),
    ('calc', 'rwa_result'),
    ('calc', 'cva_result'),
    ('calc', 'allocation_result')
ON CONFLICT (table_schema, table_name) DO NOTHING;

-- ---------------------------------------------------------------- row-level security

-- Every table carrying legal_entity_id gets the isolation policy (ENABLE + FORCE + WITH CHECK).
-- Later migrations call meta.apply_le_isolation themselves; a test fails on any table left out.
DO $rls$
DECLARE
    v_table regclass;
BEGIN
    FOR v_table IN
        SELECT c.oid::regclass
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attname = 'legal_entity_id' AND NOT a.attisdropped
        WHERE c.relkind IN ('r', 'p')
          AND n.nspname IN ('ref', 'portfolio', 'financing', 'market', 'bench', 'calc', 'reporting', 'meta', 'staging')
        ORDER BY 1
    LOOP
        PERFORM meta.apply_le_isolation(v_table);
    END LOOP;
END
$rls$;

-- Partitions M-1 .. M+3 and the DEFAULT partitions (partitions inherit the isolation policy).
DO $partitions$
BEGIN
    PERFORM meta.ensure_partitions((current_date - interval '1 month')::date, 5);
END
$partitions$;

-- Business events: read by the reader roles, inserted by cr_governance_writer with the
-- HistoryCorrection type only (data-flow.md).
ALTER TABLE meta.risk_event ENABLE ROW LEVEL SECURITY;
ALTER TABLE meta.risk_event FORCE ROW LEVEL SECURITY;
CREATE POLICY risk_event_read ON meta.risk_event FOR SELECT TO cr_reader, cr_writer USING (true);
CREATE POLICY risk_event_history_correction ON meta.risk_event FOR INSERT TO cr_governance_writer
    WITH CHECK (event_type = 'HistoryCorrection');

-- ---------------------------------------------------------------- schema usage

GRANT USAGE ON SCHEMA ref, portfolio, financing, market, bench, calc, reporting, meta TO cr_reader;
GRANT USAGE ON SCHEMA ref, portfolio, financing, market, bench, calc, reporting, meta, staging TO cr_writer;
GRANT USAGE ON SCHEMA portfolio TO cr_limit_writer;
GRANT USAGE ON SCHEMA meta TO cr_governance_writer, cr_scheduler, cr_partition_job;
GRANT USAGE ON SCHEMA audit TO cr_audit_writer, cr_audit_reader, cr_audit_verifier;
GRANT USAGE ON SCHEMA dpkeys TO cr_api_keys, cr_bff_keys;
GRANT USAGE ON SCHEMA session TO cr_bff_session;
GRANT USAGE ON SCHEMA quartz TO cr_scheduler;

-- The policies call this function with the privileges of the querying role.
GRANT EXECUTE ON FUNCTION meta.current_legal_entity_ids() TO cr_reader, cr_writer, cr_limit_writer, cr_governance_writer;

-- ---------------------------------------------------------------- cr_reader (API reads, user scope)

GRANT SELECT ON ALL TABLES IN SCHEMA ref, portfolio, financing, market, bench, calc, reporting TO cr_reader;
GRANT SELECT ON meta.data_source, meta.ingestion_run, meta.method_version, meta.active_method_version,
    meta.dq_result, meta.risk_event TO cr_reader;

ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner IN SCHEMA ref, portfolio, financing, market, bench, calc, reporting
    GRANT SELECT ON TABLES TO cr_reader;

-- ---------------------------------------------------------------- cr_writer (workers, all-entities scope)

GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA ref, portfolio, market, bench, calc, reporting TO cr_writer;
GRANT INSERT ON ALL TABLES IN SCHEMA financing TO cr_writer;
GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA staging TO cr_writer;
GRANT SELECT, INSERT, UPDATE ON meta.ingestion_run, meta.dq_result TO cr_writer;
GRANT SELECT ON meta.data_source, meta.method_version, meta.active_method_version, meta.risk_event TO cr_writer;

ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner IN SCHEMA ref, portfolio, market, bench, calc, reporting
    GRANT SELECT, INSERT, UPDATE ON TABLES TO cr_writer;
ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner IN SCHEMA financing
    GRANT INSERT ON TABLES TO cr_writer;
ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner IN SCHEMA staging
    GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON TABLES TO cr_writer;
ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner IN SCHEMA ref, portfolio, financing, market, bench, calc, reporting, staging
    GRANT USAGE ON SEQUENCES TO cr_writer;

-- ---------------------------------------------------------------- cr_limit_writer (API, limit tables)
-- Limit tables (portfolio.limit, limit_change_request, threshold_policy) and their grants come
-- with #27; until then the role holds no table privilege at all.

-- ---------------------------------------------------------------- cr_governance_writer (API, governance)
-- job_run_request, history_correction_request and method_version_approval come with #19 / #26.

GRANT SELECT, INSERT ON meta.method_version TO cr_governance_writer;
GRANT UPDATE (status, status_changed_at) ON meta.method_version TO cr_governance_writer;
GRANT SELECT, UPDATE ON meta.active_method_version TO cr_governance_writer;
GRANT INSERT ON meta.risk_event TO cr_governance_writer;

-- ---------------------------------------------------------------- cr_scheduler (Quartz)

GRANT SELECT, INSERT, UPDATE ON meta.ingestion_run TO cr_scheduler;
GRANT SELECT ON meta.data_source TO cr_scheduler;
ALTER DEFAULT PRIVILEGES FOR ROLE cr_owner IN SCHEMA quartz
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cr_scheduler;

-- ---------------------------------------------------------------- cr_partition_job (maintenance)

GRANT EXECUTE ON FUNCTION meta.ensure_partitions(date, integer) TO cr_partition_job;
GRANT EXECUTE ON FUNCTION meta.purge_partitions(text, text, date) TO cr_partition_job;

-- ---------------------------------------------------------------- audit, key rings, sessions

GRANT INSERT ON audit.audit_event TO cr_audit_writer;
-- Only what the writer needs to chain the previous hash (TM-TB4-03).
GRANT SELECT (seq, hash) ON audit.audit_event TO cr_audit_writer;
GRANT SELECT ON audit.audit_event TO cr_audit_reader, cr_audit_verifier;

GRANT SELECT, INSERT ON dpkeys.api_key TO cr_api_keys;
GRANT SELECT, INSERT ON dpkeys.bff_key TO cr_bff_keys;

GRANT SELECT, INSERT, UPDATE, DELETE ON session.ticket TO cr_bff_session;
