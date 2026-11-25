namespace CounterpartyRisk.Bff.Mock;

// Front-end driven contracts of the dashboard read endpoints (/api/dashboard/*).
// They are served from synthetic data by MockDashboardData today, and will be replaced one
// endpoint at a time by the Api (through the BFF reverse proxy) without changing their shape.

/// <summary>BCBS 239 data quality header carried by every widget payload.</summary>
public sealed record DataQuality(
    DateOnly AsOfDate,
    string ReportingCurrency,
    string Source,
    string Status,
    string Completeness);

/// <summary>Counterparty reference, used by the counterparty selectors of the front end.</summary>
public sealed record CounterpartyRef(string Id, string Name, string Group, string BusinessLine);

// Summary

public sealed record KpiTile(
    string Code,
    string Label,
    string Level,
    string Vision,
    decimal Value,
    decimal? PreviousDay,
    decimal? PreviousMonth,
    string Unit,
    string Route,
    string? Breakdown,
    string? Status);

public sealed record SummaryResponse(DataQuality Quality, IReadOnlyList<KpiTile> Tiles);

// Current exposure, from gross to net

public sealed record CcrWaterfall(
    decimal GrossNotional,
    decimal GrossPositiveMtm,
    decimal NettingBenefit,
    decimal CurrentExposure,
    decimal CollateralReceivedNet,
    decimal ReplacementCost);

public sealed record CurrentExposureRow(
    string CounterpartyId,
    string Counterparty,
    string BusinessLine,
    string CollateralStatus,
    int NettingSets,
    decimal GrossNotional,
    decimal GrossPositiveMtm,
    decimal CurrentExposure,
    decimal CollateralReceivedNet,
    decimal ReplacementCost);

public sealed record FinancingCommitment(
    decimal Drawn,
    decimal Undrawn,
    decimal TotalCommitment,
    decimal WeightedCcf,
    decimal OffBalanceSheetEad,
    decimal FinancingEad);

public sealed record CurrentExposureResponse(
    DataQuality Quality,
    CcrWaterfall Ccr,
    IReadOnlyList<CurrentExposureRow> Rows,
    FinancingCommitment Financing);

// Future exposure profile (economic view)

public sealed record ExposureProfilePoint(string Horizon, decimal Months, decimal Ee, decimal Pfe, decimal Ene);

public sealed record PfePeakRow(
    string CounterpartyId,
    string Counterparty,
    string BusinessLine,
    decimal PeakPfe,
    string PeakHorizon,
    decimal Epe,
    decimal EffectiveEpe);

public sealed record FutureExposureResponse(
    DataQuality Quality,
    decimal PfeQuantile,
    IReadOnlyList<CounterpartyRef> Counterparties,
    string CounterpartyId,
    IReadOnlyList<ExposureProfilePoint> Profile,
    decimal Epe,
    decimal EffectiveEpe,
    IReadOnlyList<PfePeakRow> Peaks);

// Limits and utilisation

public sealed record UtilisationThresholds(decimal Warning, decimal Breach);

public sealed record LimitRow(
    string CounterpartyId,
    string Counterparty,
    string Group,
    string Desk,
    string Measure,
    decimal Limit,
    decimal Exposure,
    decimal Utilisation,
    decimal Headroom,
    int? BreachAgeDays);

public sealed record DatedValue(DateOnly Date, decimal Value);

public sealed record UtilisationSeries(string CounterpartyId, string Counterparty, IReadOnlyList<DatedValue> Points);

public sealed record LimitsResponse(
    DataQuality Quality,
    UtilisationThresholds Thresholds,
    IReadOnlyList<LimitRow> Rows,
    IReadOnlyList<UtilisationSeries> History);

// Risk history

public sealed record HistoryEvent(DateOnly Date, string Label);

public sealed record HistoryResponse(
    DataQuality Quality,
    string Indicator,
    string Label,
    string Level,
    string Vision,
    string Frequency,
    IReadOnlyList<DatedValue> Points,
    IReadOnlyList<HistoryEvent> Events);
