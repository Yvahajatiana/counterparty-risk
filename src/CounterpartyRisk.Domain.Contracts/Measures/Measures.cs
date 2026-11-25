namespace CounterpartyRisk.Domain.Contracts.Measures;

// Closed vocabularies of the dashboard. Every displayed amount carries its family, nature and
// level (CLAUDE.md domain rules, ADR-002 §1-§2). Enums are serialised as strings in the API.

/// <summary>
/// Stage of the mitigation chain an amount belongs to (ADR-002 §1). A level describes a stage,
/// never an instrument: the instrument-specific figure is the <see cref="MeasureCode"/>.
/// </summary>
public enum ExposureLevel
{
    /// <summary>Gross notional, a volume indicator and not an exposure.</summary>
    Notional = 1,

    /// <summary>"Gross exposure before CRM (before netting and collateral)".</summary>
    GrossBeforeCrm = 2,

    AfterNetting = 3,

    AfterCollateral = 4,

    /// <summary>Regulatory exposure at default.</summary>
    Ead = 5,

    /// <summary>Financing: outstanding principal plus accrued interest.</summary>
    Drawn = 6,

    /// <summary>Financing: committed limit minus drawn principal, floored at 0.</summary>
    Undrawn = 7,

    /// <summary>Financing: drawn plus undrawn.</summary>
    TotalCommitment = 8,

    /// <summary>Exposure value for the large exposures regime (CRR Part Four).</summary>
    LargeExposureValue = 9,

    /// <summary>Risk-weighted exposure amount, the stage after the EAD.</summary>
    RiskWeighted = 10,

    /// <summary>CVA and DVA: a valuation adjustment, not a mitigation stage.</summary>
    ValuationAdjustment = 11,
}

/// <summary>
/// Family of a measure. Economic and regulatory measures are never mixed in one chart or one sum.
/// </summary>
public enum MeasureFamily
{
    /// <summary>
    /// Current exposure at market value: notional, gross positive MtM, after netting, after
    /// collateral. Neither simulated nor regulatory.
    /// </summary>
    CurrentExposure = 1,

    /// <summary>Management measures from the simulation: EE, PFE, EPE, ENE.</summary>
    Economic = 2,

    /// <summary>Capital measures: RC, SA-CCR add-on, EAD, Effective EPE, RWA, large exposures.</summary>
    Regulatory = 3,

    /// <summary>Book values and valuation adjustments: drawn, undrawn, total commitment, CVA, DVA.</summary>
    Accounting = 4,
}

/// <summary>
/// Origin of a figure. A published benchmark (BIS, EBA Pillar 3, FR Y-9C) keeps its measure
/// family (a Pillar 3 EAD is still an EAD) but is never summed with recomputed bank figures.
/// </summary>
public enum DataOrigin
{
    Recomputed = 1,

    PublishedBenchmark = 2,
}

/// <summary>Nature of an amount inside the Gross Commitment section (ADR-002 §1).</summary>
public enum ExposureNature
{
    /// <summary>Counterparty credit risk: OTC and exchange-traded derivatives, SFTs.</summary>
    Ccr = 1,

    /// <summary>Loans, credit lines and guarantees.</summary>
    Financing = 2,
}

/// <summary>Method that produced a regulatory or economic figure.</summary>
public enum RegulatoryApproach
{
    /// <summary>Standardised approach for counterparty credit risk (CRR art. 274), derivatives.</summary>
    SaCcr = 1,

    /// <summary>Internal Monte-Carlo simulation, labelled "IMM simulated": no supervisory approval.</summary>
    ImmSimulated = 2,

    /// <summary>Basel I current exposure method, kept for historical annotations only (ADR-002 §3).</summary>
    CemHistorical = 3,

    /// <summary>Standardised approach for credit risk (CRR art. 111), financing.</summary>
    SaCreditRisk = 4,

    /// <summary>Financial collateral comprehensive method (CRR art. 223), SFTs.</summary>
    Fccm = 5,
}

/// <summary>
/// Code of the figure being shown. Each code belongs to exactly one family (see
/// <see cref="MeasureCatalog"/>).
/// </summary>
public enum MeasureCode
{
    /// <summary>Σ |N_i|.</summary>
    GrossNotional = 1,

    /// <summary>Derivatives, Σ max(V_i, 0) (BIS "gross positive market value").</summary>
    GrossPositiveMtm = 2,

    /// <summary>SFTs, Σ E_i (cash or securities lent before collateral received).</summary>
    GrossSftExposure = 3,

    /// <summary>Exposure after netting, before collateral.</summary>
    NetExposure = 4,

    /// <summary>Exposure after netting and collateral (current exposure).</summary>
    NetCollateralisedExposure = 5,

    /// <summary>SA-CCR replacement cost (CRR art. 275).</summary>
    ReplacementCost = 6,

    /// <summary>SA-CCR potential future exposure add-on (CRR art. 278), not the economic PFE.</summary>
    SaCcrPfeAddOn = 7,

    ExposureAtDefault = 8,

    RiskWeightedExposure = 9,

    ExpectedExposure = 10,

    /// <summary>Economic PFE at the quantile of the active method version.</summary>
    PotentialFutureExposure = 11,

    ExpectedPositiveExposure = 12,

    EffectiveExpectedPositiveExposure = 13,

    ExpectedNegativeExposure = 14,

    CreditValuationAdjustment = 15,

    DebitValuationAdjustment = 16,

    Drawn = 17,

    Undrawn = 18,

    TotalCommitment = 19,

    LargeExposureValue = 20,
}
