using CounterpartyRisk.Domain.Contracts.Identifiers;
using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.ReferenceData;

namespace CounterpartyRisk.Domain.Contracts.Portfolio;

// Read-only shapes exchanged between modules. Invariants are enforced by the owning module's
// aggregates (#18, #43); these records only carry validated data across module boundaries.

/// <summary>Derivative or SFT. A financing is never a trade (see <see cref="OperationType"/>).</summary>
public enum TradeType
{
    Otc = 1,

    Etd = 2,

    /// <summary>Securities financing transaction (repo, securities lending).</summary>
    Sft = 3,
}

/// <summary>"Operation type" analysis axis; extends <see cref="TradeType"/> with financing.</summary>
public enum OperationType
{
    Otc = 1,

    Etd = 2,

    Sft = 3,

    Financing = 4,
}

/// <summary>SA-CCR asset class (CRR art. 277).</summary>
public enum SaCcrAssetClass
{
    InterestRate = 1,

    ForeignExchange = 2,

    Credit = 3,

    Equity = 4,

    Commodity = 5,
}

public enum ClearingMode
{
    Bilateral = 1,

    /// <summary>Cleared through a qualifying CCP.</summary>
    Qccp = 2,

    NonQccp = 3,
}

public enum CollateralStatus
{
    Uncollateralised = 1,

    /// <summary>Variation margin only.</summary>
    VmOnly = 2,

    /// <summary>Variation and initial margin.</summary>
    VmAndIm = 3,
}

public enum CollateralDirection
{
    Received = 1,

    Posted = 2,
}

public enum MarginType
{
    VariationMargin = 1,

    InitialMargin = 2,

    IndependentAmount = 3,
}

/// <summary>Credit standing class of the counterparty under the standardised approach (CRR art. 112).</summary>
public enum SaExposureClass
{
    CentralGovernment = 1,

    RegionalGovernment = 2,

    PublicSectorEntity = 3,

    MultilateralDevelopmentBank = 4,

    InternationalOrganisation = 5,

    Institution = 6,

    Corporate = 7,

    InDefault = 8,
}

/// <summary>Counterparty identified by its LEI (GLEIF, #15).</summary>
public sealed record Counterparty(
    CounterpartyId Id,
    Lei Lei,
    string LegalName,
    CounterpartyGroupId GroupId,
    SaExposureClass ExposureClass);

/// <summary>Group of counterparties under one GLEIF ultimate parent.</summary>
public sealed record CounterpartyGroup(CounterpartyGroupId Id, Lei UltimateParentLei, string Name);

/// <summary>Legal entity of the reporting bank.</summary>
public sealed record LegalEntity(LegalEntityId Id, Lei Lei, string Code);

public sealed record BusinessLine(BusinessLineId Id, string Code);

public sealed record Desk(DeskId Id, BusinessLineId BusinessLineId, string Code);

/// <summary>Collateral agreement terms (threshold, minimum transfer amount, independent amount, MPOR).</summary>
public sealed record Csa(
    CsaId Id,
    CollateralStatus Status,
    Money Threshold,
    Money MinimumTransferAmount,
    Money IndependentAmount,
    int MarginPeriodOfRiskDays);

/// <summary>Set of trades under one enforceable netting agreement.</summary>
public sealed record NettingSet(
    NettingSetId Id,
    CounterpartyId CounterpartyId,
    LegalEntityId LegalEntityId,
    ClearingMode ClearingMode,
    CsaId? CsaId);

public sealed record Trade(
    TradeId Id,
    NettingSetId NettingSetId,
    DeskId DeskId,
    TradeType Type,
    SaCcrAssetClass AssetClass,
    Money Notional,
    Money MarkToMarket,
    DateOnly TradeDate,
    DateOnly MaturityDate);

public sealed record Collateral(
    CollateralId Id,
    CsaId CsaId,
    CollateralDirection Direction,
    MarginType MarginType,
    Money MarketValue,
    decimal Haircut);

/// <summary>A netting set with everything an engine needs to compute it on its own.</summary>
public sealed record NettingSetPortfolio(
    NettingSet NettingSet,
    Counterparty Counterparty,
    Csa? Csa,
    IReadOnlyList<Trade> Trades,
    IReadOnlyList<Collateral> Collaterals);
