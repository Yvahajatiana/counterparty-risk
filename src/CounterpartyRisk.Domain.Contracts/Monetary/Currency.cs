namespace CounterpartyRisk.Domain.Contracts.Monetary;

/// <summary>
/// ISO 4217 alphabetic currency code (three upper-case ASCII letters, e.g. EUR, USD).
/// Only the format is checked here; the list of active codes belongs to reference data.
/// </summary>
public readonly record struct Currency
{
    private Currency(string code)
    {
        Code = code;
    }

    public string Code { get; }

    public static Currency Eur { get; } = new("EUR");

    public static Currency Usd { get; } = new("USD");

    public static Currency Of(string code)
    {
        if (!TryParse(code, out var currency))
        {
            throw new ArgumentException("Currency code must be three upper-case ISO 4217 letters.", nameof(code));
        }

        return currency;
    }

    public static bool TryParse(string? code, out Currency currency)
    {
        currency = default;
        if (code is null || code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        currency = new Currency(code);
        return true;
    }

    public override string ToString() => Code ?? string.Empty;
}
