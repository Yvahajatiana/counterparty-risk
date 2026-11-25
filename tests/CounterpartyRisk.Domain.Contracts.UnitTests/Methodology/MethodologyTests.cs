using CounterpartyRisk.Domain.Contracts.Methodology;
using CounterpartyRisk.Domain.Contracts.Monetary;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Methodology;

public class MethodologyTests
{
    [Fact]
    public void Defaults_follow_adr_002()
    {
        var options = RiskMethodologyOptions.Default;

        options.PfeQuantile.ShouldBe(0.975m);
        options.AllocationRule.ShouldBe(AllocationRule.Euler);
        options.CvaCapitalApproach.ShouldBe(CvaCapitalApproach.ReducedBaCva);
        options.ReportingCurrency.ShouldBe(Currency.Eur);
        options.LimitAlertThreshold.ShouldBe(0.80m);
    }

    [Theory]
    [InlineData(0.5, 0.8)]
    [InlineData(1.0, 0.8)]
    [InlineData(0.975, 0.0)]
    [InlineData(0.975, 1.0)]
    public void Options_out_of_range_are_refused(double quantile, double alert)
    {
        RiskMethodologyOptions.Create((decimal)quantile, AllocationRule.Euler, CvaCapitalApproach.ReducedBaCva, Currency.Eur, (decimal)alert)
            .IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ccr-2026.1", true)]
    [InlineData("mv1", true)]
    [InlineData("Ccr-2026", false)]
    [InlineData("1ccr", false)]
    [InlineData("ccr 2026", false)]
    [InlineData("ab", false)]
    [InlineData(null, false)]
    public void Method_version_ids_are_short_lower_case_codes(string? value, bool valid)
    {
        MethodVersionId.Create(value).IsSuccess.ShouldBe(valid);
    }

    [Theory]
    [InlineData(MethodVersionStatus.Computing, false)]
    [InlineData(MethodVersionStatus.PendingApproval, false)]
    [InlineData(MethodVersionStatus.Rejected, false)]
    [InlineData(MethodVersionStatus.Active, true)]
    [InlineData(MethodVersionStatus.Retired, true)]
    public void Results_are_visible_only_after_approval(MethodVersionStatus status, bool visible)
    {
        var version = new MethodVersion(MethodVersionId.Create("ccr-2026.1").Value, status, RiskMethodologyOptions.Default, new string('a', 64), new DateOnly(2026, 11, 1));

        version.ResultsVisible.ShouldBe(visible);
    }

    [Fact]
    public void Horizon_grid_follows_the_bands_of_adr_002()
    {
        var horizons = ExposureHorizonGrid.HorizonsUpTo(10 * 365);

        horizons.Take(14).ShouldBe(Enumerable.Range(1, 14));
        horizons.ShouldContain(91);
        horizons.ShouldContain(365);
        horizons.ShouldContain(5 * 365);
        horizons[^1].ShouldBe(10 * 365);
        horizons.ShouldBe(horizons.Order().Distinct());
    }

    [Fact]
    public void Horizon_grid_is_capped_at_30_years()
    {
        ExposureHorizonGrid.HorizonsUpTo(50 * 365)[^1].ShouldBe(ExposureHorizonGrid.CapDays);
    }

    [Fact]
    public void Horizon_grid_stops_at_a_short_maturity()
    {
        ExposureHorizonGrid.HorizonsUpTo(10).ShouldBe(Enumerable.Range(1, 10));
    }
}
