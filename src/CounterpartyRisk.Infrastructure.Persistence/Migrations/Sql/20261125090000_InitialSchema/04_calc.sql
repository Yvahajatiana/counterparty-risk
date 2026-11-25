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
    is_margined boolean NOT NULL,
    -- V - C and NICA inputs of the replacement cost (CRR art. 275).
    v_minus_c numeric(28, 8) NOT NULL,
    nica numeric(28, 8) NOT NULL,
    replacement_cost numeric(28, 8) NOT NULL,
    aggregate_add_on numeric(28, 8) NOT NULL,
    pfe_multiplier numeric(18, 10) NOT NULL,
    potential_future_exposure numeric(28, 8) NOT NULL,
    -- Alpha is stored, not assumed: 1.4 by default, 1 for some non-financial counterparties.
    alpha numeric(6, 4) NOT NULL,
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
    CONSTRAINT ck_saccr_result_alpha CHECK (alpha > 0 AND alpha <= 1.4),
    CONSTRAINT ck_saccr_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_saccr_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
) PARTITION BY RANGE (as_of_date);

-- SA-CCR add-on per asset class of a netting set (child of saccr_result, same keys and partitions).
-- No foreign key to the partitioned parent: both are purged partition by partition.
CREATE TABLE calc.saccr_addon (
    id uuid NOT NULL,
    as_of_date date NOT NULL,
    saccr_result_id uuid NOT NULL,
    legal_entity_id uuid NOT NULL,
    counterparty_id uuid NOT NULL,
    netting_set_id uuid NOT NULL,
    asset_class varchar(16) NOT NULL,
    addon numeric(28, 8) NOT NULL,
    currency char(3) NOT NULL,
    method_version_id varchar(32) NOT NULL,
    lineage varchar(16) NOT NULL,
    source_id varchar(64),
    ingestion_run_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_saccr_addon PRIMARY KEY (as_of_date, id),
    CONSTRAINT uq_saccr_addon UNIQUE (as_of_date, saccr_result_id, asset_class),
    CONSTRAINT fk_saccr_addon_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
        REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
    CONSTRAINT fk_saccr_addon_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
    CONSTRAINT fk_saccr_addon_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
    CONSTRAINT fk_saccr_addon_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
    -- Codes of the Expert CCR (Fx, not the ForeignExchange name of SaCcrAssetClass).
    CONSTRAINT ck_saccr_addon_asset_class
        CHECK (asset_class IN ('InterestRate', 'Fx', 'Credit', 'Equity', 'Commodity')),
    CONSTRAINT ck_saccr_addon_amount CHECK (addon >= 0),
    CONSTRAINT ck_saccr_addon_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_saccr_addon_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
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

-- CVA capital, reduced BA-CVA (MAR50). ASSUMPTION: BA-CVA versus SA-CVA is still open in the EDB;
-- these tables follow the reduced BA-CVA and will be revisited with that decision. CS01 (economic
-- CVA sensitivities) is out of scope here (#24). Three levels, because K_reduced is not additive:
-- counterparty (SCVA), netting set (M, EAD, DF) and portfolio of the legal entity (K_reduced).
CREATE TABLE calc.cva_result (
    id uuid NOT NULL,
    as_of_date date NOT NULL,
    legal_entity_id uuid NOT NULL,
    counterparty_id uuid NOT NULL,
    cva_approach varchar(16) NOT NULL,
    -- MAR50 sector bucket, distinct from ref.counterparty.counterparty_type.
    cva_sector_bucket varchar(24) NOT NULL,
    credit_quality varchar(8) NOT NULL,
    risk_weight numeric(18, 10) NOT NULL,
    scva numeric(28, 8) NOT NULL,
    currency char(3) NOT NULL,
    method_version_id varchar(32) NOT NULL,
    lineage varchar(16) NOT NULL,
    source_id varchar(64),
    ingestion_run_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_cva_result PRIMARY KEY (as_of_date, id),
    CONSTRAINT uq_cva_result_counterparty UNIQUE (as_of_date, legal_entity_id, counterparty_id, method_version_id),
    CONSTRAINT fk_cva_result_legal_entity FOREIGN KEY (legal_entity_id) REFERENCES ref.legal_entity (id),
    CONSTRAINT fk_cva_result_counterparty FOREIGN KEY (counterparty_id) REFERENCES ref.counterparty (id),
    CONSTRAINT fk_cva_result_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
    CONSTRAINT fk_cva_result_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
    CONSTRAINT fk_cva_result_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
    CONSTRAINT ck_cva_result_approach CHECK (cva_approach IN ('ReducedBaCva', 'FullBaCva')),
    CONSTRAINT ck_cva_result_sector_bucket CHECK (cva_sector_bucket IN (
        'Sovereign', 'LocalGovernment', 'Financial', 'BasicMaterials', 'ConsumerGoods', 'Technology',
        'HealthCare', 'Other')),
    CONSTRAINT ck_cva_result_credit_quality CHECK (credit_quality IN ('IG', 'HYNR')),
    CONSTRAINT ck_cva_result_risk_weight CHECK (risk_weight >= 0 AND risk_weight <= 1),
    CONSTRAINT ck_cva_result_scva CHECK (scva >= 0),
    CONSTRAINT ck_cva_result_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_cva_result_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
) PARTITION BY RANGE (as_of_date);

-- Netting set inputs of SCVA: effective maturity M, EAD and supervisory discount factor DF.
CREATE TABLE calc.cva_netting_set (
    id uuid NOT NULL,
    as_of_date date NOT NULL,
    cva_result_id uuid NOT NULL,
    legal_entity_id uuid NOT NULL,
    counterparty_id uuid NOT NULL,
    netting_set_id uuid NOT NULL,
    m_ns numeric(18, 10) NOT NULL,
    ead_ns numeric(28, 8) NOT NULL,
    df_ns numeric(18, 10) NOT NULL,
    currency char(3) NOT NULL,
    method_version_id varchar(32) NOT NULL,
    lineage varchar(16) NOT NULL,
    source_id varchar(64),
    ingestion_run_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_cva_netting_set PRIMARY KEY (as_of_date, id),
    CONSTRAINT uq_cva_netting_set UNIQUE (as_of_date, cva_result_id, netting_set_id),
    CONSTRAINT fk_cva_netting_set_netting_set FOREIGN KEY (netting_set_id, legal_entity_id, counterparty_id)
        REFERENCES portfolio.netting_set (id, legal_entity_id, counterparty_id),
    CONSTRAINT fk_cva_netting_set_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
    CONSTRAINT fk_cva_netting_set_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
    CONSTRAINT fk_cva_netting_set_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
    CONSTRAINT ck_cva_netting_set_values CHECK (m_ns >= 0 AND ead_ns >= 0 AND df_ns > 0 AND df_ns <= 1),
    CONSTRAINT ck_cva_netting_set_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_cva_netting_set_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
) PARTITION BY RANGE (as_of_date);

-- Portfolio level of a legal entity: K_reduced with its correlation rho and discount scalar DS.
CREATE TABLE calc.cva_portfolio (
    id uuid NOT NULL,
    as_of_date date NOT NULL,
    legal_entity_id uuid NOT NULL,
    cva_approach varchar(16) NOT NULL,
    k_reduced numeric(28, 8) NOT NULL,
    rho numeric(6, 5) NOT NULL,
    ds numeric(6, 5) NOT NULL,
    own_funds numeric(28, 8) NOT NULL,
    currency char(3) NOT NULL,
    method_version_id varchar(32) NOT NULL,
    lineage varchar(16) NOT NULL,
    source_id varchar(64),
    ingestion_run_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_cva_portfolio PRIMARY KEY (as_of_date, id),
    CONSTRAINT uq_cva_portfolio UNIQUE (as_of_date, legal_entity_id, method_version_id),
    CONSTRAINT fk_cva_portfolio_legal_entity FOREIGN KEY (legal_entity_id) REFERENCES ref.legal_entity (id),
    CONSTRAINT fk_cva_portfolio_method FOREIGN KEY (method_version_id) REFERENCES meta.method_version (id),
    CONSTRAINT fk_cva_portfolio_source FOREIGN KEY (source_id) REFERENCES meta.data_source (id),
    CONSTRAINT fk_cva_portfolio_run FOREIGN KEY (ingestion_run_id) REFERENCES meta.ingestion_run (id),
    CONSTRAINT ck_cva_portfolio_approach CHECK (cva_approach IN ('ReducedBaCva', 'FullBaCva')),
    CONSTRAINT ck_cva_portfolio_values CHECK (k_reduced >= 0 AND own_funds >= 0 AND rho BETWEEN 0 AND 1 AND ds > 0 AND ds <= 1),
    CONSTRAINT ck_cva_portfolio_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_cva_portfolio_lineage CHECK (lineage IN ('Real', 'Transformed', 'Synthetic'))
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

-- Access paths shared by the calc tables: (as_of_date, netting set / counterparty / legal entity)
-- B-tree where the column exists, plus BRIN on as_of_date for range scans of large partitions.
DO $idx$
DECLARE
    v_table text;
    v_column text;
BEGIN
    FOREACH v_table IN ARRAY ARRAY['exposure_result', 'saccr_result', 'saccr_addon', 'profile_point', 'rwa_result',
                                   'cva_result', 'cva_netting_set', 'cva_portfolio', 'allocation_result']
    LOOP
        FOREACH v_column IN ARRAY ARRAY['netting_set_id', 'counterparty_id', 'legal_entity_id']
        LOOP
            IF EXISTS (SELECT 1 FROM pg_catalog.pg_attribute
                       WHERE attrelid = format('calc.%I', v_table)::regclass AND attname = v_column AND NOT attisdropped) THEN
                EXECUTE format('CREATE INDEX %I ON calc.%I (as_of_date, %I)',
                    'ix_' || v_table || '_as_of_' || replace(v_column, '_id', ''), v_table, v_column);
            END IF;
        END LOOP;
        EXECUTE format('CREATE INDEX %I ON calc.%I USING brin (as_of_date)', 'brin_' || v_table || '_as_of_date', v_table);
    END LOOP;
END
$idx$;