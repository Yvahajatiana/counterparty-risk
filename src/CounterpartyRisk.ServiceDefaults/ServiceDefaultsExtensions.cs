using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CounterpartyRisk.ServiceDefaults;

/// <summary>
/// Cross-cutting defaults shared by Api, Bff and Workers: OpenTelemetry, log redaction,
/// health checks, HTTP resilience and service discovery.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public const string HealthLivePath = "/health/live";
    public const string HealthReadyPath = "/health/ready";

    /// <summary>Tag of checks that only verify the process is alive (no dependency).</summary>
    public const string LiveTag = "live";

    /// <summary>Tag of checks that verify dependencies (PostgreSQL #11, Keycloak discovery #9).</summary>
    public const string ReadyTag = "ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();
        builder.AddLogRedaction();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retrying POST, PUT, PATCH or DELETE could apply an operation twice when only the response was lost.
            http.AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
            http.AddServiceDiscovery();
        });

        // Do not disclose the server technology (ASVS V14.3).
        builder.Services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);

        return builder;
    }

    /// <summary>
    /// Host filtering is configured per environment (AppHost, deployment), never "*" (ASVS 4.1, DNS rebinding):
    /// outside Development the process refuses to start without an explicit list of host names.
    /// </summary>
    public static TBuilder RequireAllowedHosts<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var hosts = (builder.Configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = hosts.Length > 0 && hosts.All(h => h != "*");
        if (!valid && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("AllowedHosts must list the expected host names (never \"*\") outside Development.");
        }

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes are noise in traces.
                    options.Filter = context => !IsHealthRequest(context);
                })
                // Query strings stay redacted (instrumentation default); no header is recorded.
                .AddHttpClientInstrumentation());

        // Exporter enabled only when an OTLP endpoint is provided (Aspire dashboard, collector).
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Values classified with the project taxonomy ([PersonalData], [SensitiveData]) are erased
    /// before reaching any log sink.
    /// </summary>
    public static TBuilder AddLogRedaction<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.EnableRedaction();
        builder.Services.AddRedaction(redaction => redaction.SetFallbackRedactor<ErasingRedactor>());
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health/live</c> (process only) and <c>/health/ready</c> (all checks).
    /// Responses only contain the aggregated status, never exception details.
    /// These endpoints are internal: the BFF must not proxy them. They are the only anonymous endpoints
    /// (the authorization fallback policy of the API and the BFF denies everything else).
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(HealthLivePath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag),
        }).AllowAnonymous();

        app.MapHealthChecks(HealthReadyPath, new HealthCheckOptions
        {
            Predicate = _ => true,
        }).AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Generic RFC 7807 responses: no stack trace or exception message outside Development.
    /// </summary>
    public static IServiceCollection AddDefaultProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                // The framework default traceId (W3C id of the current activity) is kept for correlation.
                context.ProblemDetails.Extensions.Remove("exception");
            };
        });

        return services;
    }

    private static bool IsHealthRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments(HealthLivePath, StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments(HealthReadyPath, StringComparison.OrdinalIgnoreCase);
}
