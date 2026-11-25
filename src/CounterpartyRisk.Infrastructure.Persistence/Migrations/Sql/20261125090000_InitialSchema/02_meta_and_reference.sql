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
