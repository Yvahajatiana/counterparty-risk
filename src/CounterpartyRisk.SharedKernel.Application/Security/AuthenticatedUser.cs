namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>Reason why an authenticated principal is refused before any capability check (fail closed).</summary>
public enum PrincipalFault
{
    None = 0,

    /// <summary>The token has no <c>sub</c> or no <c>iss</c>.</summary>
    MissingIdentity = 1,

    /// <summary>The token carries an incompatible role pair (segregation of duties, section 7.3).</summary>
    IncompatibleRoles = 2,

    /// <summary>A scope claim is malformed (not a LEI, not a business line code).</summary>
    InvalidScopeClaim = 3,

    /// <summary>A front-office account with a wildcard scope (section 7.4): its scope is mandatory.</summary>
    FrontOfficeWildcardScope = 4,
}

/// <summary>
/// User of the current request, built from the validated access token only.
/// <see cref="Issuer"/> + <see cref="Subject"/> identify the user (audit and rate limiting, #10).
/// </summary>
public sealed record AuthenticatedUser(
    string Issuer,
    string Subject,
    IReadOnlySet<string> Roles,
    IReadOnlySet<Capability> Capabilities,
    UserScope Scope,
    bool MultiFactor,
    PrincipalFault Fault)
{
    public bool IsValid => Fault == PrincipalFault.None;

    public bool Has(Capability capability) => IsValid && Capabilities.Contains(capability);
}

/// <summary>
/// User of the current request, provided by the host (HTTP request in the API, system identity in
/// the workers, #19). Read by <c>AuthorizationBehavior</c>; never built from client input.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user, or null for an anonymous caller.</summary>
    AuthenticatedUser? User { get; }
}
