using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.Quality;
using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Quality;

public class DataQualityTests
{
    [Theory]
    [InlineData(100, CompletenessStatus.Complete)]
    [InlineData(99.5, CompletenessStatus.Partial)]
    [InlineData(0, CompletenessStatus.Missing)]
    public void Completeness_status_follows_the_percentage(double percent, CompletenessStatus expected)
    {
        DataCompleteness.FromPercent((decimal)percent).Value.Status.ShouldBe(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Completeness_out_of_range_is_refused(double percent)
    {
        DataCompleteness.FromPercent((decimal)percent).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ecb-exr", true)]
    [InlineData("gleif-golden-copy", true)]
    [InlineData("ECB", false)]
    [InlineData("ecb exr", false)]
    [InlineData("", false)]
    public void Lineage_source_ids_are_closed_codes(string sourceId, bool valid)
    {
        DataLineage.Create(LineageStatus.Real, sourceId, "Source: ECB statistics.").IsSuccess.ShouldBe(valid);
    }

    [Fact]
    public void Reporting_money_keeps_the_original_amount_and_the_rate()
    {
        var rateDate = AsOfDate.Create(new DateOnly(2026, 10, 30), TimeProvider.System).Value;

        var converted = ReportingMoney.Convert(new Money(108m, Currency.Usd), Currency.Eur, 1.08m, rateDate).Value;

        converted.Value.ShouldBe(new Money(100m, Currency.Eur));
        converted.Original.ShouldBe(new Money(108m, Currency.Usd));
        converted.EcbRate.ShouldBe(1.08m);
    }

    [Theory]
    [InlineData("USD", 0)]
    [InlineData("USD", -1.08)]
    [InlineData("EUR", 1.08)]
    public void Reporting_money_refuses_inconsistent_rates(string currency, double rate)
    {
        var rateDate = AsOfDate.Create(new DateOnly(2026, 10, 30), TimeProvider.System).Value;

        ReportingMoney.Convert(new Money(1m, Currency.Of(currency)), Currency.Eur, (decimal)rate, rateDate).IsFailure.ShouldBeTrue();
    }
}
