namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Effective data scope of the request being handled, set by <c>AuthorizationBehavior</c> before the
/// handler runs. Handlers filter with it and check the ownership of resources addressed by identifier
/// (<see cref="UserScope.Covers"/>, out of scope = <see cref="AuthorizationErrors.NotFound"/>).
/// </summary>
public interface IRequestScope
{
    /// <summary>Throws when no authorized request is in progress (fail closed).</summary>
    UserScope Current { get; }
}

/// <summary>
/// Legal entities the database session may read, consumed by the PostgreSQL RLS interceptor of #11
/// (<c>set_config('app.legal_entity_ids', ..., true)</c>), applied by <c>AuthorizationBehavior</c>. When nothing has been set, the interceptor
/// must not run any query (fail closed).
/// </summary>
public interface ILegalEntityScope
{
    /// <summary>False until an authorized request has set the scope.</summary>
    bool IsSet { get; }

    /// <summary>Every legal entity is readable (wildcard scope).</summary>
    bool AllLegalEntities { get; }

    /// <summary>Readable legal entities (LEI); empty when <see cref="AllLegalEntities"/> is true.</summary>
    IReadOnlySet<string> LegalEntityIds { get; }

    void Apply(UserScope scope);
}

/// <summary>Scoped holder (one per DI scope, i.e. per HTTP request or job execution).</summary>
internal sealed class RequestScopeHolder : IRequestScope, ILegalEntityScope
{
    private UserScope? _scope;

    public UserScope Current =>
        _scope ?? throw new InvalidOperationException("No authorized data scope: the request did not go through AuthorizationBehavior.");

    public bool IsSet => _scope is not null;

    public bool AllLegalEntities => _scope?.AllLegalEntities ?? false;

    public IReadOnlySet<string> LegalEntityIds => _scope?.LegalEntities ?? UserScope.Empty.LegalEntities;

    public void Apply(UserScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }
}
