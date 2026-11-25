using System.Collections.Frozen;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Session security of the BFF (#9): confidential OIDC client (code flow + PKCE S256) with Keycloak,
/// server-side session behind the <c>__Host-ccr</c> cookie, Data Protection and antiforgery.
/// The browser never receives a token (SEC-031, SEC-116).
/// </summary>
internal static class BffSecurityExtensions
{
    public const string SessionCookieName = "__Host-ccr";

    public static WebApplicationBuilder AddBffSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var environment = builder.Environment;

        services.AddOptions<BffAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(BffAuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.Authority, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment()),
                "Authentication:Authority must be an absolute HTTPS URL (HTTP is accepted in Development only).")
            .Validate(o => BffAuthenticationOptions.IsAcceptableSecret(o.ClientSecret),
                "Authentication:ClientSecret is missing, too short or a placeholder.")
            .Validate(o => o.RequireMultiFactor || environment.IsDevelopment(),
                "Authentication:RequireMultiFactor can be disabled in Development only.")
            .Validate(o => o.IdleTimeout > TimeSpan.Zero && o.IdleTimeout <= o.AbsoluteLifetime,
                "Authentication:IdleTimeout must be positive and not exceed AbsoluteLifetime.")
            .ValidateOnStart();

        AddDataProtection(builder);

        services.TryAddSingleton(TimeProvider.System);
        services.AddDistributedMemoryCache();
        services.AddSingleton<ServerSideTicketStore>();
        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieSessionStore>();
        services.AddSingleton<SessionValidator>();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // XHR calls get 401, never a redirect: the SPA calls /bff/login explicitly.
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect();

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<BffAuthenticationOptions>, SessionValidator, TimeProvider>(ConfigureCookie);
        services.AddOptions<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme)
            .Configure<IOptions<BffAuthenticationOptions>, TimeProvider>((oidc, auth, time) =>
                ConfigureOpenIdConnect(oidc, auth.Value, time, environment));

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build())
            // Deny by default: only the SPA assets, /bff/login and the back-channel logout are anonymous.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(CookieAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().Build());

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-ccr-af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.HeaderName = BffEndpoints.AntiforgeryHeader;
        });

        return builder;
    }

    private static void ConfigureCookie(
        CookieAuthenticationOptions cookie,
        IOptions<BffAuthenticationOptions> auth,
        SessionValidator validator,
        TimeProvider timeProvider)
    {
        // __Host- prefix: Secure, Path=/, no Domain attribute (SEC-028, SEC-030).
        cookie.Cookie.Name = SessionCookieName;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.Path = "/";
        cookie.Cookie.Domain = null;
        cookie.Cookie.IsEssential = true;
        cookie.ExpireTimeSpan = auth.Value.IdleTimeout;
        cookie.SlidingExpiration = true;
        cookie.TimeProvider = timeProvider;
        cookie.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnRedirectToLogout = context =>
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            },
            OnSigningIn = context =>
            {
                SessionStart.Stamp(context.Properties, timeProvider.GetUtcNow());
                return Task.CompletedTask;
            },
            OnValidatePrincipal = validator.ValidateAsync,
        };
    }

    private static void ConfigureOpenIdConnect(
        OpenIdConnectOptions oidc,
        BffAuthenticationOptions auth,
        TimeProvider timeProvider,
        IHostEnvironment environment)
    {
        // Normalized: a trailing slash in configuration must not change the metadata address. The issuer itself is
        // taken from the discovery metadata by the handler.
        oidc.Authority = auth.Authority.TrimEnd('/');
        oidc.ClientId = auth.ClientId;
        oidc.ClientSecret = auth.ClientSecret;
        oidc.RequireHttpsMetadata = !environment.IsDevelopment();
        oidc.ResponseType = OpenIdConnectResponseType.Code;
        // Query response mode: the callback is a top-level GET, so the correlation and nonce cookies can stay Lax.
        oidc.ResponseMode = OpenIdConnectResponseMode.Query;
        oidc.UsePkce = true;
        oidc.SaveTokens = true;
        oidc.MapInboundClaims = false;
        oidc.GetClaimsFromUserInfoEndpoint = false;
        oidc.DisableTelemetry = true;
        oidc.Scope.Clear();
        oidc.Scope.Add(OpenIdConnectScope.OpenId);
        oidc.Scope.Add("profile");
        oidc.CallbackPath = "/signin-oidc";
        oidc.SignedOutCallbackPath = "/signout-callback-oidc";
        oidc.TimeProvider = timeProvider;
        oidc.TokenValidationParameters.NameClaimType = "preferred_username";
        oidc.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSsaPssSha256];
        oidc.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);

        // Handler-managed, short-lived cookies: documented exception to the __Host- prefix (SEC-029, SEC-030).
        oidc.CorrelationCookie.SameSite = SameSiteMode.Lax;
        oidc.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        oidc.CorrelationCookie.HttpOnly = true;
        oidc.NonceCookie.SameSite = SameSiteMode.Lax;
        oidc.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        oidc.NonceCookie.HttpOnly = true;

        var acceptedAcr = auth.MultiFactorAcrValues.ToFrozenSet(StringComparer.Ordinal);
        oidc.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                // Step-up: ask the realm for the OTP level (Conditional Level of Authentication, acr "mfa" = LoA 2).
                if (auth.RequireMultiFactor && acceptedAcr.Count > 0)
                {
                    context.ProtocolMessage.AcrValues = string.Join(' ', auth.MultiFactorAcrValues);
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                // SEC-083: a single-factor login is refused outside Development (missing acr = single factor).
                var acr = context.Principal?.FindFirst("acr")?.Value;
                if (auth.RequireMultiFactor && (acr is null || !acceptedAcr.Contains(acr)))
                {
                    context.Fail("Multi-factor authentication required.");
                }

                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                // Invalid state, replayed code, failed token validation: generic 400, no detail, no session.
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// Key ring of the BFF (<c>ccr-bff</c>, separate from the API key ring), AES-256-GCM (SEC-147).
    /// Keys must be persisted outside Development, otherwise sessions break on restart and cookies become
    /// unverifiable across instances (TM-INT-03). Until #11 provides <c>dpkeys.bff_key</c>, a directory is used.
    /// </summary>
    private static void AddDataProtection(WebApplicationBuilder builder)
    {
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("ccr-bff")
            .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
            {
                EncryptionAlgorithm = EncryptionAlgorithm.AES_256_GCM,
                ValidationAlgorithm = ValidationAlgorithm.HMACSHA256,
            });

        var keysPath = builder.Configuration["Bff:DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }
        else if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Bff:DataProtection:KeysPath is required outside Development (persisted, protected key ring).");
        }
    }
}
