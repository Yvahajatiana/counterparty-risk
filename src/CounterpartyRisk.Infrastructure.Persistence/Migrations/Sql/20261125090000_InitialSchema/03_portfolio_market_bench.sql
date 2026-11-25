-- #11 - Portfolio (C1, RLS per legal entity), market data and benchmarks (C0, public).
-- Dated facts are partitioned by month on as_of_date: the primary key always includes it.

-- ---------------------------------------------------------------- portfolio
-- Collateral: market value >= 0, the sign is carried by direction (received / posted), haircut in
-- [0, 1) (Expert CCR).

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
    -- Netting agreement (CRR art. 274(1), 296); None: an uncovered trade is its own netting set.
    agreement_type varchar(16) NOT NULL,
    -- Margin agreement, distinct from netting: a CSA margins, it does not net.
    margin_agreement varchar(8) NOT NULL,
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
    CONSTRAINT ck_netting_set_agreement_type
        CHECK (agreement_type IN ('Isda', 'Gmra', 'Gmsla', 'Mra', 'Msla', 'CcpRulebook', 'None')),
    CONSTRAINT ck_netting_set_margin_agreement CHECK (margin_agreement IN ('Csa', 'None')),
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
    is_segregated boolean NOT NULL DEFAULT false,
    -- Asset type of the collateral (cash, bonds...): code list to be fixed with #18.
    collateral_asset_type varchar(32) NOT NULL,
    -- Initial margin computed with ISDA SIMM.
    is_simm boolean NOT NULL DEFAULT false,
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
