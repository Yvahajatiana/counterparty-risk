using CounterpartyRisk.Domain.Contracts.Measures;
using CounterpartyRisk.Domain.Contracts.Methodology;
using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Measures;

public class ExposureAggregationGuardTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static readonly AsOfDate AsOf = AsOfDate.Create(new DateOnly(2026, 10, 30), Clock).Value;
    private static readonly MethodVersionId Version = MethodVersionId.Create("ccr-2026.1").Value;

    [Fact]
    public void Same_measure_same_context_can_be_summed()
    {
        var a = Amount(MeasureCode.NetCollateralisedExposure, ExposureNature.Ccr);
        var b = Amount(MeasureCode.NetCollateralisedExposure, ExposureNature.Ccr);

        ExposureAggregationGuard.CanAggregate(a, b).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Ead_of_ccr_and_financing_can_be_summed()
    {
        var ccr = Amount(MeasureCode.ExposureAtDefault, ExposureNature.Ccr, RegulatoryApproach.SaCcr);
        var financing = Amount(MeasureCode.ExposureAtDefault, ExposureNature.Financing, RegulatoryApproach.SaCreditRisk);

        ExposureAggregationGuard.CanAggregate(ccr, financing).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(MeasureCode.GrossPositiveMtm, MeasureCode.GrossSftExposure)]
    [InlineData(MeasureCode.Drawn, MeasureCode.Undrawn)]
    public void Allowed_section_totals_can_be_summed(MeasureCode left, MeasureCode right)
    {
        var nature = left == MeasureCode.Drawn ? ExposureNature.Financing : ExposureNature.Ccr;

        ExposureAggregationGuard.CanAggregate(Amount(left, nature), Amount(right, nature)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Economic_and_regulatory_measures_are_never_summed()
    {
        var ee = Amount(MeasureCode.ExpectedExposure, ExposureNature.Ccr, RegulatoryApproach.ImmSimulated);
        var ead = Amount(MeasureCode.ExposureAtDefault, ExposureNature.Ccr, RegulatoryApproach.SaCcr);

        Refusal(ee, ead).ShouldBe("aggregation.mixed_families");
    }

    [Fact]
    public void Gross_mtm_and_drawn_are_never_summed()
    {
        var grossMtm = Amount(MeasureCode.GrossPositiveMtm, ExposureNature.Ccr);
        var drawn = Amount(MeasureCode.Drawn, ExposureNature.Financing);

        Refusal(grossMtm, drawn).ShouldBe("aggregation.mixed_families");
    }

    [Fact]
    public void Notional_and_total_commitment_are_never_summed()
    {
        var notional = Amount(MeasureCode.GrossNotional, ExposureNature.Ccr);
        var commitment = Amount(MeasureCode.TotalCommitment, ExposureNature.Financing);

        Refusal(notional, commitment).ShouldBe("aggregation.mixed_families");
    }

    [Theory]
    [InlineData(MeasureCode.ExpectedPositiveExposure, MeasureCode.ExpectedNegativeExposure)]
    [InlineData(MeasureCode.CreditValuationAdjustment, MeasureCode.DebitValuationAdjustment)]
    public void Different_measures_of_one_family_are_never_summed(MeasureCode left, MeasureCode right)
    {
        var approach = left == MeasureCode.ExpectedPositiveExposure ? RegulatoryApproach.ImmSimulated : (RegulatoryApproach?)null;

        Refusal(Amount(left, ExposureNature.Ccr, approach), Amount(right, ExposureNature.Ccr, approach))
            .ShouldBe("aggregation.mixed_measures");
    }

    [Fact]
    public void A_pfe_is_never_summed()
    {
        var pfe = Amount(MeasureCode.PotentialFutureExposure, ExposureNature.Ccr, RegulatoryApproach.ImmSimulated);

        Refusal(pfe, pfe).ShouldBe("aggregation.pfe_not_additive");
    }

    [Fact]
    public void Imm_simulated_never_mixes_with_a_regulatory_approach()
    {
        var imm = Amount(MeasureCode.EffectiveExpectedPositiveExposure, ExposureNature.Ccr, RegulatoryApproach.ImmSimulated);
        var replacementCost = Amount(MeasureCode.ReplacementCost, ExposureNature.Ccr, RegulatoryApproach.SaCcr);

        Refusal(imm, replacementCost).ShouldBe("aggregation.mixed_measures");
    }

    [Fact]
    public void Current_exposure_is_not_summed_across_natures()
    {
        // No current exposure measure exists for financing: the key itself is refused.
        MeasureKey.Create(MeasureCode.NetCollateralisedExposure, ExposureNature.Financing).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Amounts_from_different_method_versions_or_origins_are_never_summed()
    {
        var current = Amount(MeasureCode.NetExposure, ExposureNature.Ccr);
        var otherVersion = current with { MethodVersion = MethodVersionId.Create("ccr-2025.4").Value };
        var benchmark = current with { Origin = DataOrigin.PublishedBenchmark };

        Refusal(current, otherVersion).ShouldBe("aggregation.mixed_context");
        Refusal(current, benchmark).ShouldBe("aggregation.mixed_context");
    }

    [Fact]
    public void Profile_points_at_different_horizons_are_never_summed()
    {
        var ee = Amount(MeasureCode.ExpectedExposure, ExposureNature.Ccr, RegulatoryApproach.ImmSimulated);

        Refusal(ee with { HorizonDays = 30 }, ee with { HorizonDays = 91 }).ShouldBe("aggregation.mixed_horizons");
    }

    [Fact]
    public void Amounts_in_different_currencies_are_never_summed()
    {
        var eur = Amount(MeasureCode.NetExposure, ExposureNature.Ccr);
        var usd = eur with { Value = new Money(1m, Currency.Usd) };

        Refusal(eur, usd).ShouldBe("aggregation.mixed_currencies");
    }

    private static string Refusal(MeasuredAmount left, MeasuredAmount right)
    {
        var result = ExposureAggregationGuard.CanAggregate(left, right);
        result.IsFailure.ShouldBeTrue();
        return result.Error.Code;
    }

    private static MeasuredAmount Amount(MeasureCode code, ExposureNature nature, RegulatoryApproach? approach = null) =>
        new(MeasureKey.Create(code, nature, approach).Value, new Money(100m, Currency.Eur), AsOf, Version);
}
