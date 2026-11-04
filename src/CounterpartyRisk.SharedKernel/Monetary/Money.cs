using System.Globalization;

namespace CounterpartyRisk.SharedKernel.Monetary;

/// <summary>
/// Monetary amount in a given currency. Always <see cref="decimal"/>: <see cref="double"/>
/// is reserved for the Monte-Carlo core (ADR-001). Arithmetic across currencies is
/// forbidden; conversion goes through an explicit FX rate from market data.
/// </summary>
public readonly record struct Money
{
    public Money(decimal amount, Currency currency)
    {
        if (currency == default)
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Negate() => new(-Amount, Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);

    public static Money operator -(Money money) => money.Negate();

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException("Cannot combine amounts in different currencies without an FX conversion.");
        }
    }
}
