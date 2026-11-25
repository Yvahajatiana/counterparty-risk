using System.Collections.Frozen;
using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Data scope of a user (legal entities by LEI, business lines by code), read exclusively from the
/// access token (Keycloak attributes <c>ccr_legal_entities</c> and <c>ccr_business_lines</c>). No client
/// parameter can widen it: a requested filter is only accepted when it is included in it.
/// </summary>
/// <remarks>
/// LEIs and codes are kept as strings until the reference data of #11 maps them to
/// <c>LegalEntityId</c> / <c>BusinessLineId</c> (and to the <c>DataScope</c> contract of #5).
/// </remarks>
public sealed record UserScope
{
    private UserScope(IReadOnlySet<string> legalEntities, bool allLegalEntities, IReadOnlySet<string> businessLines, bool allBusinessLines)
    {
        LegalEntities = legalEntities;
        AllLegalEntities = allLegalEntities;
        BusinessLines = businessLines;
        AllBusinessLines = allBusinessLines;
    }

    /// <summary>No legal entity and no business line: every data request is refused (403).</summary>
    public static UserScope Empty { get; } = new(FrozenSet<string>.Empty, false, FrozenSet<string>.Empty, false);

    /// <summary>Explicit legal entities (empty when <see cref="AllLegalEntities"/>).</summary>
    public IReadOnlySet<string> LegalEntities { get; }

    public bool AllLegalEntities { get; }

    /// <summary>Explicit business lines (empty when <see cref="AllBusinessLines"/>).</summary>
    public IReadOnlySet<string> BusinessLines { get; }

    public bool AllBusinessLines { get; }

    /// <summary>Every legal entity and every business line (direction).</summary>
    public bool IsGlobal => AllLegalEntities && AllBusinessLines;

    /// <summary>True when the scope cannot match any row: no legal entity or no business line.</summary>
    public bool IsEmpty => (!AllLegalEntities && LegalEntities.Count == 0) || (!AllBusinessLines && BusinessLines.Count == 0);

    /// <summary>
    /// True when measures computed per netting set (PFE, EAD, EEPE, RWA) must be served through the
    /// business-line allocation rule (#25, rule R7) instead of the full netting set amount.
    /// </summary>
    public bool RequiresAllocatedMeasures => !AllBusinessLines;

    public static UserScope Create(IEnumerable<string> legalEntities, bool allLegalEntities, IEnumerable<string> businessLines, bool allBusinessLines)
    {
        ArgumentNullException.ThrowIfNull(legalEntities);
        ArgumentNullException.ThrowIfNull(businessLines);
        return new UserScope(
            allLegalEntities ? FrozenSet<string>.Empty : legalEntities.ToFrozenSet(StringComparer.Ordinal),
            allLegalEntities,
            allBusinessLines ? FrozenSet<string>.Empty : businessLines.ToFrozenSet(StringComparer.Ordinal),
            allBusinessLines);
    }

    public bool CoversLegalEntity(string legalEntity) => AllLegalEntities || LegalEntities.Contains(legalEntity);

    public bool CoversBusinessLine(string businessLine) => AllBusinessLines || BusinessLines.Contains(businessLine);

    /// <summary>Ownership check for a resource addressed by identifier: out of scope must become a 404.</summary>
    public bool Covers(string legalEntity, string businessLine) => CoversLegalEntity(legalEntity) && CoversBusinessLine(businessLine);

    /// <summary>
    /// Effective scope of a request: a missing filter is replaced by this scope, a filter included in it
    /// is kept, and a filter outside it (or a wildcard) is refused (403, never silently filtered).
    /// </summary>
    public Result<UserScope> Narrow(IReadOnlyCollection<string>? requestedLegalEntities, IReadOnlyCollection<string>? requestedBusinessLines)
    {
        if (IsEmpty)
        {
            return AuthorizationErrors.EmptyScope;
        }

        var legalEntities = NarrowAxis(requestedLegalEntities, AllLegalEntities, LegalEntities, out var allLegalEntities);
        var businessLines = NarrowAxis(requestedBusinessLines, AllBusinessLines, BusinessLines, out var allBusinessLines);
        if (legalEntities is null || businessLines is null)
        {
            return AuthorizationErrors.OutOfScope;
        }

        return new UserScope(legalEntities, allLegalEntities, businessLines, allBusinessLines);
    }

    private static IReadOnlySet<string>? NarrowAxis(
        IReadOnlyCollection<string>? requested,
        bool all,
        IReadOnlySet<string> granted,
        out bool effectiveAll)
    {
        if (requested is null || requested.Count == 0)
        {
            effectiveAll = all;
            return granted;
        }

        effectiveAll = false;
        var set = requested.ToFrozenSet(StringComparer.Ordinal);
        foreach (var value in set)
        {
            // A client never asks for "everything": the wildcard only exists in the token.
            if (string.IsNullOrWhiteSpace(value) || value == CcrClaimTypes.Wildcard || (!all && !granted.Contains(value)))
            {
                return null;
            }
        }

        return set;
    }
}
