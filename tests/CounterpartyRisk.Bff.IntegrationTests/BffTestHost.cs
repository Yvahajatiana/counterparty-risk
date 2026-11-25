using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Yarp.ReverseProxy.Forwarder;

namespace CounterpartyRisk.Bff.IntegrationTests;

/// <summary>
/// BFF in Production mode with a fake identity provider (static metadata + back-channel handler) and a
/// fake Api (YARP forwarder client). <c>/test/signin</c> creates a session exactly as the OIDC callback does
/// (cookie sign-in with saved tokens), so session, CSRF, proxy and revocation rules run for real.
/// </summary>
public sealed class BffFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://idp.test/realms/ccr";
    public const string TokenEndpoint = "https://idp.test/realms/ccr/protocol/openid-connect/token";
    public const string EndSessionEndpoint = "https://idp.test/realms/ccr/protocol/openid-connect/logout";

    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "ccr-bff-tests", Guid.NewGuid().ToString("N"));

    private static readonly string[] LegalEntities = ["CCRSYNTHPARENTFR0057"];
    private static readonly string[] BusinessLines = ["MARKETS"];

    public static RsaSecurityKey SigningKey { get; } = new(RSA.Create(3072)) { KeyId = "realm-key" };

    public FakeIdentityProvider IdentityProvider { get; } = new();

    public FakeApi Api { get; } = new();

    public TestTimeProvider Time { get; } = new();

    /// <summary>Issuer metadata; a key added with <see cref="RotatingConfigurationManager.PendingKey"/> appears after a refresh.</summary>
    public RotatingConfigurationManager Metadata { get; } = new(new OpenIdConnectConfiguration
    {
        Issuer = Issuer,
        AuthorizationEndpoint = Issuer + "/protocol/openid-connect/auth",
        TokenEndpoint = TokenEndpoint,
        EndSessionEndpoint = EndSessionEndpoint,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Production");
        builder.UseSetting("Authentication:Authority", Issuer);
        builder.UseSetting("Authentication:ClientSecret", "s3cr3t-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
        builder.UseSetting("Authentication:MultiFactorAcrValues:0", "mfa");
        builder.UseSetting("Bff:DataProtection:KeysPath", _keysPath);
        builder.UseSetting("Bff:ApiAddress", "https://api.internal");
        builder.UseSetting("AllowedHosts", "localhost");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IForwarderHttpClientFactory>(new FakeApiClientFactory(Api));
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                // Like production: metadata comes from the configuration manager (cached, refreshable).
                options.Configuration = null;
                options.ConfigurationManager = Metadata;
                options.Backchannel = new HttpClient(IdentityProvider);
            });
            services.AddSingleton<IStartupFilter, TestSignInStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_keysPath))
        {
            Directory.Delete(_keysPath, recursive: true);
        }
    }

    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost"),
        });

    /// <summary>Access token as issued by the realm (only decoded by the BFF).</summary>
    public static string AccessToken(string subject = "user-1", string[]? roles = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "ccr-api",
            Claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["typ"] = "Bearer",
                ["azp"] = "ccr-bff",
                ["roles"] = roles ?? ["ccr-risk-manager", "offline_access"],
                ["ccr_legal_entities"] = LegalEntities,
                ["ccr_business_lines"] = BusinessLines,
            },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSsaPssSha256),
        });

    /// <summary>Back-channel logout token (OIDC Back-Channel Logout 1.0).</summary>
    public static string LogoutToken(string? sid, string subject = "user-1", bool withNonce = false, bool withEvent = true, SecurityKey? key = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["jti"] = Guid.NewGuid().ToString() };
        if (sid is not null)
        {
            claims["sid"] = sid;
        }

        if (withEvent)
        {
            claims["events"] = new Dictionary<string, object> { ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>() };
        }

        if (withNonce)
        {
            claims["nonce"] = "n";
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "ccr-bff",
            TokenType = "logout+jwt",
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(2),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSsaPssSha256),
        });
    }
}

/// <summary>Creates a session like the OIDC callback: <c>GET /test/signin?sub=..&amp;sid=..</c>.</summary>
internal sealed class TestSignInStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (!context.Request.Path.Equals("/test/signin", StringComparison.Ordinal))
            {
                await nextMiddleware(context);
                return;
            }

            var time = context.RequestServices.GetRequiredService<TimeProvider>();
            var subject = context.Request.Query["sub"].ToString();
            var sid = context.Request.Query["sid"].ToString();
            var expiresIn = int.TryParse(context.Request.Query["expiresIn"], out var seconds) ? seconds : 300;

            var identity = new ClaimsIdentity(
                [new Claim("sub", subject), new Claim("sid", sid), new Claim("name", "Test User"), new Claim("preferred_username", subject)],
                "oidc",
                "preferred_username",
                "roles");
            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = BffFactory.AccessToken(subject) },
                new AuthenticationToken { Name = "refresh_token", Value = "refresh-" + subject },
                new AuthenticationToken { Name = "id_token", Value = "id-token-" + subject },
                new AuthenticationToken { Name = "expires_at", Value = time.GetUtcNow().AddSeconds(expiresIn).ToString("o", System.Globalization.CultureInfo.InvariantCulture) },
            ]);

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        next(app);
    };
}

public sealed class TestTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>Configuration manager whose signing keys change only after <see cref="RequestRefresh"/> (key rotation).</summary>
public sealed class RotatingConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private readonly OpenIdConnectConfiguration _configuration;
    private bool _refreshRequested;

    public RotatingConfigurationManager(OpenIdConnectConfiguration configuration)
    {
        _configuration = configuration;
        _configuration.SigningKeys.Add(BffFactory.SigningKey);
    }

    public SecurityKey? PendingKey { get; set; }

    public int Refreshes { get; private set; }

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        if (_refreshRequested && PendingKey is not null)
        {
            _configuration.SigningKeys.Add(PendingKey);
            PendingKey = null;
        }

        _refreshRequested = false;
        return Task.FromResult(_configuration);
    }

    public void RequestRefresh()
    {
        Refreshes++;
        _refreshRequested = true;
    }
}

/// <summary>
/// Token and logout endpoints of the identity provider. Refresh tokens rotate and cannot be reused
/// (<c>revokeRefreshToken=true</c>, <c>refreshTokenMaxReuse=0</c>), like the realm.
/// </summary>
public sealed class FakeIdentityProvider : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, bool> _usedRefreshTokens = new(StringComparer.Ordinal);
    private int _issued;

    public ConcurrentQueue<(string Uri, Dictionary<string, string> Form)> Requests { get; } = new();

    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Simulates an HttpClient timeout of the identity provider.</summary>
    public bool TimeOut { get; set; }

    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    public string RefreshedAccessToken { get; set; } = BffFactory.AccessToken("refreshed");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1].Replace('+', ' ') : string.Empty));
        Requests.Enqueue((request.RequestUri!.ToString(), form));
        if (TimeOut)
        {
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
        }

        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, cancellationToken);
        }

        if (request.RequestUri!.ToString() == BffFactory.TokenEndpoint)
        {
            var reused = form.TryGetValue("refresh_token", out var presented) && !_usedRefreshTokens.TryAdd(presented, true);
            if (TokenStatus != HttpStatusCode.OK || reused)
            {
                return new HttpResponseMessage(TokenStatus) { Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json") };
            }

            var rotated = "rotated-" + Interlocked.Increment(ref _issued);
            var json = $$"""{"access_token":"{{RefreshedAccessToken}}","refresh_token":"{{rotated}}","expires_in":300,"token_type":"Bearer"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }
}

/// <summary>Records what the proxy forwards to the Api.</summary>
public sealed class FakeApi : HttpMessageHandler
{
    public ConcurrentQueue<(string Path, Dictionary<string, string> Headers)> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Enqueue((request.RequestUri!.PathAndQuery, headers));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json") };
        response.Headers.Add("Set-Cookie", "api-cookie=leak; Path=/");
        return Task.FromResult(response);
    }
}

internal sealed class FakeApiClientFactory(FakeApi api) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(api, disposeHandler: false);
}

/// <summary>Minimal cookie jar: the test client never sends cookies on its own.</summary>
public sealed class CookieJar
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public IReadOnlyList<string> LastSetCookies { get; private set; } = [];

    public string? this[string name] => _cookies.TryGetValue(name, out var value) ? value : null;

    public void Capture(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        LastSetCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
        foreach (var header in LastSetCookies)
        {
            var pair = header.Split(';')[0];
            var index = pair.IndexOf('=', StringComparison.Ordinal);
            var name = pair[..index];
            var value = pair[(index + 1)..];
            if (value.Length == 0 || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
            {
                _cookies.Remove(name);
            }
            else
            {
                _cookies[name] = value;
            }
        }
    }

    public void Set(string name, string value) => _cookies[name] = value;

    public string Header() => string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
}
