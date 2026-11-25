using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using CounterpartyRisk.Domain.Contracts.Identifiers;
using CounterpartyRisk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CounterpartyRisk.IntegrationTests.Database;

/// <summary>
/// Shared PostgreSQL 17 container (#11), reusable by every integration test: db/roles.sql applied
/// by the superuser, a random password per run for the service identities, migrations applied
/// with cr_migrator, then a small synthetic portfolio on two legal entities (A and B).
/// </summary>
[SuppressMessage("Security", "CA2100", Justification = "Test infrastructure: SQL comes from the repository scripts or constants; values are parameters.")]
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>docker.io/library/postgres:17.11, same digest as the AppHost (ADR-001).</summary>
    public const string Image = "postgres:17.11@sha256:748a18a54d3842d43bc156f0840d78029b8d349e0feca98c12b9c4ce3404a6b3";

    public const string DatabaseName = "ccr";

    public static readonly string[] LoginRoles =
    [
        "cr_migrator", "cr_reader", "cr_writer", "cr_limit_writer", "cr_governance_writer", "cr_audit_writer",
        "cr_audit_reader", "cr_audit_verifier", "cr_api_keys", "cr_bff_keys", "cr_bff_session", "cr_scheduler",
        "cr_partition_job",
    ];

    // Generated per run, never stored: the container is thrown away with it.
    private readonly string _roleSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private PostgreSqlContainer? _container;

    public string AdminConnectionString { get; private set; } = string.Empty;

    /// <summary>First day of the current month of the database clock (UTC).</summary>
    public DateOnly CurrentMonth { get; private set; }

    /// <summary>As-of date of the seeded portfolio (current month).</summary>
    public DateOnly AsOfDate => CurrentMonth.AddDays(9);

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder(Image)
            .WithDatabase(DatabaseName)
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        AdminConnectionString = _container.GetConnectionString();

        await BootstrapRolesAsync(DatabaseName).ConfigureAwait(false);
        await MigrateAsync(DatabaseName).ConfigureAwait(false);

        CurrentMonth = await ScalarAsync<DateOnly>("cr_reader", "SELECT date_trunc('month', current_date)::date").ConfigureAwait(false);
        await SeedAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    public string ConnectionString(string role, string database = DatabaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = role,
            Database = database,
            IncludeErrorDetail = true,
        };

        // The superuser keeps the container credential; service identities use the run secret.
        if (role != "postgres")
        {
            builder["Password"] = _roleSecret;
        }

        return builder.ConnectionString;
    }

    public async Task<NpgsqlConnection> OpenAsync(string role, string database = DatabaseName)
    {
        var connection = new NpgsqlConnection(ConnectionString(role, database));
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    /// <summary>Applies db/roles.sql as the superuser and gives every login role the run password.</summary>
    public async Task BootstrapRolesAsync(string database)
    {
        var roles = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", "Sql", "roles.sql")).ConfigureAwait(false);
        await using var connection = await OpenAsync("postgres", database).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(roles, connection))
        {
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using (var function = new NpgsqlCommand(
            """
            CREATE FUNCTION pg_temp.set_password(p_role text, p_password text) RETURNS void
            LANGUAGE plpgsql AS $$ BEGIN EXECUTE format('ALTER ROLE %I PASSWORD %L', p_role, p_password); END $$
            """,
            connection))
        {
            await function.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var role in LoginRoles)
        {
            await using var command = new NpgsqlCommand("SELECT pg_temp.set_password(@role, @secret)", connection);
            command.Parameters.AddWithValue("role", role);
            command.Parameters.AddWithValue("secret", _roleSecret);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public async Task MigrateAsync(string database)
    {
        var context = CounterpartyRiskDbContext.ForMigrations(ConnectionString("cr_migrator", database), isDevelopment: true);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.MigrateAsync().ConfigureAwait(false);
        }
    }

    public async Task CreateDatabaseAsync(string database)
    {
        await using var connection = await OpenAsync("postgres").ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>Runs a statement as <paramref name="role"/> in one transaction carrying <paramref name="scope"/>.</summary>
    public async Task<int> ExecuteAsync(string role, string sql, IEnumerable<LegalEntityId>? scope = null, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(role).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await SetScopeAsync(connection, transaction, scope).ConfigureAwait(false);
        await using var command = Command(sql, connection, transaction, parameters);
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    public async Task<T> ScalarAsync<T>(string role, string sql, IEnumerable<LegalEntityId>? scope = null, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(role).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await SetScopeAsync(connection, transaction, scope).ConfigureAwait(false);
        await using var command = Command(sql, connection, transaction, parameters);
        var value = await command.ExecuteScalarAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return (T)value!;
    }

    public static async Task SetScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<LegalEntityId>? scope)
    {
        if (scope is null)
        {
            return;
        }

        await using var command = new NpgsqlCommand("SELECT set_config('app.legal_entity_ids', @scope, true)", connection, transaction);
        command.Parameters.AddWithValue("scope", string.Join(',', scope.Select(id => id.ToString())));
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    private async Task SeedAsync()
    {
        LegalEntityId[] all = [Seed.EntityA, Seed.EntityB];

        // meta.data_source is seeded by the migration (synthetic-portfolio included).
        await ExecuteAsync(
            "cr_writer",
            """
            INSERT INTO ref.country (code, name) VALUES ('FR', 'France'), ('DE', 'Germany');
            INSERT INTO ref.legal_entity (id, lei, code, name, country_code) VALUES
                (@entityA, '969500AAAAAAAAAAAA01', 'LE-A', 'Entity A', 'FR'),
                (@entityB, '969500BBBBBBBBBBBB02', 'LE-B', 'Entity B', 'DE');
            INSERT INTO ref.counterparty (id, lei, legal_name, exposure_class, lineage, source_id)
            VALUES (@counterparty, '969500CCCCCCCCCCCC03', 'Counterparty C', 'Institution', 'Synthetic', 'synthetic-portfolio');
            INSERT INTO ref.business_line (id, code, name) VALUES (@businessLine, 'BL-RATES', 'Rates');
            INSERT INTO ref.desk (id, business_line_id, code, name) VALUES (@desk, @businessLine, 'DESK-1', 'Desk 1');
            INSERT INTO portfolio.netting_set (id, legal_entity_id, counterparty_id, agreement_type, margin_agreement, clearing_mode, lineage, source_id)
            VALUES (@nettingSetA, @entityA, @counterparty, 'Isda', 'None', 'Bilateral', 'Synthetic', 'synthetic-portfolio'),
                   (@nettingSetB, @entityB, @counterparty, 'Isda', 'None', 'Bilateral', 'Synthetic', 'synthetic-portfolio');
            INSERT INTO portfolio.trade (id, as_of_date, netting_set_id, legal_entity_id, counterparty_id, trade_type, asset_class,
                business_line_id, desk_id, notional, notional_currency, mtm, mtm_currency, trade_date, maturity_date, lineage, source_id)
            SELECT gen_random_uuid(), @asOf, ns.id, ns.legal_entity_id, ns.counterparty_id, 'Otc', 'InterestRate',
                   @businessLine, @desk, 1000000, 'EUR', 1000, 'EUR', @asOf, @asOf + 3650, 'Synthetic', 'synthetic-portfolio'
            FROM portfolio.netting_set ns
            CROSS JOIN LATERAL generate_series(1, CASE WHEN ns.legal_entity_id = @entityA THEN @tradesA ELSE @tradesB END)
            """,
            all,
            ("entityA", Seed.EntityA.Value),
            ("entityB", Seed.EntityB.Value),
            ("counterparty", Seed.Counterparty),
            ("businessLine", Seed.BusinessLine),
            ("desk", Seed.Desk),
            ("nettingSetA", Seed.NettingSetA),
            ("nettingSetB", Seed.NettingSetB),
            ("asOf", AsOfDate),
            ("tradesA", Seed.TradesA),
            ("tradesB", Seed.TradesB)).ConfigureAwait(false);

        await ExecuteAsync(
            "cr_governance_writer",
            """
            INSERT INTO meta.method_version (id, framework, cva_approach, pfe_quantile, allocation_rule, reporting_currency,
                limit_alert_threshold, parameters, parameters_sha256, regulatory_reference_date, effective_from, status, published_by)
            VALUES (@id, 'CRR3', 'ReducedBaCva', 0.975, 'Euler', 'EUR', 0.80, '{}'::jsonb, repeat('a', 64), @asOf, @asOf, 'Computing', 'publisher-sub')
            """,
            null,
            ("id", Seed.MethodVersion),
            ("asOf", AsOfDate)).ConfigureAwait(false);
    }
}

/// <summary>Identifiers of the seeded synthetic portfolio.</summary>
public static class Seed
{
    public const int TradesA = 2;

    public const int TradesB = 3;

    public const string MethodVersion = "ccr-2026.1";

    public static readonly LegalEntityId EntityA = new(Guid.Parse("0193a3c0-0000-7000-8000-0000000000a1"));

    public static readonly LegalEntityId EntityB = new(Guid.Parse("0193a3c0-0000-7000-8000-0000000000b2"));

    public static readonly Guid Counterparty = Guid.Parse("0193a3c0-0000-7000-8000-0000000000c3");

    public static readonly Guid BusinessLine = Guid.Parse("0193a3c0-0000-7000-8000-0000000000d4");

    public static readonly Guid Desk = Guid.Parse("0193a3c0-0000-7000-8000-0000000000e5");

    public static readonly Guid NettingSetA = Guid.Parse("0193a3c0-0000-7000-8000-0000000000f6");

    public static readonly Guid NettingSetB = Guid.Parse("0193a3c0-0000-7000-8000-0000000000f7");
}

[CollectionDefinition(Name)]
public sealed class PostgresDatabase : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
