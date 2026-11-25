using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using CounterpartyRisk.Domain.Contracts.Measures;
using CounterpartyRisk.Domain.Contracts.Methodology;
using CounterpartyRisk.Domain.Contracts.Portfolio;
using CounterpartyRisk.Domain.Contracts.Quality;
using CounterpartyRisk.Infrastructure.Persistence;
using CounterpartyRisk.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CounterpartyRisk.IntegrationTests.Database;

/// <summary>Migration bundle behaviour and schema conventions (#11 acceptance criteria).</summary>
[Collection(PostgresDatabase.Name)]
[Trait("Category", "Database")]
[SuppressMessage("Security", "CA2100", Justification = "Constant SQL or the committed migration script.")]
public sealed partial class MigrationTests(PostgresFixture db)
{
    private const string CountObjects =
        """
        SELECT (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname !~ '^pg_')
             + (SELECT count(*) FROM pg_policy) + (SELECT count(*) FROM pg_proc WHERE pronamespace = 'meta'::regnamespace)
        """;

    public static TheoryData<string, string, Type, bool> EnumChecks => new()
    {
        { "ref.counterparty", "ck_counterparty_exposure_class", typeof(SaExposureClass), true },
        { "ref.counterparty", "ck_counterparty_lineage", typeof(LineageStatus), true },
        { "ref.product_line", "ck_product_line_asset_class", typeof(SaCcrAssetClass), true },
        { "portfolio.csa", "ck_csa_collateral_status", typeof(CollateralStatus), true },
        { "portfolio.netting_set", "ck_netting_set_clearing_mode", typeof(ClearingMode), true },
        { "portfolio.trade", "ck_trade_type", typeof(TradeType), true },
        { "portfolio.trade", "ck_trade_asset_class", typeof(SaCcrAssetClass), true },
        { "portfolio.collateral_position", "ck_collateral_position_direction", typeof(CollateralDirection), true },
        { "portfolio.collateral_position", "ck_collateral_position_margin_type", typeof(MarginType), true },
        { "calc.exposure_result", "ck_exposure_result_level", typeof(ExposureLevel), true },
        { "calc.exposure_result", "ck_exposure_result_measure", typeof(MeasureCode), true },
        { "calc.saccr_result", "ck_saccr_result_approach", typeof(RegulatoryApproach), true },
        { "calc.profile_point", "ck_profile_point_measure", typeof(MeasureCode), false },
        { "calc.rwa_result", "ck_rwa_result_approach", typeof(RegulatoryApproach), true },
        { "calc.rwa_result", "ck_rwa_result_exposure_class", typeof(SaExposureClass), true },
        { "calc.cva_result", "ck_cva_result_approach", typeof(CvaCapitalApproach), true },
        { "calc.cva_result", "ck_cva_result_measure", typeof(MeasureCode), false },
        { "calc.allocation_result", "ck_allocation_result_measure", typeof(MeasureCode), true },
        { "calc.allocation_result", "ck_allocation_result_rule", typeof(AllocationRule), true },
        { "meta.method_version", "ck_method_version_cva_approach", typeof(CvaCapitalApproach), true },
        { "meta.method_version", "ck_method_version_allocation_rule", typeof(AllocationRule), true },
        { "meta.method_version", "ck_method_version_status", typeof(MethodVersionStatus), true },
    };

    [Fact]
    public async Task Migration_Creates_Every_Schema()
    {
        string[] expected = ["audit", "bench", "calc", "dpkeys", "financing", "market", "meta", "portfolio", "quartz", "ref", "reporting", "session", "staging"];

        var count = await db.ScalarAsync<long>(
            "cr_reader", "SELECT count(*) FROM pg_namespace WHERE nspname = ANY (@names)", null, ("names", expected));

        count.ShouldBe(expected.Length);
    }

    [Fact]
    public async Task Second_Run_Of_The_Migrations_Has_No_Effect()
    {
        var before = await db.ScalarAsync<long>("cr_reader", CountObjects);

        await db.MigrateAsync(PostgresFixture.DatabaseName);

        (await db.ScalarAsync<long>("cr_reader", CountObjects)).ShouldBe(before);
        await using var context = CounterpartyRiskDbContext.ForMigrations(db.ConnectionString("cr_migrator"));
        (await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Migration_Refuses_To_Run_Without_The_Migrator_Identity()
    {
        await db.CreateDatabaseAsync("ccr_superuser");
        await db.BootstrapRolesAsync("ccr_superuser");

        // Run as the superuser: objects would not be owned by cr_owner, the first script stops it.
        await using var context = CounterpartyRiskDbContext.ForMigrations(db.ConnectionString("postgres", "ccr_superuser"));
        var exception = await Should.ThrowAsync<PostgresException>(() => context.Database.MigrateAsync(TestContext.Current.CancellationToken));

        exception.MessageText.ShouldContain("cr_owner");
    }

    [Fact]
    public async Task Committed_Idempotent_Script_Is_Up_To_Date()
    {
        await using var context = CounterpartyRiskDbContext.ForMigrations(db.ConnectionString("cr_migrator"));
        var generated = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        var committed = await File.ReadAllTextAsync(ScriptPath, TestContext.Current.CancellationToken);

        Normalize(committed).ShouldBe(Normalize(generated), "Regenerate db/migrations/idempotent.sql (see db/README.md)");
    }

    [Fact]
    public async Task Committed_Idempotent_Script_Applies_Twice_On_An_Empty_Database()
    {
        await db.CreateDatabaseAsync("ccr_script");
        await db.BootstrapRolesAsync("ccr_script");
        var script = await File.ReadAllTextAsync(ScriptPath, TestContext.Current.CancellationToken);

        for (var run = 0; run < 2; run++)
        {
            await using var connection = await db.OpenAsync("cr_migrator", "ccr_script");
            await using var command = new NpgsqlCommand(script, connection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var check = await db.OpenAsync("cr_reader", "ccr_script");
        await using var query = new NpgsqlCommand(
            "SELECT relforcerowsecurity FROM pg_class WHERE oid = 'portfolio.trade'::regclass", check);
        ((bool)(await query.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(EnumChecks))]
    public async Task Enum_Checks_Match_The_Domain_Contracts(string table, string constraint, Type enumType, bool exhaustive)
    {
        var definition = await db.ScalarAsync<string>(
            "cr_reader",
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = @table::regclass AND conname = @name",
            null,
            ("table", table),
            ("name", constraint));

        var allowed = Literal().Matches(definition).Select(m => m.Groups[1].Value).Order().ToList();
        var names = Enum.GetNames(enumType).Order().ToList();

        allowed.ShouldNotBeEmpty();
        if (exhaustive)
        {
            allowed.ShouldBe(names);
        }
        else
        {
            allowed.ShouldBeSubsetOf(names);
        }
    }

    [Fact]
    public async Task Readiness_Check_Is_Healthy_With_The_Reader_Identity()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresReadinessCheck("postgres", db.ConnectionString("cr_reader"), isDevelopment: true, ["ready"]);
        await using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single();

        var result = await registration.Factory(provider).CheckHealthAsync(
            new HealthCheckContext { Registration = registration }, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    private static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Database", "Sql", "idempotent.sql");

    private static string Normalize(string sql) => sql.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    [GeneratedRegex("'([A-Za-z]+)'::")]
    private static partial Regex Literal();
}
