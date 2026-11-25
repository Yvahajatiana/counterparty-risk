using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// BFF session endpoints consumed by the Angular shell (#13). Contract documented in docs/security/bff-contract.md.
/// </summary>
internal static partial class BffEndpoints
{
    public const string LoginPath = "/bff/login";
    public const string LogoutPath = "/bff/logout";
    public const string UserPath = "/bff/user";
    public const string BackchannelLogoutPath = "/bff/backchannel-logout";

    /// <summary>Readable cookie carrying the antiforgery request token for <c>POST /bff/logout</c>.</summary>
    public const string AntiforgeryRequestCookie = "__Host-ccr-xsrf";
    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    private const string BackchannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    public static WebApplication MapBffEndpoints(this WebApplication app)
    {
        app.MapGet(LoginPath, Login).AllowAnonymous();
        app.MapGet(UserPath, GetUser).RequireAuthorization();
        app.MapPost(LogoutPath, LogoutAsync).RequireAuthorization().DisableAntiforgery();
        app.MapPost(BackchannelLogoutPath, BackchannelLogoutAsync).AllowAnonymous().DisableAntiforgery();
        return app;
    }

    /// <summary>
    /// Starts the code flow + PKCE. Only local, relative return URLs are accepted (SEC-043, TM-TB1-09).
    /// </summary>
    private static IResult Login(HttpContext context, string? returnUrl)
    {
        var target = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl;
        if (!IsLocalUrl(target))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid return URL");
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            return TypedResults.LocalRedirect(target);
        }

        return TypedResults.Challenge(
            new AuthenticationProperties { RedirectUri = target },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// Minimal profile (API3:2023 BOPLA): no token, no technical attribute. Also issues the antiforgery
    /// request token used by <c>POST /bff/logout</c>.
    /// </summary>
    private static async Task<IResult> GetUser(HttpContext context, IAntiforgery antiforgery, IOptions<BffAuthenticationOptions> options)
    {
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Properties is null)
        {
            return TypedResults.Unauthorized();
        }

        var tokens = antiforgery.GetAndStoreTokens(context);
#pragma warning disable SCS0009 // Deliberately readable: the SPA echoes it in X-XSRF-TOKEN (double-submit pattern).
        // Useless on its own: it only validates together with the HttpOnly antiforgery cookie and the session.
        context.Response.Cookies.Append(AntiforgeryRequestCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
            Path = "/",
        });
#pragma warning restore SCS0009

        var accessToken = result.Properties.GetTokenValue("access_token");
        var claims = ReadAccessTokenClaims(accessToken);
        var issued = SessionStart.Read(result.Properties) ?? result.Properties.IssuedUtc ?? DateTimeOffset.UtcNow;
        var absoluteEnd = issued + options.Value.AbsoluteLifetime;
        var slidingEnd = result.Properties.ExpiresUtc ?? absoluteEnd;

        return TypedResults.Ok(new BffUser(
            result.Principal?.FindFirst("name")?.Value ?? result.Principal?.Identity?.Name ?? string.Empty,
            claims.Roles,
            claims.LegalEntities,
            claims.BusinessLines,
            slidingEnd < absoluteEnd ? slidingEnd : absoluteEnd));
    }

    /// <summary>
    /// Ends the session: antiforgery token checked (in addition to X-CSRF and SameSite=Strict), ticket
    /// removed server side, Keycloak SSO session ended through the back channel (no token in the browser,
    /// not even an <c>id_token_hint</c> in a redirect), and browser state cleared (SEC-181).
    /// </summary>
    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        ILoggerFactory loggerFactory)
    {
        if (!await antiforgery.IsRequestValidAsync(context).ConfigureAwait(false))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
        }

        var refreshToken = await context.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "refresh_token").ConfigureAwait(false);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        context.Response.Cookies.Delete(AntiforgeryRequestCookie, new CookieOptions { Secure = true, Path = "/" });

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await EndIdentityProviderSessionAsync(oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme), refreshToken, loggerFactory, context.RequestAborted)
                .ConfigureAwait(false);
        }

        context.Response.Headers["Clear-Site-Data"] = "\"cache\", \"cookies\", \"storage\"";
        return TypedResults.NoContent();
    }

    /// <summary>
    /// OIDC back-channel logout (TM-TB2-06, SEC-138): the logout token is validated (signature PS256,
    /// issuer, audience, lifetime, type, <c>events</c>, no <c>nonce</c>) before the sessions are revoked.
    /// </summary>
    private static async Task<IResult> BackchannelLogoutAsync(
        HttpContext context,
        IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
        ServerSideTicketStore store,
        IDistributedCache cache)
    {
        if (!context.Request.HasFormContentType)
        {
            return TypedResults.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var logoutToken = form["logout_token"].ToString();
        if (string.IsNullOrEmpty(logoutToken) || logoutToken.Length > 8192)
        {
            return TypedResults.BadRequest();
        }

        var oidc = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var token = await ValidateLogoutTokenAsync(oidc, logoutToken, context.RequestAborted).ConfigureAwait(false);
        if (token is null || !await TryConsumeJtiAsync(cache, token, context.RequestAborted).ConfigureAwait(false))
        {
            return TypedResults.BadRequest();
        }

        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        if (token.TryGetClaim("sid", out var sid) && !string.IsNullOrEmpty(sid.Value))
        {
            await store.RevokeSessionAsync(sid.Value, context.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            await store.RevokeSubjectAsync(token.Subject, context.RequestAborted).ConfigureAwait(false);
        }

        return TypedResults.Ok();
    }

    internal static async Task<JsonWebToken?> ValidateLogoutTokenAsync(OpenIdConnectOptions oidc, string logoutToken, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ValidateOnceAsync(oidc, logoutToken, cancellationToken).ConfigureAwait(false);
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // Unknown kid: the realm may have rotated its keys. Refresh the metadata once and retry.
                OidcConfiguration.RequestRefresh(oidc);
                result = await ValidateOnceAsync(oidc, logoutToken, cancellationToken).ConfigureAwait(false);
            }

            if (!result.IsValid || result.SecurityToken is not JsonWebToken token)
            {
                return null;
            }

            // A logout token carries the back-channel logout event and a jti, never a nonce, and a sid or a sub.
            if (token.TryGetClaim("nonce", out _) || !HasBackchannelLogoutEvent(token) || string.IsNullOrEmpty(token.Id))
            {
                return null;
            }

            var hasSid = token.TryGetClaim("sid", out var sid) && !string.IsNullOrEmpty(sid.Value);
            return hasSid || !string.IsNullOrEmpty(token.Subject) ? token : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<TokenValidationResult> ValidateOnceAsync(OpenIdConnectOptions oidc, string logoutToken, CancellationToken cancellationToken)
    {
        var configuration = await OidcConfiguration.GetAsync(oidc, cancellationToken).ConfigureAwait(false);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = oidc.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSsaPssSha256],
            // OIDC Back-Channel Logout 1.0: explicit type "logout+jwt"; Keycloak versions that do not set it send "JWT".
            ValidTypes = ["logout+jwt", "JWT"],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        return await new JsonWebTokenHandler().ValidateTokenAsync(logoutToken, parameters).ConfigureAwait(false);
    }

    /// <summary>Replay protection: a logout token (jti) is accepted once during its lifetime.</summary>
    private static async Task<bool> TryConsumeJtiAsync(IDistributedCache cache, JsonWebToken token, CancellationToken cancellationToken)
    {
        var key = "ccr:logout-jti:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Issuer + "|" + token.Id)));
        if (await cache.GetAsync(key, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        var expires = new DateTimeOffset(token.ValidTo, TimeSpan.Zero).AddSeconds(30);
        await cache.SetAsync(key, [1], new DistributedCacheEntryOptions { AbsoluteExpiration = expires }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool HasBackchannelLogoutEvent(JsonWebToken token)
    {
        if (!token.TryGetClaim("events", out var events))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(events.Value);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(BackchannelLogoutEvent, out var value)
                && value.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task EndIdentityProviderSessionAsync(
        OpenIdConnectOptions oidc,
        string refreshToken,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await OidcConfiguration.GetAsync(oidc, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(configuration.EndSessionEndpoint))
            {
                return;
            }

            // Keycloak ends the SSO session when its logout endpoint receives the refresh token from the client.
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.EndSessionEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = oidc.ClientId!,
                    ["client_secret"] = oidc.ClientSecret!,
                    ["refresh_token"] = refreshToken,
                }),
            };
            using var response = await oidc.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogIdentityProviderLogoutFailed(loggerFactory.CreateLogger(typeof(BffEndpoints)), (int)response.StatusCode);
            }
        }
        catch (HttpRequestException)
        {
            // The local session is already revoked; the SSO session expires on its own (idle 30 min).
            LogIdentityProviderLogoutFailed(loggerFactory.CreateLogger(typeof(BffEndpoints)), 0);
        }
        catch (InvalidOperationException)
        {
            LogIdentityProviderLogoutFailed(loggerFactory.CreateLogger(typeof(BffEndpoints)), 0);
        }
        catch (OperationCanceledException)
        {
            // Timeout of the identity provider: the local logout still completes (204 + Clear-Site-Data).
            LogIdentityProviderLogoutFailed(loggerFactory.CreateLogger(typeof(BffEndpoints)), 0);
        }
    }

    /// <summary>
    /// Claims of the access token for the profile. The token comes from the token endpoint over the
    /// back channel and never left the server: it is decoded, not revalidated (the API validates it).
    /// </summary>
    private static (IReadOnlyList<string> Roles, IReadOnlyList<string> LegalEntities, IReadOnlyList<string> BusinessLines) ReadAccessTokenClaims(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return ([], [], []);
        }

        try
        {
            var token = new JsonWebToken(accessToken);
            return (
                Values(token, "roles").Where(r => r.StartsWith("ccr-", StringComparison.Ordinal)).ToList(),
                Values(token, "ccr_legal_entities"),
                Values(token, "ccr_business_lines"));
        }
        catch (ArgumentException)
        {
            return ([], [], []);
        }
    }

    private static List<string> Values(JsonWebToken token, string type) =>
        token.Claims.Where(c => c.Type == type).Select(c => c.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>Same rule as <c>IUrlHelper.IsLocalUrl</c>: "/path" but never "//host" or "/\host".</summary>
    internal static bool IsLocalUrl(string url) =>
        url.Length > 0
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Any(char.IsControl)
        && url.Length <= 2048;

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Identity provider logout failed with status {StatusCode}")]
    private static partial void LogIdentityProviderLogoutFailed(ILogger logger, int statusCode);

    internal sealed record BffUser(
        string Name,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> LegalEntities,
        IReadOnlyList<string> BusinessLines,
        DateTimeOffset SessionExpiresAt);
}
