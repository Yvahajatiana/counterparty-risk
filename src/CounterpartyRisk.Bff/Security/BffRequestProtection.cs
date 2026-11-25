using Microsoft.Net.Http.Headers;

namespace CounterpartyRisk.Bff.Security;

/// <summary>
/// Anti-CSRF and cache rules of the BFF (TM-TB1-02, TM-TB1-04, SEC-038..040):
/// <list type="bullet">
/// <item>every <c>/api/**</c> call, and <c>/bff/user</c> and <c>/bff/logout</c>, must carry <c>X-CSRF: 1</c>.
/// A cross-site page cannot set this header without a CORS preflight, and CORS is disabled; GET
/// requests are included (exports), so a navigation or an <c>&lt;img&gt;</c> never reaches the API.
/// The request is refused with 403 before authorization and before proxying;</item>
/// <item>those responses are never cached (<c>Cache-Control: no-store</c>).</item>
/// </list>
/// </summary>
internal static class BffRequestProtection
{
    public const string CsrfHeaderName = "X-CSRF";
    public const string CsrfHeaderValue = "1";

    public static WebApplication UseBffRequestProtection(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (IsProtected(context.Request.Path))
            {
                context.Response.Headers[HeaderNames.CacheControl] = "no-store";
                context.Response.Headers[HeaderNames.Pragma] = "no-cache";

                var values = context.Request.Headers[CsrfHeaderName];
                if (values.Count != 1 || !string.Equals(values[0], CsrfHeaderValue, StringComparison.Ordinal))
                {
                    // Empty body: UseStatusCodePages writes a generic ProblemDetails.
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });
        return app;
    }

    internal static bool IsProtected(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(BffEndpoints.UserPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(BffEndpoints.LogoutPath, StringComparison.OrdinalIgnoreCase);
}
