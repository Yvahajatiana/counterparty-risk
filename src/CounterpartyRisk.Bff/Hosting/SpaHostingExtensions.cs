using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace CounterpartyRisk.Bff.Hosting;

/// <summary>
/// Serves the Angular build (wwwroot) under a strict Content-Security-Policy.
/// index.html is never served as a static file: every request gets a fresh nonce,
/// injected in place of <c>CSP_NONCE_PLACEHOLDER</c> and in the <c>style-src</c> directive.
/// </summary>
internal static class SpaHostingExtensions
{
    private const string NoncePlaceholder = "CSP_NONCE_PLACEHOLDER";
    private const string IndexFile = "index.html";

    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            // Default policy for every non-HTML response (API, assets); index.html overrides it.
            headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; frame-ancestors 'none'";
            await next(context).ConfigureAwait(false);
        });
        return app;
    }

    public static WebApplication MapSpa(this WebApplication app)
    {
        var index = new Lazy<string?>(() => ReadIndex(app.Environment));

        // Block direct access to the raw template, which still contains the placeholder.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/" + IndexFile, StringComparison.OrdinalIgnoreCase))
            {
                await WriteIndexAsync(context, index.Value).ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                // Angular output files are content-hashed, so they can be cached for a long time.
                ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
            },
        });

        app.MapFallback(context =>
        {
            // Unknown API routes must not fall back to the SPA.
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            return WriteIndexAsync(context, index.Value);
        });

        return app;
    }

    private static string? ReadIndex(IWebHostEnvironment environment)
    {
        var file = environment.WebRootFileProvider.GetFileInfo(IndexFile);
        if (!file.Exists)
        {
            return null;
        }
        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static async Task WriteIndexAsync(HttpContext context, string? template)
    {
        if (template is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Front end not built. Use 'npm start' in web/ or the Docker image.",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var headers = context.Response.Headers;
        headers[HeaderNames.ContentSecurityPolicy] = BuildCsp(nonce);
        headers[HeaderNames.CacheControl] = "no-store";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            template.Replace(NoncePlaceholder, nonce, StringComparison.Ordinal),
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Target policy documented in web/README.md.</summary>
    private static string BuildCsp(string nonce) => string.Join("; ",
        "default-src 'self'",
        "script-src 'self'",
        $"style-src 'self' 'nonce-{nonce}'",
        "font-src 'self'",
        "img-src 'self' data:",
        "connect-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'",
        "require-trusted-types-for 'script'",
        // Highcharts registers its own pass-through policy at load time: its markup is parsed in
        // an inert document and filtered by the Highcharts AST allow-list before reaching the DOM.
        // Labels and tooltips must still never render untrusted HTML (useHTML stays false).
        "trusted-types angular highcharts");
}
