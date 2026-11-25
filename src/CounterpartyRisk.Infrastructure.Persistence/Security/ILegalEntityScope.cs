using CounterpartyRisk.Domain.Contracts.Identifiers;

namespace CounterpartyRisk.Infrastructure.Persistence.Security;

/// <summary>
/// Legal entities the current principal may read or write. It feeds PostgreSQL row-level security
/// (second line of defence behind the ABAC of #9, ADR-001 rule 8). An empty scope sees nothing.
/// The API implementation reads the token claims (#9); workers use an explicit all-entities scope (#26).
/// </summary>
public interface ILegalEntityScope
{
    IReadOnlyCollection<LegalEntityId> LegalEntityIds { get; }
}

/// <summary>Default scope until #9 provides the claims-based one: nothing is visible (fail closed).</summary>
public sealed class EmptyLegalEntityScope : ILegalEntityScope
{
    public IReadOnlyCollection<LegalEntityId> LegalEntityIds => [];
}

/// <summary>Explicit scope, for jobs that work on a known list of entities and for tests.</summary>
public sealed class FixedLegalEntityScope : ILegalEntityScope
{
    public FixedLegalEntityScope(IEnumerable<LegalEntityId> legalEntityIds)
    {
        ArgumentNullException.ThrowIfNull(legalEntityIds);
        LegalEntityIds = [.. legalEntityIds.Distinct()];
    }

    public IReadOnlyCollection<LegalEntityId> LegalEntityIds { get; }
}
