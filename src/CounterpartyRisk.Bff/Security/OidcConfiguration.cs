using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CounterpartyRisk.Bff.Security;

/// <summary>Issuer metadata of the realm, from the configuration manager (cached, refreshable) when available.</summary>
internal static class OidcConfiguration
{
    public static async Task<OpenIdConnectConfiguration> GetAsync(OpenIdConnectOptions oidc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oidc);
        if (oidc.ConfigurationManager is { } manager)
        {
            return await manager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }

        return oidc.Configuration ?? throw new InvalidOperationException("OpenID Connect metadata is not configured.");
    }

    /// <summary>Asks for fresh metadata (key rotation); the manager throttles the actual download.</summary>
    public static void RequestRefresh(OpenIdConnectOptions oidc)
    {
        ArgumentNullException.ThrowIfNull(oidc);
        oidc.ConfigurationManager?.RequestRefresh();
    }
}
