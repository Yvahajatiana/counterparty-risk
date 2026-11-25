using CounterpartyRisk.Domain.Contracts.Identifiers;
using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.Portfolio;
using CounterpartyRisk.Domain.Contracts.Quality;
using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.Engines;

/// <summary>
/// Authorised data scope of a request: the legal entities and business lines the caller may see,
/// already intersected with the ABAC rights (#9). A filter requested by the user only narrows it.
/// </summary>
public sealed record DataScope(IReadOnlySet<LegalEntityId> LegalEntities, IReadOnlySet<BusinessLineId> BusinessLines);

/// <summary>ECB reference rate: units of <see cref="Quote"/> per 1 unit of <see cref="Base"/> (EUR).</summary>
public sealed record FxRate(Currency Base, Currency Quote, decimal Rate, AsOfDate FixingDate);

public sealed record CurvePoint(int TenorDays, decimal Rate);

/// <summary>Zero-coupon yield curve (ECB, #16).</summary>
public sealed record YieldCurve(string CurveId, Currency Currency, IReadOnlyList<CurvePoint> Points);

public sealed record MarketDataSnapshot(
    AsOfDate AsOf,
    IReadOnlyList<FxRate> FxRates,
    IReadOnlyList<YieldCurve> Curves,
    DataLineage Lineage);

public sealed record CounterpartyReference(Counterparty Counterparty, CounterpartyGroup Group, DataLineage Lineage);

public sealed record PortfolioSnapshot(
    AsOfDate AsOf,
    IReadOnlyList<NettingSetPortfolio> NettingSets,
    DataLineage Lineage,
    DataCompleteness Completeness);
