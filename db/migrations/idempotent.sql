DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'meta') THEN
        CREATE SCHEMA meta;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS meta.ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK_ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
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
        table_name varchar(56) NOT NULL,
        purgeable boolean NOT NULL DEFAULT true,
        min_retention_months integer NOT NULL DEFAULT 13,
        registered_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_partitioned_table PRIMARY KEY (table_schema, table_name),
        CONSTRAINT ck_partitioned_table_identifiers
            CHECK (table_schema ~ '^[a-z][a-z0-9_]*$' AND table_name ~ '^[a-z][a-z0-9_]*$'),
        CONSTRAINT ck_partitioned_table_retention CHECK (min_retention_months BETWEEN 1 AND 600),
        -- Audit partitions are only detached by a traced operating procedure (TM-TB4-14).
        CONSTRAINT ck_partitioned_table_audit_not_purgeable CHECK (table_schema <> 'audit' OR NOT purgeable)
    );

    COMMENT ON TABLE meta.partitioned_table IS
        'Tables partitioned by month on as_of_date, maintained by meta.ensure_partitions / meta.purge_partitions.';

    -- Creates the DEFAULT partition and the monthly partitions [month(p_from), +p_months) of every
    -- registered table. Idempotent. SECURITY DEFINER owned by cr_owner: cr_partition_job only holds
    -- EXECUTE on it and never runs DDL directly (TM-TB4-14). Returns the number of partitions created.
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
        v_name text;
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
            v_parent := format('%I.%I', t.table_schema, t.table_name)::regclass;
            SELECT relrowsecurity INTO v_secure FROM pg_class WHERE oid = v_parent;

            -- DEFAULT partition: must stay empty, a row there means a missing monthly partition.
            v_name := t.table_name || '_default';
            IF to_regclass(format('%I.%I', t.table_schema, v_name)) IS NULL THEN
                EXECUTE format('CREATE TABLE %I.%I PARTITION OF %s DEFAULT', t.table_schema, v_name, v_parent);
                IF v_secure THEN
                    PERFORM meta.apply_le_isolation(format('%I.%I', t.table_schema, v_name)::regclass);
                END IF;
                v_created := v_created + 1;
            END IF;

            FOR i IN 0 .. p_months - 1
            LOOP
                v_month := (v_start + make_interval(months => i))::date;
                v_name := t.table_name || '_p' || to_char(v_month, 'YYYYMM');
                IF to_regclass(format('%I.%I', t.table_schema, v_name)) IS NULL THEN
                    EXECUTE format(
                        'CREATE TABLE %I.%I PARTITION OF %s FOR VALUES FROM (%L) TO (%L)',
                        t.table_schema, v_name, v_parent, v_month, (v_month + interval '1 month')::date);
                    -- Policies are not inherited by partitions: a direct read of a partition must
                    -- not bypass the isolation of its parent.
                    IF v_secure THEN
                        PERFORM meta.apply_le_isolation(format('%I.%I', t.table_schema, v_name)::regclass);
                    END IF;
                    v_created := v_created + 1;
                END IF;
            END LOOP;
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

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
    -- #11 - Governance (meta) and reference data (ref).
    -- Enumerations are varchar + CHECK on the member names of the Domain.Contracts enums (#5): readable
    -- in SQL, no PostgreSQL enum type to migrate. A test compares every CHECK with its C# enum.

    -- ---------------------------------------------------------------- meta: sources, runs, methods

    CREATE TABLE meta.data_source (
        id varchar(64) NOT NULL,
        name varchar(256) NOT NULL,
        licence varchar(128) NOT NULL,
        url varchar(512) NOT NULL,
        attribution varchar(256) NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_data_source PRIMARY KEY (id),
        CONSTRAINT ck_data_source_id CHECK (id ~ '^[a-z0-9][a-z0-9.-]{0,63}$'),
        CONSTRAINT ck_data_source_url CHECK (url ~ '^https://')
    );

    COMMENT ON TABLE meta.data_source IS 'Open data sources, licence and mandatory attribution (rule R6).';

    CREATE TABLE meta.ingestion_run (
        id uuid NOT NULL,
        source_id varchar(64) NOT NULL,
        as_of_date date,
        status varchar(16) NOT NULL,
        file_sha256 char(64),
        row_count bigint,
        started_at timestamptz NOT NULL DEFAULT now(),
        finished_at timestamptz,
        CONSTRAINT pk_ingestion_run PRIMARY KEY (id),
        CONSTRAINT fk_ingestion_run_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT ck_ingestion_run_status CHECK (status IN ('Running', 'Succeeded', 'Failed', 'Quarantined')),
        CONSTRAINT ck_ingestion_run_sha256 CHECK (file_sha256 ~ '^[0-9a-f]{64}$'),
        CONSTRAINT ck_ingestion_run_row_count CHECK (row_count >= 0),
        CONSTRAINT ck_ingestion_run_finished CHECK (finished_at IS NULL OR finished_at >= started_at)
    );

    CREATE INDEX ix_ingestion_run_source_as_of ON meta.ingestion_run (source_id, as_of_date);

    -- Method versions of the code catalogue (ADR-002 governance, #26). The id is the catalogue code
    -- (MethodVersionId), parameters_sha256 is recomputed from the catalogue and compared (TM-TB3-14).
    CREATE TABLE meta.method_version (
        id varchar(32) NOT NULL,
        framework varchar(32) NOT NULL,
        cva_approach varchar(16) NOT NULL,
        pfe_quantile numeric(6, 5) NOT NULL,
        allocation_rule varchar(16) NOT NULL,
        reporting_currency char(3) NOT NULL,
        limit_alert_threshold numeric(6, 5) NOT NULL,
        parameters jsonb NOT NULL,
        parameters_sha256 char(64) NOT NULL,
        regulatory_reference_date date NOT NULL,
        effective_from date NOT NULL,
        status varchar(16) NOT NULL,
        published_by varchar(128) NOT NULL,
        published_at timestamptz NOT NULL DEFAULT now(),
        status_changed_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_method_version PRIMARY KEY (id),
        CONSTRAINT ck_method_version_id CHECK (id ~ '^[a-z][a-z0-9.-]{2,31}$'),
        CONSTRAINT ck_method_version_cva_approach CHECK (cva_approach IN ('ReducedBaCva', 'FullBaCva')),
        CONSTRAINT ck_method_version_allocation_rule CHECK (allocation_rule IN ('Euler', 'ProRata')),
        CONSTRAINT ck_method_version_status
            CHECK (status IN ('Computing', 'PendingApproval', 'Active', 'Rejected', 'Retired')),
        CONSTRAINT ck_method_version_pfe_quantile CHECK (pfe_quantile > 0.5 AND pfe_quantile < 1),
        CONSTRAINT ck_method_version_alert_threshold CHECK (limit_alert_threshold > 0 AND limit_alert_threshold < 1),
        CONSTRAINT ck_method_version_currency CHECK (reporting_currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_method_version_sha256 CHECK (parameters_sha256 ~ '^[0-9a-f]{64}$'),
        CONSTRAINT ck_method_version_parameters CHECK (jsonb_typeof(parameters) = 'object')
    );

    -- Single active pointer consumed by #26 and #31: one row (singleton), switched in one transaction
    -- by cr_governance_writer only (TM-TB3-15). NULL until the first approval.
    CREATE TABLE meta.active_method_version (
        singleton boolean NOT NULL DEFAULT true,
        method_version_id varchar(32),
        activated_at timestamptz,
        CONSTRAINT pk_active_method_version PRIMARY KEY (singleton),
        CONSTRAINT ck_active_method_version_singleton CHECK (singleton),
        CONSTRAINT fk_active_method_version FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id)
    );

    INSERT INTO meta.active_method_version (singleton, method_version_id, activated_at)
    VALUES (true, NULL, NULL)
    ON CONFLICT (singleton) DO NOTHING;

    CREATE TABLE meta.dq_result (
        id uuid NOT NULL,
        ingestion_run_id uuid,
        as_of_date date NOT NULL,
        rule_code varchar(32) NOT NULL,
        status varchar(16) NOT NULL,
        measured_value numeric(28, 8),
        threshold numeric(28, 8),
        checked_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_dq_result PRIMARY KEY (id),
        CONSTRAINT fk_dq_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_dq_result_status CHECK (status IN ('Passed', 'Warning', 'Failed')),
        CONSTRAINT ck_dq_result_rule_code CHECK (rule_code ~ '^[A-Z][A-Z0-9-]{2,31}$')
    );

    CREATE INDEX ix_dq_result_as_of_rule ON meta.dq_result (as_of_date, rule_code);

    -- Business events (method switch, history correction...). No row content, no personal data
    -- beyond the actor sub (C1-PD).
    CREATE TABLE meta.risk_event (
        id uuid NOT NULL,
        event_type varchar(32) NOT NULL,
        as_of_date date,
        occurred_at timestamptz NOT NULL DEFAULT now(),
        actor varchar(128),
        details jsonb NOT NULL DEFAULT '{}'::jsonb,
        CONSTRAINT pk_risk_event PRIMARY KEY (id),
        CONSTRAINT ck_risk_event_type CHECK (event_type ~ '^[A-Z][A-Za-z]{2,31}$'),
        CONSTRAINT ck_risk_event_details CHECK (jsonb_typeof(details) = 'object')
    );

    CREATE INDEX ix_risk_event_occurred_at ON meta.risk_event (occurred_at);

    -- ---------------------------------------------------------------- ref: reference data

    CREATE TABLE ref.country (
        code char(2) NOT NULL,
        name varchar(128) NOT NULL,
        CONSTRAINT pk_country PRIMARY KEY (code),
        CONSTRAINT ck_country_code CHECK (code ~ '^[A-Z]{2}$')
    );

    -- Single sector nomenclature (code list decided with #15).
    CREATE TABLE ref.sector (
        code varchar(16) NOT NULL,
        name varchar(256) NOT NULL,
        nomenclature varchar(16) NOT NULL,
        CONSTRAINT pk_sector PRIMARY KEY (code),
        CONSTRAINT ck_sector_code CHECK (code ~ '^[A-Z0-9][A-Z0-9.]{0,15}$')
    );

    -- Legal entities of the (fictitious) reporting bank: the RLS and ABAC scope dimension. The list
    -- itself is not filtered: it carries no exposure and the writer jobs enumerate it (#26).
    CREATE TABLE ref.legal_entity (
        id uuid NOT NULL,
        lei char(20) NOT NULL,
        code varchar(32) NOT NULL,
        name varchar(256) NOT NULL,
        country_code char(2) NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_legal_entity PRIMARY KEY (id),
        CONSTRAINT uq_legal_entity_lei UNIQUE (lei),
        CONSTRAINT uq_legal_entity_code UNIQUE (code),
        CONSTRAINT fk_legal_entity_country FOREIGN KEY (country_code) REFERENCES ref.country (code),
        CONSTRAINT ck_legal_entity_lei CHECK (lei ~ '^[A-Z0-9]{18}[0-9]{2}$')
    );

    CREATE TABLE ref.counterparty_group (
        id uuid NOT NULL,
        ultimate_parent_lei char(20) NOT NULL,
        name varchar(256) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_counterparty_group PRIMARY KEY (id),
        CONSTRAINT uq_counterparty_group_parent_lei UNIQUE (ultimate_parent_lei),
        CONSTRAINT fk_counterparty_group_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_counterparty_group_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_counterparty_group_lei CHECK (ultimate_parent_lei ~ '^[A-Z0-9]{18}[0-9]{2}$'),
        CONSTRAINT ck_counterparty_group_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    );

    -- Counterparties identified by their LEI (GLEIF, #15), public reference data (C0).
    CREATE TABLE ref.counterparty (
        id uuid NOT NULL,
        lei char(20) NOT NULL,
        legal_name varchar(256) NOT NULL,
        group_id uuid,
        country_code char(2),
        sector_code varchar(16),
        exposure_class varchar(32) NOT NULL,
        counterparty_type varchar(32),
        rating_internal varchar(8),
        rating_external varchar(8),
        watch_status varchar(16),
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_counterparty PRIMARY KEY (id),
        CONSTRAINT uq_counterparty_lei UNIQUE (lei),
        CONSTRAINT fk_counterparty_group FOREIGN KEY (group_id) REFERENCES ref.counterparty_group (id),
        CONSTRAINT fk_counterparty_country FOREIGN KEY (country_code) REFERENCES ref.country (code),
        CONSTRAINT fk_counterparty_sector FOREIGN KEY (sector_code) REFERENCES ref.sector (code),
        CONSTRAINT fk_counterparty_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_counterparty_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_counterparty_lei CHECK (lei ~ '^[A-Z0-9]{18}[0-9]{2}$'),
        CONSTRAINT ck_counterparty_exposure_class CHECK (exposure_class IN (
            'CentralGovernment', 'RegionalGovernment', 'PublicSectorEntity', 'MultilateralDevelopmentBank',
            'InternationalOrganisation', 'Institution', 'Corporate', 'InDefault')),
        CONSTRAINT ck_counterparty_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    );

    CREATE INDEX ix_counterparty_group ON ref.counterparty (group_id);

    CREATE TABLE ref.business_line (
        id uuid NOT NULL,
        code varchar(32) NOT NULL,
        name varchar(256) NOT NULL,
        CONSTRAINT pk_business_line PRIMARY KEY (id),
        CONSTRAINT uq_business_line_code UNIQUE (code)
    );

    CREATE TABLE ref.desk (
        id uuid NOT NULL,
        business_line_id uuid NOT NULL,
        code varchar(32) NOT NULL,
        name varchar(256) NOT NULL,
        CONSTRAINT pk_desk PRIMARY KEY (id),
        CONSTRAINT uq_desk_code UNIQUE (code),
        CONSTRAINT fk_desk_business_line FOREIGN KEY (business_line_id) REFERENCES ref.business_line (id)
    );

    CREATE TABLE ref.product_line (
        id uuid NOT NULL,
        code varchar(32) NOT NULL,
        name varchar(256) NOT NULL,
        asset_class varchar(16) NOT NULL,
        CONSTRAINT pk_product_line PRIMARY KEY (id),
        CONSTRAINT uq_product_line_code UNIQUE (code),
        CONSTRAINT ck_product_line_asset_class
            CHECK (asset_class IN ('InterestRate', 'ForeignExchange', 'Credit', 'Equity', 'Commodity'))
    );

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
    -- #11 - Portfolio (C1, RLS per legal entity), market data and benchmarks (C0, public).
    -- Dated facts are partitioned by month on as_of_date: the primary key always includes it.

    -- ---------------------------------------------------------------- portfolio

    CREATE TABLE portfolio.csa (
        id uuid NOT NULL,
        legal_entity_id uuid NOT NULL,
        collateral_status varchar(16) NOT NULL,
        currency char(3) NOT NULL,
        threshold numeric(28, 8) NOT NULL,
        minimum_transfer_amount numeric(28, 8) NOT NULL,
        independent_amount numeric(28, 8) NOT NULL,
        rounding numeric(28, 8) NOT NULL DEFAULT 0,
        margin_period_of_risk_days integer NOT NULL,
        has_initial_margin boolean NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_csa PRIMARY KEY (id),
        -- Target of the composite foreign keys that keep children in the same legal entity.
        CONSTRAINT uq_csa_legal_entity UNIQUE (id, legal_entity_id),
        CONSTRAINT fk_csa_legal_entity FOREIGN KEY (legal_entity_id) REFERENCES ref.legal_entity (id),
        CONSTRAINT fk_csa_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_csa_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_csa_collateral_status CHECK (collateral_status IN ('Uncollateralised', 'VmOnly', 'VmAndIm')),
        CONSTRAINT ck_csa_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_csa_amounts CHECK (threshold >= 0 AND minimum_transfer_amount >= 0 AND rounding >= 0),
        CONSTRAINT ck_csa_mpor CHECK (margin_period_of_risk_days BETWEEN 1 AND 365),
        CONSTRAINT ck_csa_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    );

    CREATE TABLE portfolio.netting_set (
        id uuid NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        agreement_type varchar(16) NOT NULL,
        clearing_mode varchar(16) NOT NULL,
        csa_id uuid,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_netting_set PRIMARY KEY (id),
        CONSTRAINT uq_netting_set_scope UNIQUE (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_netting_set_legal_entity FOREIGN KEY (legal_entity_id) REFERENCES ref.legal_entity (id),
        CONSTRAINT fk_netting_set_counterparty FOREIGN KEY (counterparty_id) REFERENCES ref.counterparty (id),
        -- The CSA belongs to the same legal entity as the netting set.
        CONSTRAINT fk_netting_set_csa FOREIGN KEY (csa_id, legal_entity_id) REFERENCES portfolio.csa (id, legal_entity_id),
        CONSTRAINT fk_netting_set_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_netting_set_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_netting_set_agreement_type CHECK (agreement_type IN ('Isda', 'Gmra')),
        CONSTRAINT ck_netting_set_clearing_mode CHECK (clearing_mode IN ('Bilateral', 'Qccp', 'NonQccp')),
        CONSTRAINT ck_netting_set_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    );

    CREATE INDEX ix_netting_set_legal_entity ON portfolio.netting_set (legal_entity_id);
    CREATE INDEX ix_netting_set_counterparty ON portfolio.netting_set (counterparty_id);

    -- Trade snapshot per as-of date.
    CREATE TABLE portfolio.trade (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        netting_set_id uuid NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        trade_type varchar(8) NOT NULL,
        asset_class varchar(16) NOT NULL,
        product_line_id uuid,
        business_line_id uuid NOT NULL,
        desk_id uuid NOT NULL,
        notional numeric(28, 8) NOT NULL,
        notional_currency char(3) NOT NULL,
        mtm numeric(28, 8) NOT NULL,
        mtm_currency char(3) NOT NULL,
        trade_date date NOT NULL,
        maturity_date date NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_trade PRIMARY KEY (as_of_date, id),
        -- Legal entity and counterparty are denormalised for RLS and indexing, and kept consistent
        -- with the netting set by this composite key.
        CONSTRAINT fk_trade_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_trade_product_line FOREIGN KEY (product_line_id) REFERENCES ref.product_line (id),
        CONSTRAINT fk_trade_business_line FOREIGN KEY (business_line_id) REFERENCES ref.business_line (id),
        CONSTRAINT fk_trade_desk FOREIGN KEY (desk_id) REFERENCES ref.desk (id),
        CONSTRAINT fk_trade_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_trade_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_trade_type CHECK (trade_type IN ('Otc', 'Etd', 'Sft')),
        CONSTRAINT ck_trade_asset_class
            CHECK (asset_class IN ('InterestRate', 'ForeignExchange', 'Credit', 'Equity', 'Commodity')),
        CONSTRAINT ck_trade_currencies CHECK (notional_currency ~ '^[A-Z]{3}$' AND mtm_currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_trade_notional CHECK (notional >= 0),
        CONSTRAINT ck_trade_dates CHECK (maturity_date >= trade_date),
        CONSTRAINT ck_trade_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    CREATE INDEX ix_trade_as_of_netting_set ON portfolio.trade (as_of_date, netting_set_id);
    CREATE INDEX ix_trade_as_of_counterparty ON portfolio.trade (as_of_date, counterparty_id);
    CREATE INDEX ix_trade_as_of_legal_entity ON portfolio.trade (as_of_date, legal_entity_id);

    CREATE TABLE portfolio.collateral_position (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        csa_id uuid NOT NULL,
        netting_set_id uuid NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        direction varchar(16) NOT NULL,
        margin_type varchar(24) NOT NULL,
        market_value numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        haircut numeric(9, 8) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_collateral_position PRIMARY KEY (as_of_date, id),
        CONSTRAINT fk_collateral_position_csa FOREIGN KEY (csa_id, legal_entity_id) REFERENCES portfolio.csa (id, legal_entity_id),
        CONSTRAINT fk_collateral_position_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_collateral_position_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_collateral_position_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_collateral_position_direction CHECK (direction IN ('Received', 'Posted')),
        CONSTRAINT ck_collateral_position_margin_type
            CHECK (margin_type IN ('VariationMargin', 'InitialMargin', 'IndependentAmount')),
        CONSTRAINT ck_collateral_position_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_collateral_position_market_value CHECK (market_value >= 0),
        CONSTRAINT ck_collateral_position_haircut CHECK (haircut >= 0 AND haircut < 1),
        CONSTRAINT ck_collateral_position_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    CREATE INDEX ix_collateral_position_as_of_netting_set ON portfolio.collateral_position (as_of_date, netting_set_id);
    CREATE INDEX ix_collateral_position_as_of_counterparty ON portfolio.collateral_position (as_of_date, counterparty_id);
    CREATE INDEX ix_collateral_position_as_of_legal_entity ON portfolio.collateral_position (as_of_date, legal_entity_id);

    -- ---------------------------------------------------------------- market (ECB, #16)

    CREATE TABLE market.fx_rate (
        as_of_date date NOT NULL,
        base_ccy char(3) NOT NULL,
        quote_ccy char(3) NOT NULL,
        rate numeric(28, 10) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64) NOT NULL,
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_fx_rate PRIMARY KEY (as_of_date, base_ccy, quote_ccy),
        CONSTRAINT fk_fx_rate_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_fx_rate_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_fx_rate_currencies CHECK (base_ccy ~ '^[A-Z]{3}$' AND quote_ccy ~ '^[A-Z]{3}$' AND base_ccy <> quote_ccy),
        CONSTRAINT ck_fx_rate_rate CHECK (rate > 0),
        CONSTRAINT ck_fx_rate_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    CREATE TABLE market.yield_curve_point (
        as_of_date date NOT NULL,
        curve_id varchar(64) NOT NULL,
        tenor_months integer NOT NULL,
        rate numeric(18, 10) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64) NOT NULL,
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_yield_curve_point PRIMARY KEY (as_of_date, curve_id, tenor_months),
        CONSTRAINT fk_yield_curve_point_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_yield_curve_point_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_yield_curve_point_tenor CHECK (tenor_months BETWEEN 1 AND 600),
        CONSTRAINT ck_yield_curve_point_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    -- Svensson parameters published by the ECB (BETA0..BETA3, TAU1, TAU2).
    CREATE TABLE market.yield_curve_param (
        as_of_date date NOT NULL,
        curve_id varchar(64) NOT NULL,
        beta0 numeric(18, 10) NOT NULL,
        beta1 numeric(18, 10) NOT NULL,
        beta2 numeric(18, 10) NOT NULL,
        beta3 numeric(18, 10) NOT NULL,
        tau1 numeric(18, 10) NOT NULL,
        tau2 numeric(18, 10) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64) NOT NULL,
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_yield_curve_param PRIMARY KEY (as_of_date, curve_id),
        CONSTRAINT fk_yield_curve_param_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_yield_curve_param_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_yield_curve_param_tau CHECK (tau1 > 0 AND tau2 > 0),
        CONSTRAINT ck_yield_curve_param_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    -- ---------------------------------------------------------------- bench (published benchmarks, #17)

    CREATE TABLE bench.benchmark_observation (
        id uuid NOT NULL,
        source_id varchar(64) NOT NULL,
        indicator_code varchar(64) NOT NULL,
        reporter varchar(128) NOT NULL,
        period varchar(16) NOT NULL,
        value numeric(28, 8) NOT NULL,
        unit varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_benchmark_observation PRIMARY KEY (id),
        CONSTRAINT uq_benchmark_observation UNIQUE (source_id, indicator_code, reporter, period),
        CONSTRAINT fk_benchmark_observation_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_benchmark_observation_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_benchmark_observation_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    );

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
    -- #11 - Calculation results (C1, RLS per legal entity). Every result carries its method version
    -- and its lineage (NOT NULL, TM-TB4-07) and is computed per netting set where the measure is
    -- (PFE, EAD, EEPE). Partitioned by month on as_of_date; BRIN on as_of_date for large scans.

    CREATE TABLE calc.exposure_result (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        netting_set_id uuid NOT NULL,
        exposure_level varchar(24) NOT NULL,
        measure_code varchar(40) NOT NULL,
        amount numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        method_version_id varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_exposure_result PRIMARY KEY (as_of_date, id),
        CONSTRAINT fk_exposure_result_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_exposure_result_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
        CONSTRAINT fk_exposure_result_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_exposure_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_exposure_result_level CHECK (exposure_level IN (
            'Notional', 'GrossBeforeCrm', 'AfterNetting', 'AfterCollateral', 'Ead', 'Drawn', 'Undrawn',
            'TotalCommitment', 'LargeExposureValue', 'RiskWeighted', 'ValuationAdjustment')),
        CONSTRAINT ck_exposure_result_measure CHECK (measure_code IN (
            'GrossNotional', 'GrossPositiveMtm', 'GrossSftExposure', 'NetExposure', 'NetCollateralisedExposure',
            'ReplacementCost', 'SaCcrPfeAddOn', 'ExposureAtDefault', 'RiskWeightedExposure', 'ExpectedExposure',
            'PotentialFutureExposure', 'ExpectedPositiveExposure', 'EffectiveExpectedPositiveExposure',
            'ExpectedNegativeExposure', 'CreditValuationAdjustment', 'DebitValuationAdjustment', 'Drawn',
            'Undrawn', 'TotalCommitment', 'LargeExposureValue')),
        CONSTRAINT ck_exposure_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_exposure_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    CREATE TABLE calc.saccr_result (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        netting_set_id uuid NOT NULL,
        regulatory_approach varchar(16) NOT NULL,
        replacement_cost numeric(28, 8) NOT NULL,
        aggregate_add_on numeric(28, 8) NOT NULL,
        pfe_multiplier numeric(18, 10) NOT NULL,
        potential_future_exposure numeric(28, 8) NOT NULL,
        exposure_at_default numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        method_version_id varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_saccr_result PRIMARY KEY (as_of_date, id),
        CONSTRAINT uq_saccr_result_netting_set UNIQUE (as_of_date, netting_set_id, method_version_id),
        CONSTRAINT fk_saccr_result_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_saccr_result_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
        CONSTRAINT fk_saccr_result_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_saccr_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_saccr_result_approach
            CHECK (regulatory_approach IN ('SaCcr', 'ImmSimulated', 'CemHistorical', 'SaCreditRisk', 'Fccm')),
        CONSTRAINT ck_saccr_result_amounts CHECK (replacement_cost >= 0 AND aggregate_add_on >= 0
            AND potential_future_exposure >= 0 AND exposure_at_default >= 0),
        CONSTRAINT ck_saccr_result_multiplier CHECK (pfe_multiplier > 0 AND pfe_multiplier <= 1),
        CONSTRAINT ck_saccr_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_saccr_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    -- Exposure profile (EE, PFE, ENE...) per horizon in calendar days (ExposureHorizonGrid, 30 years cap).
    CREATE TABLE calc.profile_point (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        netting_set_id uuid NOT NULL,
        measure_code varchar(40) NOT NULL,
        horizon_days integer NOT NULL,
        quantile numeric(6, 5),
        amount numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        method_version_id varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_profile_point PRIMARY KEY (as_of_date, id),
        CONSTRAINT fk_profile_point_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_profile_point_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
        CONSTRAINT fk_profile_point_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_profile_point_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_profile_point_measure CHECK (measure_code IN (
            'ExpectedExposure', 'PotentialFutureExposure', 'ExpectedPositiveExposure',
            'EffectiveExpectedPositiveExposure', 'ExpectedNegativeExposure')),
        CONSTRAINT ck_profile_point_horizon CHECK (horizon_days BETWEEN 0 AND 10950),
        CONSTRAINT ck_profile_point_quantile CHECK (quantile IS NULL OR (quantile > 0.5 AND quantile < 1)),
        CONSTRAINT ck_profile_point_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_profile_point_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    CREATE TABLE calc.rwa_result (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        netting_set_id uuid NOT NULL,
        regulatory_approach varchar(16) NOT NULL,
        exposure_class varchar(32) NOT NULL,
        exposure_at_default numeric(28, 8) NOT NULL,
        risk_weight numeric(18, 10) NOT NULL,
        risk_weighted_amount numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        method_version_id varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_rwa_result PRIMARY KEY (as_of_date, id),
        CONSTRAINT fk_rwa_result_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_rwa_result_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
        CONSTRAINT fk_rwa_result_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_rwa_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_rwa_result_approach
            CHECK (regulatory_approach IN ('SaCcr', 'ImmSimulated', 'CemHistorical', 'SaCreditRisk', 'Fccm')),
        CONSTRAINT ck_rwa_result_exposure_class CHECK (exposure_class IN (
            'CentralGovernment', 'RegionalGovernment', 'PublicSectorEntity', 'MultilateralDevelopmentBank',
            'InternationalOrganisation', 'Institution', 'Corporate', 'InDefault')),
        CONSTRAINT ck_rwa_result_amounts CHECK (exposure_at_default >= 0 AND risk_weight >= 0 AND risk_weighted_amount >= 0),
        CONSTRAINT ck_rwa_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_rwa_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    -- CVA is computed per counterparty (all its netting sets): netting_set_id is optional here.
    CREATE TABLE calc.cva_result (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        netting_set_id uuid,
        cva_approach varchar(16) NOT NULL,
        measure_code varchar(40) NOT NULL,
        amount numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        method_version_id varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_cva_result PRIMARY KEY (as_of_date, id),
        CONSTRAINT fk_cva_result_legal_entity FOREIGN KEY (legal_entity_id) REFERENCES ref.legal_entity (id),
        CONSTRAINT fk_cva_result_counterparty FOREIGN KEY (counterparty_id) REFERENCES ref.counterparty (id),
        CONSTRAINT fk_cva_result_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_cva_result_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
        CONSTRAINT fk_cva_result_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_cva_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_cva_result_approach CHECK (cva_approach IN ('ReducedBaCva', 'FullBaCva')),
        CONSTRAINT ck_cva_result_measure
            CHECK (measure_code IN ('CreditValuationAdjustment', 'DebitValuationAdjustment')),
        CONSTRAINT ck_cva_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_cva_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    -- Allocation of a netting set figure to its trades with the documented rule (ADR-002 §5).
    -- No foreign key to the partitioned trade table: both are purged partition by partition.
    CREATE TABLE calc.allocation_result (
        id uuid NOT NULL,
        as_of_date date NOT NULL,
        legal_entity_id uuid NOT NULL,
        counterparty_id uuid NOT NULL,
        netting_set_id uuid NOT NULL,
        trade_id uuid NOT NULL,
        business_line_id uuid NOT NULL,
        measure_code varchar(40) NOT NULL,
        allocation_rule varchar(16) NOT NULL,
        allocated_amount numeric(28, 8) NOT NULL,
        currency char(3) NOT NULL,
        method_version_id varchar(32) NOT NULL,
        lineage varchar(16) NOT NULL,
        source_id varchar(64),
        ingestion_run_id uuid,
        created_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_allocation_result PRIMARY KEY (as_of_date, id),
        CONSTRAINT fk_allocation_result_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
            REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
        CONSTRAINT fk_allocation_result_business_line FOREIGN KEY (business_line_id) REFERENCES ref.business_line (id),
        CONSTRAINT fk_allocation_result_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
        CONSTRAINT fk_allocation_result_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
        CONSTRAINT fk_allocation_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
        CONSTRAINT ck_allocation_result_measure CHECK (measure_code IN (
            'GrossNotional', 'GrossPositiveMtm', 'GrossSftExposure', 'NetExposure', 'NetCollateralisedExposure',
            'ReplacementCost', 'SaCcrPfeAddOn', 'ExposureAtDefault', 'RiskWeightedExposure', 'ExpectedExposure',
            'PotentialFutureExposure', 'ExpectedPositiveExposure', 'EffectiveExpectedPositiveExposure',
            'ExpectedNegativeExposure', 'CreditValuationAdjustment', 'DebitValuationAdjustment', 'Drawn',
            'Undrawn', 'TotalCommitment', 'LargeExposureValue')),
        CONSTRAINT ck_allocation_result_rule CHECK (allocation_rule IN ('Euler', 'ProRata')),
        CONSTRAINT ck_allocation_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
        CONSTRAINT ck_allocation_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
    ) PARTITION BY RANGE (as_of_date);

    -- Access paths shared by every calc table: (as_of_date, netting set / counterparty / legal entity)
    -- B-tree, plus BRIN on as_of_date for range scans of large partitions.
    DO $idx$
    DECLARE
        v_table text;
    BEGIN
        FOREACH v_table IN ARRAY ARRAY['exposure_result', 'saccr_result', 'profile_point', 'rwa_result', 'cva_result', 'allocation_result']
        LOOP
            EXECUTE format('CREATE INDEX %I ON calc.%I (as_of_date, netting_set_id)', 'ix_' || v_table || '_as_of_netting_set', v_table);
            EXECUTE format('CREATE INDEX %I ON calc.%I (as_of_date, counterparty_id)', 'ix_' || v_table || '_as_of_counterparty', v_table);
            EXECUTE format('CREATE INDEX %I ON calc.%I (as_of_date, legal_entity_id)', 'ix_' || v_table || '_as_of_legal_entity', v_table);
            EXECUTE format('CREATE INDEX %I ON calc.%I USING brin (as_of_date)', 'brin_' || v_table || '_as_of_date', v_table);
        END LOOP;
    END
    $idx$;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
    -- #11 - Platform tables that the service identities of data-flow.md are granted on.
    -- Minimal structures: their owners (#10 audit, #9 BFF session and key ring, #31 API key ring)
    -- extend them with their own migrations.

    -- Audit trail (C1-PD). INSERT-only, hash chain computed by the application with a key held
    -- outside PostgreSQL (#10); the trigger rejects any UPDATE or DELETE, whatever the grants.
    CREATE TABLE audit.audit_event (
        seq bigint GENERATED ALWAYS AS IDENTITY,
        occurred_at timestamptz NOT NULL DEFAULT now(),
        event_type varchar(64) NOT NULL,
        actor varchar(128),
        payload jsonb NOT NULL,
        prev_hash bytea,
        hash bytea NOT NULL,
        CONSTRAINT pk_audit_event PRIMARY KEY (seq),
        CONSTRAINT ck_audit_event_payload CHECK (jsonb_typeof(payload) = 'object'),
        CONSTRAINT ck_audit_event_hash CHECK (octet_length(hash) = 32)
    );

    CREATE OR REPLACE FUNCTION audit.reject_change()
    RETURNS trigger
    LANGUAGE plpgsql
    SET search_path = pg_catalog, pg_temp
    AS $fn$
    BEGIN
        RAISE EXCEPTION 'audit.audit_event is append-only' USING ERRCODE = '42501';
    END
    $fn$;

    REVOKE ALL ON FUNCTION audit.reject_change() FROM PUBLIC;

    CREATE TRIGGER trg_audit_event_append_only
        BEFORE UPDATE OR DELETE ON audit.audit_event
        FOR EACH ROW EXECUTE FUNCTION audit.reject_change();

    CREATE TRIGGER trg_audit_event_no_truncate
        BEFORE TRUNCATE ON audit.audit_event
        FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_change();

    -- Data Protection key rings (C2): one table per application, one role per table (TM-TB4-08).
    -- Shape of Microsoft.AspNetCore.DataProtection.EntityFrameworkCore (Id, FriendlyName, Xml).
    CREATE TABLE dpkeys.api_key (
        id integer GENERATED BY DEFAULT AS IDENTITY,
        friendly_name text,
        xml text,
        CONSTRAINT pk_api_key PRIMARY KEY (id)
    );

    CREATE TABLE dpkeys.bff_key (
        id integer GENERATED BY DEFAULT AS IDENTITY,
        friendly_name text,
        xml text,
        CONSTRAINT pk_bff_key PRIMARY KEY (id)
    );

    -- Server-side authentication tickets of the BFF (C2, encrypted by the application, #9).
    CREATE TABLE session.ticket (
        id varchar(128) NOT NULL,
        value bytea NOT NULL,
        expires_at timestamptz NOT NULL,
        last_activity_at timestamptz NOT NULL DEFAULT now(),
        CONSTRAINT pk_ticket PRIMARY KEY (id)
    );

    CREATE INDEX ix_ticket_expires_at ON session.ticket (expires_at);

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
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

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM meta.ef_migrations_history WHERE "MigrationId" = '20261125090000_InitialSchema') THEN
    INSERT INTO meta.ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20261125090000_InitialSchema', '10.0.15');
    END IF;
END $EF$;
COMMIT;

