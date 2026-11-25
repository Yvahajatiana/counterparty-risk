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
