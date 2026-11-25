namespace CounterpartyRisk.Domain.Contracts.Methodology;

/// <summary>
/// Relative horizons (in calendar days from the as-of date) of the exposure profile, identical over
/// the whole history (ADR-002 §4): daily up to 2 weeks, weekly up to 3 months, monthly up to
/// 1 year, quarterly up to 5 years, then yearly up to the longest maturity, capped at 30 years.
/// The simulator (#22) adds the t - MPOR points and the maturity and cash-flow dates.
/// </summary>
public static class ExposureHorizonGrid
{
    public const int CapDays = 30 * 365;

    /// <summary>Horizons up to <paramref name="longestMaturityDays"/> (capped), ascending, without duplicates.</summary>
    public static IReadOnlyList<int> HorizonsUpTo(int longestMaturityDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(longestMaturityDays);

        var limit = Math.Min(longestMaturityDays, CapDays);
        var horizons = new SortedSet<int>();

        AddSteps(horizons, step: 1, until: 14, limit);
        AddSteps(horizons, step: 7, until: 91, limit);
        AddSteps(horizons, step: 30, until: 365, limit);
        AddSteps(horizons, step: 91, until: 5 * 365, limit);
        AddSteps(horizons, step: 365, until: CapDays, limit);

        // The last point always reaches the longest maturity (or the cap).
        if (limit > 0)
        {
            horizons.Add(limit);
        }

        return [.. horizons];
    }

    private static void AddSteps(SortedSet<int> horizons, int step, int until, int limit)
    {
        var start = horizons.Count == 0 ? 0 : horizons.Max;
        for (var day = start + step; day <= until && day <= limit; day += step)
        {
            horizons.Add(day);
        }

        // Close each band exactly on its boundary (14 d, 91 d, 365 d, 5 y) when it is reachable.
        if (until <= limit)
        {
            horizons.Add(until);
        }
    }
}
