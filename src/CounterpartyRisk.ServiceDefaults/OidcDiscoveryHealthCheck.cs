using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CounterpartyRisk.ServiceDefaults;

/// <summary>
/// Readiness of the identity provider: the OpenID Connect discovery document of the realm answers.
/// Only the status is reported, never the response body or an exception message.
/// </summary>
internal sealed class OidcDiscoveryHealthCheck(IHttpClientFactory httpClientFactory, Uri discoveryUri) : IHealthCheck
{
    public const string HttpClientName = "oidc-discovery-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(discoveryUri, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Identity provider discovery unavailable.");
        }
        catch (HttpRequestException)
        {
            return HealthCheckResult.Unhealthy("Identity provider discovery unreachable.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Identity provider discovery timed out.");
        }
    }
}

public static class OidcDiscoveryHealthCheckExtensions
{
    /// <summary>
    /// Adds the <c>keycloak-discovery</c> readiness check (tag <see cref="ServiceDefaultsExtensions.ReadyTag"/>)
    /// for the given realm authority. A missing or relative authority registers nothing: the host's own
    /// options validation already refuses to start in that case.
    /// </summary>
    public static IServiceCollection AddOidcDiscoveryHealthCheck(this IServiceCollection services, string? authority)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri))
        {
            return services;
        }

        var discovery = new Uri(authorityUri.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        services.AddHttpClient(OidcDiscoveryHealthCheck.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "keycloak-discovery",
            sp => new OidcDiscoveryHealthCheck(sp.GetRequiredService<IHttpClientFactory>(), discovery),
            HealthStatus.Unhealthy,
            [ServiceDefaultsExtensions.ReadyTag]));
        return services;
    }
}
