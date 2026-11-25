using CounterpartyRisk.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace CounterpartyRisk.Infrastructure.Persistence.Configuration;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module DbContext on a least-privilege connection (one connection per role,
    /// docs/security/data-flow.md), with the legal entity scope interceptor (RLS) and without
    /// sensitive data logging. <see cref="ILegalEntityScope"/> defaults to the empty scope (fail
    /// closed) until the host registers its own (#9 for the API, #26 for the workers).
    /// </summary>
    public static IServiceCollection AddCounterpartyRiskDbContext<TContext>(
        this IServiceCollection services,
        string? connectionString,
        bool isDevelopment,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var hardened = PostgresConnectionPolicy.Harden(connectionString, isDevelopment);

        services.TryAddScoped<ILegalEntityScope, EmptyLegalEntityScope>();
        services.TryAddScoped<LegalEntityScopeInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            options.UseNpgsql(hardened, npgsql => configureNpgsql?.Invoke(npgsql));
            options.AddInterceptors(provider.GetRequiredService<LegalEntityScopeInterceptor>());
            // Never EnableSensitiveDataLogging: parameters carry amounts and LEIs (TM-TB4-05).
            options.EnableDetailedErrors(isDevelopment);
        });

        return services;
    }

    /// <summary>Readiness check of a PostgreSQL connection (tag it with the host readiness tag).</summary>
    public static IHealthChecksBuilder AddPostgresReadinessCheck(
        this IHealthChecksBuilder builder,
        string name,
        string? connectionString,
        bool isDevelopment,
        IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var hardened = PostgresConnectionPolicy.Harden(connectionString, isDevelopment);
        return builder.Add(new HealthCheckRegistration(
            name,
            _ => new PostgresReadinessCheck(hardened),
            HealthStatus.Unhealthy,
            tags,
            TimeSpan.FromSeconds(5)));
    }
}
