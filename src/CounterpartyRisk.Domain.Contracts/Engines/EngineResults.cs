using CounterpartyRisk.Domain.Contracts.Identifiers;
using CounterpartyRisk.Domain.Contracts.Measures;
using CounterpartyRisk.Domain.Contracts.Methodology;
using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.Portfolio;

namespace CounterpartyRisk.Domain.Contracts.Engines;

// Engine outputs (v0). Amounts are in the reporting currency; every result names the method
// version that produced it. Fields may be added; removing or renaming one is a breaking change.

/// <summary>
/// W2 current exposure waterfall of one netting set: notional, gross positive MtM, netting,
/// collateral. It ends at the net collateralised exposure; the SA-CCR replacement cost is a
/// regulatory figure and belongs to <see cref="SaCcrResult"/> (W5).
/// </summary>
public sealed record ExposureWaterfall(
    NettingSetId NettingSetId,
    Money GrossNotional,
    Money GrossBeforeCrm,
    Money NettingBenefit,
    Money NetExposure,
    Money CollateralReceivedNet,
    Money NetCollateralisedExposure);

/// <summary>SA-CCR EAD of one netting set: alpha x (RC + multiplier x add-on), CRR art. 274.</summary>
public sealed record SaCcrResult(
    NettingSetId NettingSetId,
    Money ReplacementCost,
    IReadOnlyDictionary<SaCcrAssetClass, Money> AddOnsByAssetClass,
    Money AggregateAddOn,
    decimal Multiplier,
    decimal Alpha,
    Money Ead);

public sealed record ExposureProfilePoint(int HorizonDays, Money ExpectedExposure, Money PotentialFutureExposure, Money ExpectedNegativeExposure);

/// <summary>Simulated profile ("IMM simulated", not approved by a supervisor).</summary>
public sealed record ExposureProfile(
    NettingSetId NettingSetId,
    MethodVersionId MethodVersion,
    decimal Quantile,
    IReadOnlyList<ExposureProfilePoint> Points,
    Money ExpectedPositiveExposure,
    Money EffectiveExpectedPositiveExposure,
    Money StressedEffectiveExpectedPositiveExposure);

public sealed record NettingSetEad(NettingSetId NettingSetId, ClearingMode ClearingMode, RegulatoryApproach Approach, Money Ead);

public sealed record RwaResult(CounterpartyId CounterpartyId, Money CcrRwa, Money CvaRwa, Money CcpRwa);

public sealed record CvaResult(CounterpartyId CounterpartyId, Money Cva, Money Dva, Money Cs01);

/// <summary>Contribution of one trade (Euler: may be negative).</summary>
public sealed record TradeContribution(TradeId TradeId, Money Contribution);

/// <summary>Allocation of a netting set figure below the netting set (ADR-002 §5).</summary>
public sealed record AllocationResult(
    NettingSetId NettingSetId,
    MeasureKey Measure,
    AllocationRule Rule,
    Money Total,
    IReadOnlyList<TradeContribution> Contributions,
    Money CollateralContribution,
    Money Unallocated,
    Money StandardError);

public sealed record Limit(LimitId Id, CounterpartyId? CounterpartyId, CounterpartyGroupId? GroupId, MeasureKey Measure, Money Amount);

public enum LimitStatus
{
    Ok = 1,

    Warning = 2,

    Breach = 3,
}

public sealed record LimitUtilisation(LimitId LimitId, Money Limit, Money Exposure, decimal Utilisation, Money Headroom, LimitStatus Status);

/// <summary>Code of a scenario of the stress catalogue (ADR-002 §8), e.g. <c>EBA-2025-ADVERSE</c>.</summary>
public sealed record StressScenario(string Code, string CatalogueVersion);

public sealed record StressedExposure(NettingSetId NettingSetId, MeasureKey Measure, Money Base, Money Stressed);

/// <summary>The three separate roles of a CDS (W8): never netted against each other.</summary>
public sealed record CdsExposure(
    TradeId TradeId,
    Money CounterpartyCcrExposure,
    Money ProtectionBoughtOnReferenceEntity,
    Money ProtectionSoldJumpToDefault,
    bool SpecificWrongWayRisk);

public sealed record CollateralMetrics(
    NettingSetId NettingSetId,
    Money VariationMarginReceived,
    Money VariationMarginPosted,
    Money InitialMarginReceived,
    Money InitialMarginPosted,
    Money UncollateralisedExposure);

public sealed record ConcentrationMetrics(CounterpartyGroupId GroupId, Money LargeExposureValue, decimal ShareOfTier1, bool AboveReportingThreshold);
