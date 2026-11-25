using CounterpartyRisk.Domain.Contracts.Monetary;
using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.Domain.Contracts.Methodology;

/// <summary>
/// Identifier of a method version from the code catalogue (ADR-002, governance): a short
/// lower-case code such as <c>ccr-2026.1</c>. The publish command carries this id only.
/// </summary>
public readonly record struct MethodVersionId
{
    private MethodVersionId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<MethodVersionId> Create(string? value)
    {
        if (value is null || value.Length is < 3 or > 32 || !IsCode(value))
        {
            return Error.Validation("method_version.invalid_id", "A method version id is 3 to 32 lower-case letters, digits, '.' or '-'.");
        }

        return new MethodVersionId(value);
    }

    public override string ToString() => Value ?? string.Empty;

    private static bool IsCode(string value) =>
        char.IsAsciiLetterLower(value[0])
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-');
}

/// <summary>Life cycle of a method version (ADR-002, governance, four eyes).</summary>
public enum MethodVersionStatus
{
    /// <summary>History recalculation running; results not visible.</summary>
    Computing = 1,

    /// <summary>Recalculation and data quality checks passed, waiting for the direction.</summary>
    PendingApproval = 2,

    Active = 3,

    Rejected = 4,

    /// <summary>Previous active version; its results are kept and a rollback needs four eyes.</summary>
    Retired = 5,
}

/// <summary>Allocation of a netting set figure to its trades (ADR-002 §5).</summary>
public enum AllocationRule
{
    /// <summary>Euler contributions, negative contributions allowed. The project default.</summary>
    Euler = 1,

    /// <summary>Pro rata fallback, inactive by default.</summary>
    ProRata = 2,
}

/// <summary>Capital approach for CVA risk (ADR-002 §7).</summary>
public enum CvaCapitalApproach
{
    /// <summary>Reduced BA-CVA, used when no CVA hedge is designated.</summary>
    ReducedBaCva = 1,

    /// <summary>Full BA-CVA, when eligible CVA hedges are designated.</summary>
    FullBaCva = 2,
}

/// <summary>
/// Methodological choices of the project carried by a method version (ADR-002, "versioned
/// parameters"). Never edited at runtime: a change is a reviewed PR and a new version.
/// </summary>
public sealed record RiskMethodologyOptions
{
    private RiskMethodologyOptions(
        decimal pfeQuantile,
        AllocationRule allocationRule,
        CvaCapitalApproach cvaCapitalApproach,
        Currency reportingCurrency,
        decimal limitAlertThreshold)
    {
        PfeQuantile = pfeQuantile;
        AllocationRule = allocationRule;
        CvaCapitalApproach = cvaCapitalApproach;
        ReportingCurrency = reportingCurrency;
        LimitAlertThreshold = limitAlertThreshold;
    }

    /// <summary>One-sided PFE quantile, 0.975 by default (ADR-002 §4).</summary>
    public decimal PfeQuantile { get; }

    public AllocationRule AllocationRule { get; }

    public CvaCapitalApproach CvaCapitalApproach { get; }

    /// <summary>EUR, converted with the ECB reference rates of the as-of date (ADR-002 §9).</summary>
    public Currency ReportingCurrency { get; }

    /// <summary>Limit utilisation that raises an alert, 0.80 by default; a breach is fixed at 100 %.</summary>
    public decimal LimitAlertThreshold { get; }

    /// <summary>The defaults of ADR-002.</summary>
    public static RiskMethodologyOptions Default { get; } =
        new(0.975m, AllocationRule.Euler, CvaCapitalApproach.ReducedBaCva, Currency.Eur, 0.80m);

    public static Result<RiskMethodologyOptions> Create(
        decimal pfeQuantile,
        AllocationRule allocationRule,
        CvaCapitalApproach cvaCapitalApproach,
        Currency reportingCurrency,
        decimal limitAlertThreshold)
    {
        if (pfeQuantile is <= 0.5m or >= 1m)
        {
            return Error.Validation("methodology.invalid_pfe_quantile", "The PFE quantile must be strictly between 0.5 and 1.");
        }

        if (limitAlertThreshold is <= 0m or >= 1m)
        {
            return Error.Validation("methodology.invalid_alert_threshold", "The limit alert threshold must be strictly between 0 and 1.");
        }

        if (!Enum.IsDefined(allocationRule) || !Enum.IsDefined(cvaCapitalApproach) || reportingCurrency == default)
        {
            return Error.Validation("methodology.invalid_option", "Unknown allocation rule, CVA approach or currency.");
        }

        return new RiskMethodologyOptions(pfeQuantile, allocationRule, cvaCapitalApproach, reportingCurrency, limitAlertThreshold);
    }
}

/// <summary>
/// A version of the methodology as defined in the code catalogue: every result stores its id and
/// <see cref="ParametersSha256"/>.
/// </summary>
/// <param name="Id">Catalogue identifier.</param>
/// <param name="Status">Current life-cycle status.</param>
/// <param name="Options">Methodological choices.</param>
/// <param name="ParametersSha256">Lower-case hex SHA-256 of the canonical parameters.</param>
/// <param name="RegulatoryReferenceDate">Date whose regulation applies (CCF, output floor), the date of the recalculation (ADR-002 §14).</param>
public sealed record MethodVersion(
    MethodVersionId Id,
    MethodVersionStatus Status,
    RiskMethodologyOptions Options,
    string ParametersSha256,
    DateOnly RegulatoryReferenceDate)
{
    /// <summary>Only an active version exposes its results (no result is visible before approval).</summary>
    public bool ResultsVisible => Status is MethodVersionStatus.Active or MethodVersionStatus.Retired;
}
