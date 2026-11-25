using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace CounterpartyRisk.Api.Security;

/// <summary>
/// Access token validation settings (section <c>Authentication</c>). Validated at startup: the API
/// refuses to start without an issuer (fail closed).
/// </summary>
internal sealed class ApiAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Issuer of the realm, e.g. <c>https://idp.example/realms/ccr</c>; also the metadata authority.</summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    /// <summary>Audience added by the Keycloak audience mapper (SEC-114).</summary>
    [Required]
    public string Audience { get; set; } = "ccr-api";

    /// <summary>Only tokens obtained by the BFF client are accepted (<c>azp</c> claim).</summary>
    [Required]
    public string AuthorizedParty { get; set; } = "ccr-bff";

    /// <summary>Multi-factor level for sensitive capabilities; false is accepted in Development only.</summary>
    public bool RequireMultiFactor { get; set; } = true;

    /// <summary><c>acr</c> values proving a multi-factor login (Keycloak level of authentication mapping).</summary>
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Bound from configuration.")]
    public List<string> MultiFactorAcrValues { get; set; } = [];
}
