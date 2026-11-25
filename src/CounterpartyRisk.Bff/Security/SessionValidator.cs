using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Runs on every request carrying a session cookie (<c>OnValidatePrincipal</c>):
/// <list type="bullet">
/// <item>ends the session after the absolute lifetime (10 h, SEC-092), whatever the sliding expiry says;</item>
/// <item>refreshes the access token server side shortly before it expires (refresh token rotation);</item>
/// <item>ends the session when the refresh fails (account disabled, SSO session ended, SEC-094).</item>
/// </list>
/// </summary>
internal sealed partial class SessionValidator(
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
    IOptions<BffAuthenticationOptions> options,
    TimeProvider timeProvider,
    ILogger<SessionValidator> logger)
{
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = timeProvider.GetUtcNow();
        var settings = options.Value;

        if (SessionStart.Read(context.Properties) is not { } started || now - started > settings.AbsoluteLifetime)
        {
            await EndSessionAsync(context).ConfigureAwait(false);
            return;
        }

        var expiresAt = context.Properties.GetTokenValue("expires_at");
        if (expiresAt is not null
            && DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var accessTokenExpiry)
            && accessTokenExpiry - settings.RefreshMargin > now)
        {
            return;
        }

        var refreshed = await RefreshAsync(context.Properties.GetTokenValue("refresh_token"), context.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (refreshed is null)
        {
            LogRefreshFailed();
            await EndSessionAsync(context).ConfigureAwait(false);
            return;
        }

        context.Properties.UpdateTokenValue("access_token", refreshed.AccessToken);
        if (!string.IsNullOrEmpty(refreshed.RefreshToken))
        {
            context.Properties.UpdateTokenValue("refresh_token", refreshed.RefreshToken);
        }

        context.Properties.UpdateTokenValue(
            "expires_at",
            now.AddSeconds(refreshed.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private async Task<TokenResponse?> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return null;
        }

        var oidc = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        try
        {
            var configuration = oidc.Configuration
                ?? await oidc.ConfigurationManager!.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = oidc.ClientId!,
                    ["client_secret"] = oidc.ClientSecret!,
                }),
            };
            using var response = await oidc.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken).ConfigureAwait(false);
            return token is { AccessToken.Length: > 0, ExpiresIn: > 0 } ? token : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // Metadata unavailable: fail closed.
            return null;
        }
    }

    private static async Task EndSessionAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        // Removes the server-side ticket and expires the cookie.
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Access token refresh failed: session ended")]
    private partial void LogRefreshFailed();

    internal sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
