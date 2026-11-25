using CounterpartyRisk.Domain.Contracts.Methodology;
using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.Results;
using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.Measures;

/// <summary>
/// An amount together with everything needed to know whether it can be added to another one.
/// </summary>
/// <param name="Key">What is measured.</param>
/// <param name="Value">Amount in the reporting currency.</param>
/// <param name="AsOf">Reporting date.</param>
/// <param name="MethodVersion">Method version that produced it.</param>
/// <param name="Origin">Recomputed by the bank or published benchmark.</param>
/// <param name="HorizonDays">Horizon of a profile point (EE, ENE), null for a point-in-time figure.</param>
public sealed record MeasuredAmount(
    MeasureKey Key,
    Money Value,
    AsOfDate AsOf,
    MethodVersionId MethodVersion,
    DataOrigin Origin = DataOrigin.Recomputed,
    int? HorizonDays = null);

/// <summary>
/// Aggregation guard shared by the engines (#45) and the API (#31): decides whether two amounts may
/// be summed. Rules validated by the Expert CCR (#5) on top of ADR-002 §1-§2.
/// </summary>
public static class ExposureAggregationGuard
{
    // Different codes that may still be summed (the result is a section total, e.g. drawn + undrawn
    // = total commitment, or the gross exposure before CRM of derivatives and SFTs).
    private static readonly HashSet<(MeasureCode, MeasureCode)> AllowedCodePairs =
    [
        (MeasureCode.GrossPositiveMtm, MeasureCode.GrossSftExposure),
        (MeasureCode.Drawn, MeasureCode.Undrawn),
    ];

    private static readonly HashSet<RegulatoryApproach> RegulatoryEadApproaches =
        [RegulatoryApproach.SaCcr, RegulatoryApproach.Fccm, RegulatoryApproach.SaCreditRisk];

    public static Result CanAggregate(MeasuredAmount left, MeasuredAmount right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var (a, b) = (left.Key, right.Key);

        if (a.Family != b.Family)
        {
            return Refuse("aggregation.mixed_families", "Measures of different families are never summed.");
        }

        if (a.Code == MeasureCode.PotentialFutureExposure || b.Code == MeasureCode.PotentialFutureExposure)
        {
            // A quantile is not additive; netting set contributions come from the allocation engine.
            return Refuse("aggregation.pfe_not_additive", "A PFE is never summed; aggregate exposures path by path.");
        }

        if (a.Code != b.Code && !AllowedCodePairs.Contains((a.Code, b.Code)) && !AllowedCodePairs.Contains((b.Code, a.Code)))
        {
            return Refuse("aggregation.mixed_measures", "Different measures are never summed.");
        }

        if (a.Nature != b.Nature && a.Level is not (ExposureLevel.Ead or ExposureLevel.LargeExposureValue))
        {
            return Refuse("aggregation.mixed_natures", "Only EAD and large exposure values are summed across CCR and financing.");
        }

        if (a.Approach != b.Approach && !BothRegulatoryEad(a, b))
        {
            return Refuse("aggregation.mixed_approaches", "Amounts from different approaches are never summed.");
        }

        if (left.AsOf != right.AsOf || left.MethodVersion != right.MethodVersion || left.Origin != right.Origin)
        {
            return Refuse("aggregation.mixed_context", "Amounts from different dates, method versions or origins are never summed.");
        }

        if (left.Value.Currency != right.Value.Currency)
        {
            return Refuse("aggregation.mixed_currencies", "Amounts must be in the same reporting currency.");
        }

        if (left.HorizonDays != right.HorizonDays)
        {
            return Refuse("aggregation.mixed_horizons", "Profile points at different horizons are never summed.");
        }

        return Result.Success();
    }

    private static bool BothRegulatoryEad(MeasureKey a, MeasureKey b) =>
        a.Level is ExposureLevel.Ead or ExposureLevel.LargeExposureValue or ExposureLevel.RiskWeighted
        && a.Level == b.Level
        && a.Approach is { } x && RegulatoryEadApproaches.Contains(x)
        && b.Approach is { } y && RegulatoryEadApproaches.Contains(y);

    private static Result Refuse(string code, string message) => Result.Failure(Error.Validation(code, message));
}
