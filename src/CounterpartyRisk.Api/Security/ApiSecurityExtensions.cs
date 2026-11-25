using System.Collections.Frozen;
using CounterpartyRisk.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CounterpartyRisk.Api.Security;

/// <summary>
/// JWT bearer validation of the access token forwarded by the BFF (defence in depth: the API is
/// internal but revalidates every token) and ABAC policies built from <see cref="Policies"/>.
/// </summary>
internal static class ApiSecurityExtensions
{
    /// <summary>Keycloak puts the token purpose in the <c>typ</c> claim: "Bearer" for access tokens, "ID" for ID tokens.</summary>
    private const string AccessTokenType = "Bearer";

    public static WebApplicationBuilder AddApiSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddOptions<ApiAuthenticationOptions>()
            .Bind(builder.Configuration.GetSection(ApiAuthenticationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(o => Uri.TryCreate(o.Authority, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || builder.Environment.IsDevelopment()),
                "Authentication:Authority must be an absolute HTTPS URL (HTTP is accepted in Development only).")
            .Validate(o => o.RequireMultiFactor || builder.Environment.IsDevelopment(),
                "Authentication:RequireMultiFactor can be disabled in Development only.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<ApiAuthenticationOptions>, IHostEnvironment>((jwt, auth, environment) =>
                ConfigureJwtBearer(jwt, auth.Value, environment));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<IAuthorizationHandler, CapabilityAuthorizationHandler>();

        var baseline = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new ValidPrincipalRequirement())
            .Build();

        var authorization = services.AddAuthorizationBuilder()
            // Deny by default: an endpoint without a policy still needs a valid, consistent principal.
            .SetFallbackPolicy(baseline)
            .SetDefaultPolicy(baseline);

        foreach (var capability in Policies.AllCapabilities)
        {
            authorization.AddPolicy(Policies.PolicyName(capability), policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new ValidPrincipalRequirement(), new CapabilityRequirement(capability)));
        }

        return builder;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, ApiAuthenticationOptions auth, IHostEnvironment environment)
    {
        jwt.Authority = auth.Authority;
        jwt.Audience = auth.Audience;
        jwt.RequireHttpsMetadata = !environment.IsDevelopment();
        jwt.MapInboundClaims = false;
        jwt.SaveToken = false;
        // No validation detail in WWW-Authenticate (it would tell an attacker which check failed).
        jwt.IncludeErrorDetails = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.Authority,
            ValidateAudience = true,
            ValidAudience = auth.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            // ASVS 5.0 appendix C: RSASSA-PKCS1-v1_5 (RS256) is disallowed; PS256 only (SEC-110).
            ValidAlgorithms = [SecurityAlgorithms.RsaSsaPssSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = CcrClaimTypes.Name,
            RoleClaimType = CcrClaimTypes.Roles,
        };
        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var principal = context.Principal;
                // SEC-113: an ID token (typ "ID") or any other token type is not an access token.
                if (principal?.FindFirst("typ")?.Value != AccessTokenType)
                {
                    context.Fail("Not an access token.");
                }
                else if (principal.FindFirst("azp")?.Value != auth.AuthorizedParty)
                {
                    context.Fail("Token not issued to the BFF.");
                }

                return Task.CompletedTask;
            },
        };
    }

    /// <summary>Multi-factor policy of the host, derived from the validated options.</summary>
    public static MultiFactorPolicy ToMultiFactorPolicy(this ApiAuthenticationOptions options) =>
        new(options.RequireMultiFactor, options.MultiFactorAcrValues.ToFrozenSet(StringComparer.Ordinal));
}
