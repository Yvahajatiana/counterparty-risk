using CounterpartyRisk.Domain.Contracts.Monetary;

namespace CounterpartyRisk.Domain.Contracts.UnitTests.Monetary;

public class MoneyTests
{
    [Theory]
    [InlineData("EUR")]
    [InlineData("USD")]
    [InlineData("JPY")]
    public void Currency_accepts_iso_4217_codes(string code)
    {
        Currency.TryParse(code, out var currency).ShouldBeTrue();
        currency.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void Currency_rejects_invalid_codes(string? code)
    {
        Currency.TryParse(code, out _).ShouldBeFalse();
    }

    [Fact]
    public void Money_adds_amounts_in_same_currency_without_rounding()
    {
        var total = new Money(0.1m, Currency.Eur) + new Money(0.2m, Currency.Eur);

        total.ShouldBe(new Money(0.3m, Currency.Eur));
    }

    [Fact]
    public void Money_refuses_cross_currency_arithmetic()
    {
        Should.Throw<InvalidOperationException>(() => new Money(1m, Currency.Eur) + new Money(1m, Currency.Usd));
    }

    [Fact]
    public void Money_requires_a_currency()
    {
        Should.Throw<ArgumentException>(() => new Money(1m, default));
    }
}
