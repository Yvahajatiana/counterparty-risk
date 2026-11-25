namespace CounterpartyRisk.Api.Http;

/// <summary>
/// Group of endpoints contributed by a module (#31). Every endpoint must name a policy of
/// <c>Policies</c> (<c>RequireAuthorization(Policies.ReadExposure)</c>) and dispatch through
/// <c>IDispatcher</c>: the "every endpoint has a named policy" test enforces it.
/// </summary>
internal interface IEndpointModule
{
    void Map(RouteGroupBuilder api);
}

internal static class ApiEndpoints
{
    /// <summary>Versioned prefix of the business API; the BFF forwards <c>/api/**</c> unchanged.</summary>
    public const string Prefix = "/api/v1";

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup(Prefix);

        // Figures must never be cached by the browser or an intermediate proxy (TM-TB1-04).
        api.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context).ConfigureAwait(false);
        });

        foreach (var module in app.Services.GetServices<IEndpointModule>())
        {
            module.Map(api);
        }

        return app;
    }
}
