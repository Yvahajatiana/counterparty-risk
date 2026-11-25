using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// OIDC client and session settings (section <c>Authentication</c>). Validated at startup: the BFF refuses
/// to start without an issuer or with a missing, short or placeholder client secret (fail closed, SEC-165).
/// </summary>
internal sealed class BffAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Minimum length of the confidential client secret.</summary>
    public const int MinimumSecretLength = 32;

    /// <summary>Issuer of the realm, e.g. <c>https://idp.example/realms/ccr</c>.</summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = "ccr-bff";

    /// <summary>Confidential client secret: user-secrets, Aspire secret parameter or vault, never appsettings.</summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Idle timeout of the session (sliding, SEC-091).</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Absolute lifetime of the session, equal to the Keycloak SSO session max (SEC-092).</summary>
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(10);

    /// <summary>Access token refreshed server side when it expires within this margin.</summary>
    public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Concurrent sessions per user; the oldest is revoked beyond this limit (SEC-085).</summary>
    [Range(1, 10)]
    public int MaxConcurrentSessions { get; set; } = 3;

    /// <summary>Multi-factor login required (<c>acr</c> check, SEC-083); false is accepted in Development only.</summary>
    public bool RequireMultiFactor { get; set; } = true;

    [SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Bound from configuration.")]
    public List<string> MultiFactorAcrValues { get; set; } = [];

    /// <summary>Rejects empty, short and obvious placeholder secrets (<c>changeme</c>, <c>${...}</c>, <c>&lt;...&gt;</c>).</summary>
    public static bool IsAcceptableSecret(string? secret) =>
        secret is { Length: >= MinimumSecretLength }
        && !secret.Contains("changeme", StringComparison.OrdinalIgnoreCase)
        && !secret.Contains("${", StringComparison.Ordinal)
        && !(secret.StartsWith('<') && secret.EndsWith('>'))
        && secret.Distinct().Count() > 8;
}
