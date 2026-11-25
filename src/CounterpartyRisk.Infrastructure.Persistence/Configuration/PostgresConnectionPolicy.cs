using Npgsql;

namespace CounterpartyRisk.Infrastructure.Persistence.Configuration;

/// <summary>
/// Hardens a PostgreSQL connection string (TM-TB4-04, TM-TB4-05): TLS with full certificate
/// validation outside Development and no server error detail outside Development; an application
/// connection additionally never uses the owner, migration or superuser identity.
/// </summary>
public static class PostgresConnectionPolicy
{
    private static readonly string[] ForbiddenApplicationUsers = ["postgres", "cr_owner", "cr_migrator"];

    /// <summary>Application connection: transport rules plus a least-privilege cr_* identity.</summary>
    public static string Harden(string? connectionString, bool isDevelopment)
    {
        var builder = Parse(connectionString, isDevelopment);

        if (ForbiddenApplicationUsers.Contains(builder.Username, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "An application connection uses a least-privilege cr_* identity, never the owner, migrator or superuser.");
        }

        return builder.ConnectionString;
    }

    /// <summary>Transport rules only, for the migration bundle (which connects as cr_migrator).</summary>
    public static string HardenTransport(string? connectionString, bool isDevelopment) =>
        Parse(connectionString, isDevelopment).ConnectionString;

    private static NpgsqlConnectionStringBuilder Parse(string? connectionString, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The PostgreSQL connection string is missing (secret store or user-secrets).");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // The parser message may echo part of the connection string (password): not chained.
            throw new InvalidOperationException("The PostgreSQL connection string is malformed.");
        }

        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database) || string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException("The PostgreSQL connection string must name a host, a database and a service identity.");
        }

        if (!isDevelopment && builder.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException("Outside Development, PostgreSQL connections require SSL Mode=VerifyFull.");
        }

        // Server error details may carry row values (amounts, LEIs): Development only.
        builder.IncludeErrorDetail = isDevelopment;
        return builder;
    }
}
