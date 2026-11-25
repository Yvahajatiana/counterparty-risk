using CounterpartyRisk.Domain.Contracts.Results;
using CounterpartyRisk.Domain.Contracts.Time;

namespace CounterpartyRisk.Domain.Contracts.Monetary;

/// <summary>
/// Amount converted into the reporting currency (EUR, ADR-002 §9) with the ECB reference rate
/// that produced it, so every displayed figure can be traced back to its original amount.
/// </summary>
/// <remarks>
/// <see cref="EcbRate"/> is quoted the ECB way: units of <see cref="Original"/> currency per 1 unit
/// of the reporting currency (e.g. 1.08 USD per EUR). A missing fixing uses the last published
/// rate, so <see cref="RateDate"/> can be earlier than the as-of date.
/// </remarks>
public sealed record ReportingMoney
{
    private ReportingMoney(Money original, Money value, decimal ecbRate, AsOfDate rateDate)
    {
        Original = original;
        Value = value;
        EcbRate = ecbRate;
        RateDate = rateDate;
    }

    public Money Original { get; }

    public Money Value { get; }

    public decimal EcbRate { get; }

    public AsOfDate RateDate { get; }

    public static Result<ReportingMoney> Convert(Money original, Currency reportingCurrency, decimal ecbRate, AsOfDate rateDate)
    {
        if (ecbRate <= 0m)
        {
            return Error.Validation("reporting_money.invalid_rate", "An FX rate must be strictly positive.");
        }

        if (original.Currency == reportingCurrency && ecbRate != 1m)
        {
            return Error.Validation("reporting_money.invalid_rate", "An amount already in the reporting currency has a rate of 1.");
        }

        return new ReportingMoney(original, new Money(original.Amount / ecbRate, reportingCurrency), ecbRate, rateDate);
    }
}
