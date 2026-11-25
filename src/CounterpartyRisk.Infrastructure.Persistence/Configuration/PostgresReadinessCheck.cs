using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CounterpartyRisk.Infrastructure.Persistence.Configuration;

/// <summary>Readiness probe: opens a connection with the component identity and runs <c>SELECT 1</c>.</summary>
internal sealed class PostgresReadinessCheck : IHealthCheck
{
    private readonly string _connectionString;

    public PostgresReadinessCheck(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = new NpgsqlConnection(_connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var command = new NpgsqlCommand("SELECT 1", connection);
                await using (command.ConfigureAwait(false))
                {
                    await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            // Status only: the exception (host, user) is not exposed by the health endpoint.
            return new HealthCheckResult(context?.Registration.FailureStatus ?? HealthStatus.Unhealthy);
        }
    }
}
