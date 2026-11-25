using CounterpartyRisk.Domain.Contracts.Measures;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Measures;

public class MeasureCatalogTests
{
    [Fact]
    public void Every_measure_code_has_exactly_one_definition()
    {
        MeasureCatalog.All.Select(d => d.Code).Order().ShouldBe(Enum.GetValues<MeasureCode>().Order());
    }

    [Fact]
    public void No_economic_measure_exists_for_financing()
    {
        MeasureCatalog.All
            .Where(d => d.Family == MeasureFamily.Economic)
            .ShouldAllBe(d => !d.Natures.Contains(ExposureNature.Financing));
    }

    [Fact]
    public void The_current_exposure_waterfall_never_includes_a_regulatory_measure()
    {
        MeasureCode[] waterfall =
        [
            MeasureCode.GrossNotional,
            MeasureCode.GrossPositiveMtm,
            MeasureCode.NetExposure,
            MeasureCode.NetCollateralisedExposure,
        ];

        waterfall.ShouldAllBe(code => MeasureCatalog.Find(code).Value.Family == MeasureFamily.CurrentExposure);
        MeasureCatalog.Find(MeasureCode.ReplacementCost).Value.Family.ShouldBe(MeasureFamily.Regulatory);
    }

    [Theory]
    [InlineData(MeasureCode.ExposureAtDefault, ExposureNature.Ccr, null)]
    [InlineData(MeasureCode.ExposureAtDefault, ExposureNature.Financing, RegulatoryApproach.ImmSimulated)]
    [InlineData(MeasureCode.ExpectedExposure, ExposureNature.Ccr, RegulatoryApproach.SaCcr)]
    [InlineData(MeasureCode.GrossPositiveMtm, ExposureNature.Ccr, RegulatoryApproach.SaCcr)]
    [InlineData(MeasureCode.Drawn, ExposureNature.Ccr, null)]
    public void Invalid_measure_keys_are_refused(MeasureCode code, ExposureNature nature, RegulatoryApproach? approach)
    {
        MeasureKey.Create(code, nature, approach).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_valid_key_carries_family_and_level()
    {
        var key = MeasureKey.Create(MeasureCode.GrossPositiveMtm, ExposureNature.Ccr).Value;

        key.Family.ShouldBe(MeasureFamily.CurrentExposure);
        key.Level.ShouldBe(ExposureLevel.GrossBeforeCrm);
        key.Approach.ShouldBeNull();
    }
}
