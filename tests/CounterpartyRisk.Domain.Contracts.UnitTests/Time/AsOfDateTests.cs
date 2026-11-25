using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Time;

public class AsOfDateTests
{
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 11, 25, 23, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Create_accepts_today_and_past_dates()
    {
        AsOfDate.Create(new DateOnly(2026, 11, 25), Clock).IsSuccess.ShouldBeTrue();
        AsOfDate.Create(new DateOnly(2025, 12, 31), Clock).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_rejects_a_date_in_the_future()
    {
        var result = AsOfDate.Create(new DateOnly(2026, 11, 26), Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("as_of_date.in_future");
    }

    [Fact]
    public void As_of_dates_compare_chronologically()
    {
        var earlier = AsOfDate.Create(new DateOnly(2026, 1, 1), Clock).Value;
        var later = AsOfDate.Create(new DateOnly(2026, 6, 30), Clock).Value;

        (earlier < later).ShouldBeTrue();
        later.ToString().ShouldBe("2026-06-30");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
