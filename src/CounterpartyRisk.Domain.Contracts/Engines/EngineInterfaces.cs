using CounterpartyRisk.Domain.Contracts.Measures;
using CounterpartyRisk.Domain.Contracts.Methodology;
using CounterpartyRisk.Domain.Contracts.Portfolio;
using CounterpartyRisk.Domain.Contracts.ReferenceData;
using CounterpartyRisk.Domain.Contracts.Results;
using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.Engines;

// One interface per consuming ticket (#5), so Back-1/2/3 can implement them in parallel.
// Expected failures come back as a Result; exceptions are reserved for technical faults.

/// <summary>GLEIF reference data (#15).</summary>
public interface IReferenceDataProvider
{
    Task<Result<CounterpartyReference>> GetCounterpartyAsync(Lei lei, AsOfDate asOf, CancellationToken cancellationToken);
}

/// <summary>ECB curves and FX rates (#16).</summary>
public interface IMarketDataSnapshotProvider
{
    Task<Result<MarketDataSnapshot>> GetAsync(AsOfDate asOf, CancellationToken cancellationToken);
}

/// <summary>Synthetic portfolio of the as-of date, restricted to the authorised scope (#18).</summary>
public interface IPortfolioSnapshotProvider
{
    Task<Result<PortfolioSnapshot>> GetAsync(AsOfDate asOf, DataScope scope, CancellationToken cancellationToken);
}

/// <summary>W2 current exposure waterfall (#20).</summary>
public interface IExposureCalculator
{
    Task<Result<IReadOnlyList<ExposureWaterfall>>> ComputeAsync(PortfolioSnapshot portfolio, MarketDataSnapshot market, CancellationToken cancellationToken);
}

/// <summary>SA-CCR EAD per netting set (#21).</summary>
public interface ISaCcrCalculator
{
    Task<Result<SaCcrResult>> ComputeAsync(NettingSetPortfolio nettingSet, MarketDataSnapshot market, CancellationToken cancellationToken);
}

/// <summary>Monte-Carlo profile on the grid and quantile of the method version (#22).</summary>
public interface IExposureProfileSimulator
{
    Task<Result<ExposureProfile>> SimulateAsync(
        NettingSetPortfolio nettingSet,
        MarketDataSnapshot market,
        MethodVersion methodVersion,
        CancellationToken cancellationToken);
}

/// <summary>RWA CCR, CVA (BA-CVA) and CCP (#23).</summary>
public interface IRwaCalculator
{
    Task<Result<RwaResult>> ComputeAsync(
        Counterparty counterparty,
        IReadOnlyList<NettingSetEad> exposures,
        MethodVersion methodVersion,
        CancellationToken cancellationToken);
}

/// <summary>CVA, DVA and CS01 (#24).</summary>
public interface ICvaCalculator
{
    Task<Result<CvaResult>> ComputeAsync(
        Counterparty counterparty,
        IReadOnlyList<ExposureProfile> profiles,
        MarketDataSnapshot market,
        CancellationToken cancellationToken);
}

/// <summary>Allocation of a netting set figure to its trades, with the rule of the method version (#25).</summary>
public interface IAllocationEngine
{
    Task<Result<AllocationResult>> AllocateAsync(
        NettingSetPortfolio nettingSet,
        MeasureKey measure,
        MethodVersion methodVersion,
        CancellationToken cancellationToken);
}

/// <summary>Limit utilisation, headroom and breaches (#27).</summary>
public interface ILimitMonitor
{
    Task<Result<IReadOnlyList<LimitUtilisation>>> EvaluateAsync(
        IReadOnlyList<Limit> limits,
        IReadOnlyList<MeasuredAmount> exposures,
        MethodVersion methodVersion,
        CancellationToken cancellationToken);
}

/// <summary>Stress scenarios and general wrong-way risk (#28).</summary>
public interface IStressEngine
{
    Task<Result<IReadOnlyList<StressedExposure>>> RunAsync(
        StressScenario scenario,
        PortfolioSnapshot portfolio,
        MarketDataSnapshot market,
        CancellationToken cancellationToken);
}

/// <summary>CDS exposures in their three roles (#29).</summary>
public interface ICdsExposureCalculator
{
    Task<Result<IReadOnlyList<CdsExposure>>> ComputeAsync(PortfolioSnapshot portfolio, MarketDataSnapshot market, CancellationToken cancellationToken);
}

/// <summary>W9 collateral and margins (#30).</summary>
public interface ICollateralMetrics
{
    Task<Result<IReadOnlyList<CollateralMetrics>>> ComputeAsync(PortfolioSnapshot portfolio, CancellationToken cancellationToken);
}

/// <summary>W10 concentration and large exposures (#30).</summary>
public interface IConcentrationMetrics
{
    Task<Result<IReadOnlyList<ConcentrationMetrics>>> ComputeAsync(
        IReadOnlyList<MeasuredAmount> exposures,
        MethodVersion methodVersion,
        CancellationToken cancellationToken);
}
