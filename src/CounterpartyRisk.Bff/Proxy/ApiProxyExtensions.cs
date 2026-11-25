using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.ServiceDiscovery;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace CounterpartyRisk.Bff.Proxy;

/// <summary>
/// YARP route <c>/api/{**catch-all}</c> to the internal API. The proxy drops every client credential or
/// identity header and attaches the access token from the server-side session (TM-TB3-10, SEC-046).
/// <c>/health/*</c> is never proxied: only <c>/api/**</c> matches.
/// </summary>
internal static class ApiProxyExtensions
{
    public const string RouteId = "api";
    public const string ClusterId = "api";
    private const string AccessTokenItem = "ccr.access_token";

    /// <summary>
    /// Client headers that must never reach the API. X-Forwarded-* are not listed: the default YARP
    /// transforms overwrite them with the values seen by the BFF (action Set).
    /// </summary>
    private static readonly string[] StrippedHeaders =
    [
        HeaderNames.Cookie,
        HeaderNames.Authorization,
        "X-CSRF",
        "X-XSRF-TOKEN",
        "X-Real-IP",
        "Forwarded",
    ];

    public static IServiceCollection AddApiProxy(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Aspire service discovery name, preferring HTTPS; overridable for other deployments.
        var address = configuration["Bff:ApiAddress"] ?? "https+http://api";

        // Only HTTPS endpoints are resolved outside Development (SEC-160, review note of #50).
        services.Configure<ServiceDiscoveryOptions>(options =>
        {
            options.AllowAllSchemes = false;
            options.AllowedSchemes = environment.IsDevelopment() ? ["https", "http"] : ["https"];
        });

        services.AddReverseProxy()
            .LoadFromMemory(
                [
                    new RouteConfig
                    {
                        RouteId = RouteId,
                        ClusterId = ClusterId,
                        AuthorizationPolicy = "Default",
                        Match = new RouteMatch { Path = "/api/{**catch-all}" },
                    },
                ],
                [
                    new ClusterConfig
                    {
                        ClusterId = ClusterId,
                        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
                        {
                            ["api"] = new() { Address = address },
                        },
                    },
                ])
            .AddServiceDiscoveryDestinationResolver()
            .AddTransforms(context =>
            {
                context.AddRequestTransform(transform =>
                {
                    var headers = transform.ProxyRequest.Headers;
                    foreach (var name in StrippedHeaders)
                    {
                        headers.Remove(name);
                    }

                    // Any identity header a client may try to inject (X-User-Id, X-User-Roles...).
                    foreach (var name in headers.Select(h => h.Key).Where(IsIdentityHeader).ToList())
                    {
                        headers.Remove(name);
                    }

                    if (transform.HttpContext.Items[AccessTokenItem] is string accessToken)
                    {
                        headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                    }

                    return ValueTask.CompletedTask;
                });

                // The API never sets cookies for the browser.
                context.AddResponseHeaderRemove(HeaderNames.SetCookie);
            });

        return services;
    }

    public static WebApplication MapApiProxy(this WebApplication app)
    {
        app.MapReverseProxy(pipeline =>
        {
            pipeline.Use(async (context, next) =>
            {
                // Fail closed: no server-side access token, no forwarding.
                var accessToken = await context.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "access_token")
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(accessToken))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                context.Items[AccessTokenItem] = accessToken;
                await next().ConfigureAwait(false);
            });
            pipeline.UseSessionAffinity();
            pipeline.UseLoadBalancing();
            pipeline.UsePassiveHealthChecks();
        });
        return app;
    }

    private static bool IsIdentityHeader(string name) =>
        name.StartsWith("X-User-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("X-Auth", StringComparison.OrdinalIgnoreCase);
}
