using System.Collections.Frozen;
using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.Domain.Contracts.Measures;

/// <summary>Family, allowed natures, level and allowed approaches of one measure code.</summary>
public sealed record MeasureDefinition(
    MeasureCode Code,
    MeasureFamily Family,
    ExposureLevel Level,
    IReadOnlySet<ExposureNature> Natures,
    IReadOnlySet<RegulatoryApproach> Approaches);

/// <summary>
/// Single source of truth for the classification of measures (validated by the Expert CCR for #5).
/// A <see cref="MeasureKey"/> can only be built when it matches this catalogue.
/// </summary>
public static class MeasureCatalog
{
    private static readonly FrozenSet<ExposureNature> Ccr =
        FrozenSet.Create<ExposureNature>(ExposureNature.Ccr);
    private static readonly FrozenSet<ExposureNature> Financing =
        FrozenSet.Create<ExposureNature>(ExposureNature.Financing);
    private static readonly FrozenSet<ExposureNature> Both =
        FrozenSet.Create<ExposureNature>(ExposureNature.Ccr, ExposureNature.Financing);
    private static readonly FrozenSet<RegulatoryApproach> None = FrozenSet<RegulatoryApproach>.Empty;
    private static readonly FrozenSet<RegulatoryApproach> SaCcr =
        FrozenSet.Create<RegulatoryApproach>(RegulatoryApproach.SaCcr);
    private static readonly FrozenSet<RegulatoryApproach> Imm =
        FrozenSet.Create<RegulatoryApproach>(RegulatoryApproach.ImmSimulated);
    private static readonly FrozenSet<RegulatoryApproach> Ead =
        FrozenSet.Create<RegulatoryApproach>(
            RegulatoryApproach.SaCcr, RegulatoryApproach.Fccm, RegulatoryApproach.SaCreditRisk, RegulatoryApproach.CemHistorical);

    // Frozen collections: callers cannot cast them back to a mutable type and alter validation.
    private static readonly FrozenDictionary<MeasureCode, MeasureDefinition> Definitions = new MeasureDefinition[]
    {
        new(MeasureCode.GrossNotional, MeasureFamily.CurrentExposure, ExposureLevel.Notional, Ccr, None),
        new(MeasureCode.GrossPositiveMtm, MeasureFamily.CurrentExposure, ExposureLevel.GrossBeforeCrm, Ccr, None),
        new(MeasureCode.GrossSftExposure, MeasureFamily.CurrentExposure, ExposureLevel.GrossBeforeCrm, Ccr, None),
        new(MeasureCode.NetExposure, MeasureFamily.CurrentExposure, ExposureLevel.AfterNetting, Ccr, None),
        new(MeasureCode.NetCollateralisedExposure, MeasureFamily.CurrentExposure, ExposureLevel.AfterCollateral, Ccr, None),
        new(MeasureCode.ReplacementCost, MeasureFamily.Regulatory, ExposureLevel.AfterCollateral, Ccr, SaCcr),
        new(MeasureCode.SaCcrPfeAddOn, MeasureFamily.Regulatory, ExposureLevel.AfterNetting, Ccr, SaCcr),
        new(MeasureCode.ExposureAtDefault, MeasureFamily.Regulatory, ExposureLevel.Ead, Both, Ead),
        new(MeasureCode.RiskWeightedExposure, MeasureFamily.Regulatory, ExposureLevel.RiskWeighted, Both, Ead),
        new(MeasureCode.EffectiveExpectedPositiveExposure, MeasureFamily.Regulatory, ExposureLevel.AfterCollateral, Ccr, Imm),
        new(MeasureCode.LargeExposureValue, MeasureFamily.Regulatory, ExposureLevel.LargeExposureValue, Both, None),
        new(MeasureCode.ExpectedExposure, MeasureFamily.Economic, ExposureLevel.AfterCollateral, Ccr, Imm),
        new(MeasureCode.PotentialFutureExposure, MeasureFamily.Economic, ExposureLevel.AfterCollateral, Ccr, Imm),
        new(MeasureCode.ExpectedPositiveExposure, MeasureFamily.Economic, ExposureLevel.AfterCollateral, Ccr, Imm),
        new(MeasureCode.ExpectedNegativeExposure, MeasureFamily.Economic, ExposureLevel.AfterCollateral, Ccr, Imm),
        new(MeasureCode.CreditValuationAdjustment, MeasureFamily.Accounting, ExposureLevel.ValuationAdjustment, Ccr, None),
        new(MeasureCode.DebitValuationAdjustment, MeasureFamily.Accounting, ExposureLevel.ValuationAdjustment, Ccr, None),
        new(MeasureCode.Drawn, MeasureFamily.Accounting, ExposureLevel.Drawn, Financing, None),
        new(MeasureCode.Undrawn, MeasureFamily.Accounting, ExposureLevel.Undrawn, Financing, None),
        new(MeasureCode.TotalCommitment, MeasureFamily.Accounting, ExposureLevel.TotalCommitment, Financing, None),
    }.ToFrozenDictionary(d => d.Code);

    public static IReadOnlyCollection<MeasureDefinition> All => Definitions.Values;

    public static Result<MeasureDefinition> Find(MeasureCode code) =>
        Definitions.TryGetValue(code, out var definition)
            ? definition
            : Error.Validation("measure.unknown_code", "Unknown measure code.");
}

/// <summary>
/// Full identity of what an amount measures. Built only through <see cref="Create"/>, which
/// rejects any combination the catalogue does not allow (e.g. an economic measure on financing).
/// </summary>
public readonly record struct MeasureKey
{
    private MeasureKey(MeasureDefinition definition, ExposureNature nature, RegulatoryApproach? approach)
    {
        Code = definition.Code;
        Family = definition.Family;
        Level = definition.Level;
        Nature = nature;
        Approach = approach;
    }

    public MeasureCode Code { get; }

    public MeasureFamily Family { get; }

    public ExposureLevel Level { get; }

    public ExposureNature Nature { get; }

    /// <summary>Null for measures that do not depend on an approach (current exposure, book values).</summary>
    public RegulatoryApproach? Approach { get; }

    public static Result<MeasureKey> Create(MeasureCode code, ExposureNature nature, RegulatoryApproach? approach = null)
    {
        var found = MeasureCatalog.Find(code);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var definition = found.Value;
        if (!definition.Natures.Contains(nature))
        {
            return Error.Validation("measure.invalid_nature", "This measure does not exist for this exposure nature.");
        }

        var approachOk = definition.Approaches.Count == 0
            ? approach is null
            : approach is { } a && definition.Approaches.Contains(a);
        if (!approachOk)
        {
            return Error.Validation("measure.invalid_approach", "This measure does not exist for this approach.");
        }

        return new MeasureKey(definition, nature, approach);
    }
}
