namespace CounterpartyRisk.Bff.Mock;

/// <summary>
/// Deterministic synthetic portfolio for the demo. Every widget is derived from the same
/// counterparties, so the figures stay consistent across widgets (summary totals = current exposure, future exposure and limits details).
/// All names are fictitious. Amounts are in the reporting currency (EUR).
/// </summary>
internal sealed class MockDashboardData
{
    /// <summary>Single PFE quantile of the dashboard (ADR 0002, decision 4).</summary>
    public const decimal PfeQuantile = 0.975m;
    private const string PfeLabel = "PFE 97.5 %";

    private const decimal SaCcrAlpha = 1.4m;
    private const decimal WarningThreshold = 0.80m;
    private const decimal BreachThreshold = 1.00m;

    private static readonly (string Label, decimal Months)[] Horizons =
    [
        ("1W", 0.25m), ("1M", 1m), ("3M", 3m), ("6M", 6m), ("1Y", 12m),
        ("2Y", 24m), ("3Y", 36m), ("5Y", 60m), ("7Y", 84m), ("10Y", 120m),
    ];

    private static readonly MockCounterparty[] Portfolio =
    [
        new("cp-01", "Aurelia Bank SA", "Aurelia Group", "Bank", "Markets", "Rates desk", "CSA VM + IM", 4, 9_800m, 0.18m, 0.62m, 0.88m, 48m, 1.08m, 0.20m),
        new("cp-02", "Borealis Energy AG", "Borealis Holding", "Corporate", "Markets", "Commodities desk", "Uncollateralised", 2, 2_350m, 0.09m, 0.35m, 0.00m, 60m, 0.92m, 1.00m),
        new("cp-03", "Cobalt Asset Management", "Cobalt Group", "Fund", "Markets", "Equity derivatives", "CSA VM only", 3, 3_600m, 0.07m, 0.48m, 0.70m, 24m, 0.55m, 1.00m),
        new("cp-04", "Republic of Deltania", "Republic of Deltania", "Sovereign", "ALM", "ALM hedging", "Uncollateralised", 1, 5_200m, 0.05m, 0.22m, 0.00m, 120m, 0.71m, 0.00m),
        new("cp-05", "Equinox Insurance SE", "Equinox Group", "Insurer", "Markets", "Rates desk", "CSA VM only", 2, 4_100m, 0.08m, 0.41m, 0.75m, 84m, 1.02m, 0.50m),
        new("cp-06", "Fjordline Shipping ASA", "Fjordline Group", "Corporate", "Financing", "FX desk", "Uncollateralised", 1, 1_150m, 0.11m, 0.30m, 0.00m, 36m, 0.84m, 1.00m),
        new("cp-07", "Granite Industries Inc", "Granite Holdings", "Corporate", "Financing", "FX desk", "CSA VM only", 2, 1_900m, 0.06m, 0.38m, 0.65m, 36m, 0.43m, 1.00m),
        new("cp-08", "Halcyon Pension Fund", "Halcyon Pension Fund", "Fund", "Markets", "Rates desk", "CSA VM + IM", 2, 6_400m, 0.06m, 0.55m, 0.92m, 120m, 0.66m, 0.20m),
        new("cp-09", "Ionis Clearing House", "Ionis Clearing House", "CCP", "Markets", "Clearing", "CSA VM + IM", 1, 12_500m, 0.03m, 0.70m, 0.96m, 12m, 0.38m, 0.20m),
        new("cp-10", "Juniper Retail Group", "Juniper Retail Group", "Corporate", "Financing", "FX desk", "Uncollateralised", 1, 850m, 0.10m, 0.25m, 0.00m, 24m, 0.77m, 1.00m),
    ];

    private readonly TimeProvider _time;

    public MockDashboardData(TimeProvider time) => _time = time;

    public static IReadOnlyList<CounterpartyRef> Counterparties { get; } =
        Portfolio.Select(c => new CounterpartyRef(c.Id, c.Name, c.Group, c.BusinessLine)).ToArray();

    public static bool IsKnownCounterparty(string id) => Portfolio.Any(c => c.Id == id);

    /// <summary>Daily figures are published J+1: the as-of date is the previous business day.</summary>
    public DateOnly AsOfDate
    {
        get
        {
            var date = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).AddDays(-1);
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(-1);
            }
            return date;
        }
    }

    private DataQuality Quality() => new(AsOfDate, "EUR", "Synthetic mock (BFF)", "Synthetic", "Complete");

    public SummaryResponse Summary()
    {
        var eadCcr = Portfolio.Sum(SaCcrEad);
        var eadFinancing = Financing().FinancingEad;
        var ead = eadCcr + eadFinancing;
        var rwaCcr = Portfolio.Sum(c => SaCcrEad(c) * RiskWeight(c));
        var rwaCva = Round(rwaCcr * 0.34m);
        var peak = Portfolio.Select(c => (Counterparty: c, Peak: Profile(c).Max(p => p.Pfe))).MaxBy(x => x.Peak);
        var cva = Portfolio.Sum(Cva);
        var breaches = Limits().Rows.Count(r => r.Utilisation >= BreachThreshold);

        KpiTile[] tiles =
        [
            new("ead", "Total EAD", "EAD", "Regulatory", Round(ead), Round(ead * 0.988m), Round(ead * 0.953m), "EUR", "/regulatory-ead",
                $"CCR {Millions(eadCcr)} + Financing {Millions(eadFinancing)}", null),
            new("rwa", "RWA CCR + CVA", "RWA", "Regulatory", Round(rwaCcr + rwaCva), Round((rwaCcr + rwaCva) * 0.994m),
                Round((rwaCcr + rwaCva) * 1.021m), "EUR", "/rwa-capital",
                $"CCR {Millions(rwaCcr)} + CVA {Millions(rwaCva)}", null),
            new("pfe", $"Peak {PfeLabel}", "PFE", "Economic", peak.Peak, null, null, "EUR", "/future-exposure",
                peak.Counterparty.Name, null),
            new("cva", "Accounting CVA", "CVA", "Accounting", cva, Round(cva * 1.012m), Round(cva * 0.962m), "EUR", "/cva-xva",
                null, null),
            new("breaches", "Limit breaches", "Count", "Limits", breaches, Math.Max(0, breaches - 1), breaches + 1, "count", "/limits",
                "Counterparties above 100 % of their PFE limit", breaches > 0 ? "breach" : "ok"),
        ];
        return new SummaryResponse(Quality(), tiles);
    }

    public CurrentExposureResponse CurrentExposure()
    {
        var rows = Portfolio
            .Select(c =>
            {
                var gross = GrossPositiveMtm(c);
                var current = CurrentExposure(c);
                var collateral = Round(current * c.CollateralRatio);
                return new CurrentExposureRow(c.Id, c.Name, c.BusinessLine, c.CollateralStatus, c.NettingSets,
                    Round(c.Notional * 1_000_000m), gross, current, collateral, current - collateral);
            })
            .OrderByDescending(r => r.ReplacementCost)
            .ToArray();

        var waterfall = new CcrWaterfall(
            rows.Sum(r => r.GrossNotional),
            rows.Sum(r => r.GrossPositiveMtm),
            rows.Sum(r => r.GrossPositiveMtm - r.CurrentExposure),
            rows.Sum(r => r.CurrentExposure),
            rows.Sum(r => r.CollateralReceivedNet),
            rows.Sum(r => r.ReplacementCost));

        return new CurrentExposureResponse(Quality(), waterfall, rows, Financing());
    }

    public FutureExposureResponse FutureExposure(string counterpartyId)
    {
        var selected = Portfolio.Single(c => c.Id == counterpartyId);
        var profile = Profile(selected);
        var peaks = Portfolio
            .Select(c =>
            {
                var points = Profile(c);
                var peak = points.MaxBy(p => p.Pfe)!;
                var (epe, effectiveEpe) = Epe(points);
                return new PfePeakRow(c.Id, c.Name, c.BusinessLine, peak.Pfe, peak.Horizon, epe, effectiveEpe);
            })
            .OrderByDescending(r => r.PeakPfe)
            .ToArray();
        var (selectedEpe, selectedEffectiveEpe) = Epe(profile);

        return new FutureExposureResponse(Quality(), PfeQuantile, Counterparties, selected.Id, profile,
            selectedEpe, selectedEffectiveEpe, peaks);
    }

    public LimitsResponse Limits()
    {
        var asOf = AsOfDate;
        var rows = Portfolio
            .Select(c =>
            {
                var exposure = Profile(c).Max(p => p.Pfe);
                var limit = RoundTo(exposure / c.TargetUtilisation, 1_000_000m);
                var utilisation = Math.Round(exposure / limit, 4);
                int? breachAge = utilisation >= BreachThreshold ? 3 + (int)(c.Notional % 17) : null;
                return new LimitRow(c.Id, c.Name, c.Group, c.Desk, PfeLabel, limit, exposure,
                    utilisation, limit - exposure, breachAge);
            })
            .OrderByDescending(r => r.Utilisation)
            .ToArray();

        var history = rows.Take(3)
            .Select(r => new UtilisationSeries(r.CounterpartyId, r.Counterparty,
                BusinessDays(asOf, 65).Select((date, i) =>
                    new DatedValue(date, Math.Round(r.Utilisation * Trend(i, 65, 0.82m, r.CounterpartyId), 4))).ToArray()))
            .ToArray();

        return new LimitsResponse(Quality(), new UtilisationThresholds(WarningThreshold, BreachThreshold), rows, history);
    }

    public HistoryResponse History(HistoryIndicator indicator, HistoryFrequency frequency)
    {
        var summary = Summary().Tiles.ToDictionary(t => t.Code, t => t.Value);
        var (label, level, vision, current) = indicator switch
        {
            HistoryIndicator.Ead => ("Total EAD", "EAD", "Regulatory", summary["ead"]),
            HistoryIndicator.Rwa => ("RWA CCR + CVA", "RWA", "Regulatory", summary["rwa"]),
            HistoryIndicator.PeakPfe => ($"Peak {PfeLabel}", "PFE", "Economic", summary["pfe"]),
            HistoryIndicator.Cva => ("Accounting CVA", "CVA", "Accounting", summary["cva"]),
            _ => ("Replacement cost", "After collateral", "Current exposure", CurrentExposure().Ccr.ReplacementCost),
        };

        var asOf = AsOfDate;
        var dates = frequency == HistoryFrequency.Daily
            ? BusinessDays(asOf, 280)
            : Enumerable.Range(0, 60).Select(i => MonthEnd(asOf.AddMonths(i - 60))).Append(asOf).ToArray();
        var seed = indicator.ToString();
        var points = dates
            .Select((date, i) => new DatedValue(date, Round(current * Trend(i, dates.Length, 0.78m, seed))))
            .ToArray();

        // Method changes stay annotated even though the history is recalculated at constant method (ADR 0002).
        HistoryEvent[] knownEvents =
        [
            new(new DateOnly(2021, 6, 28), "CEM -> SA-CCR"),
            new(new DateOnly(2025, 1, 1), "CRR3 (incl. CCF)"),
        ];
        var events = knownEvents.Where(e => e.Date >= points[0].Date && e.Date <= asOf).ToArray();

        return new HistoryResponse(Quality(), indicator.ToString(), label, level, vision,
            frequency.ToString(), points, events);
    }

    // Portfolio mechanics. Simplified on purpose: this is presentation data, not the risk engine.

    private static decimal GrossPositiveMtm(MockCounterparty c) => Round(c.Notional * 1_000_000m * c.MtmRatio);

    private static decimal CurrentExposure(MockCounterparty c) =>
        Round(GrossPositiveMtm(c) * (1m - c.NettingBenefitRatio));

    private static decimal SaCcrEad(MockCounterparty c)
    {
        var current = CurrentExposure(c);
        var replacementCost = current - Round(current * c.CollateralRatio);
        var addOn = c.Notional * 1_000_000m * 0.012m * (decimal)Math.Sqrt((double)(c.MaturityMonths / 12m));
        var multiplier = c.CollateralRatio > 0.85m ? 0.72m : 1m;
        return Round(SaCcrAlpha * (replacementCost + multiplier * addOn));
    }

    private static decimal RiskWeight(MockCounterparty c) => c.Sector switch
    {
        "Sovereign" => 0m,
        "CCP" => 0.02m,
        "Bank" => 0.30m,
        _ => c.RiskWeight,
    };

    private static decimal Cva(MockCounterparty c) =>
        c.Sector is "CCP" or "Sovereign" ? Round(SaCcrEad(c) * 0.0015m) : Round(SaCcrEad(c) * 0.009m);

    private static ExposureProfilePoint[] Profile(MockCounterparty c)
    {
        var scale = GrossPositiveMtm(c) * (1m - c.CollateralRatio * 0.8m);
        return Horizons.Select(h =>
        {
            // Hump-shaped amortising profile: diffusion grows with sqrt(t), amortisation brings it to zero at maturity.
            var t = (double)h.Months;
            var maturity = (double)c.MaturityMonths;
            var shape = t >= maturity ? 0d : Math.Sqrt(t / 12d) * (1d - t / maturity) + 0.15d * (1d - t / maturity);
            var ee = Round(scale * (decimal)shape);
            return new ExposureProfilePoint(h.Label, h.Months, ee, Round(ee * 2.05m), -Round(ee * 0.58m));
        }).ToArray();
    }

    /// <summary>EPE and Effective EPE over the first year (time-weighted, non-decreasing for the effective one).</summary>
    private static (decimal Epe, decimal EffectiveEpe) Epe(IReadOnlyList<ExposureProfilePoint> profile)
    {
        decimal epe = 0m, effective = 0m, runningMax = 0m, previousMonths = 0m;
        foreach (var point in profile.Where(p => p.Months <= 12m))
        {
            var weight = (point.Months - previousMonths) / 12m;
            runningMax = Math.Max(runningMax, point.Ee);
            epe += point.Ee * weight;
            effective += runningMax * weight;
            previousMonths = point.Months;
        }
        return (Round(epe), Round(effective));
    }

    private static FinancingCommitment Financing()
    {
        const decimal drawn = 1_840_000_000m;
        const decimal undrawn = 960_000_000m;
        const decimal weightedCcf = 0.40m;
        var offBalance = Round(undrawn * weightedCcf);
        return new FinancingCommitment(drawn, undrawn, drawn + undrawn, weightedCcf, offBalance, drawn + offBalance);
    }

    /// <summary>Smooth deterministic path ending at 1 (today's value), starting near <paramref name="start"/>.</summary>
    private static decimal Trend(int index, int count, decimal start, string seed)
    {
        var x = count <= 1 ? 1d : (double)index / (count - 1);
        var phase = seed.Sum(ch => ch) % 17;
        var wave = 0.035 * Math.Sin(x * 9 + phase) + 0.015 * Math.Sin(x * 41 + phase * 2);
        var level = (double)start + (1 - (double)start) * x;
        return index == count - 1 ? 1m : (decimal)(level + wave * (1 - x * 0.9));
    }

    private static DateOnly[] BusinessDays(DateOnly asOf, int count)
    {
        var days = new List<DateOnly>(count);
        for (var date = asOf; days.Count < count; date = date.AddDays(-1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(date);
            }
        }
        days.Reverse();
        return [.. days];
    }

    private static DateOnly MonthEnd(DateOnly date) =>
        new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1);

    private static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal RoundTo(decimal value, decimal step) =>
        Math.Round(value / step, 0, MidpointRounding.AwayFromZero) * step;

    private static string Millions(decimal value) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value / 1_000_000m:N0} m");

    private sealed record MockCounterparty(
        string Id,
        string Name,
        string Group,
        string Sector,
        string BusinessLine,
        string Desk,
        string CollateralStatus,
        int NettingSets,
        decimal Notional,
        decimal MtmRatio,
        decimal NettingBenefitRatio,
        decimal CollateralRatio,
        decimal MaturityMonths,
        decimal TargetUtilisation,
        decimal RiskWeight);
}

public enum HistoryIndicator
{
    Ead,
    Rwa,
    PeakPfe,
    Cva,
    ReplacementCost,
}

public enum HistoryFrequency
{
    Daily,
    Monthly,
}
