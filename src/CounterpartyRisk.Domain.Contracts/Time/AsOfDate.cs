using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.Domain.Contracts.Time;

/// <summary>
/// Reporting (closing) date of a snapshot or a result. Never in the future: a date after today's
/// UTC date is rejected, so no result can be dated ahead of the data it was computed from.
/// </summary>
public readonly record struct AsOfDate : IComparable<AsOfDate>
{
    private AsOfDate(DateOnly value)
    {
        Value = value;
    }

    public DateOnly Value { get; }

    public static Result<AsOfDate> Create(DateOnly value, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return value > today
            ? Error.Validation("as_of_date.in_future", "The as-of date cannot be in the future.")
            : new AsOfDate(value);
    }

    public int CompareTo(AsOfDate other) => Value.CompareTo(other.Value);

    public static bool operator <(AsOfDate left, AsOfDate right) => left.CompareTo(right) < 0;

    public static bool operator >(AsOfDate left, AsOfDate right) => left.CompareTo(right) > 0;

    public static bool operator <=(AsOfDate left, AsOfDate right) => left.CompareTo(right) <= 0;

    public static bool operator >=(AsOfDate left, AsOfDate right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
