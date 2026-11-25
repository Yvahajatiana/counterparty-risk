using System.Text.RegularExpressions;
using CounterpartyRisk.Infrastructure.Persistence.Migrations;

namespace CounterpartyRisk.Infrastructure.Persistence.UnitTests;

public partial class MigrationScriptTests
{
    [Fact]
    public void Initial_migration_runs_its_numbered_scripts_in_order()
    {
        SqlScripts.FilesOf(InitialSchema.MigrationId).ShouldBe(
        [
            "01_foundation.sql",
            "02_meta_and_reference.sql",
            "03_portfolio_market_bench.sql",
            "04_calc.sql",
            "05_platform.sql",
            "06_partitions_and_security.sql",
        ]);
    }

    [Fact]
    public void Unknown_script_is_reported()
    {
        Should.Throw<InvalidOperationException>(() => SqlScripts.Load(InitialSchema.MigrationId, "99_missing.sql"));
    }

    [Fact]
    public void Scripts_never_grant_superuser_or_rls_bypass()
    {
        foreach (var file in SqlScripts.FilesOf(InitialSchema.MigrationId))
        {
            var sql = SqlScripts.Load(InitialSchema.MigrationId, file);

            sql.ShouldNotBeNullOrWhiteSpace();
            ForbiddenAttribute().IsMatch(sql).ShouldBeFalse($"{file} grants SUPERUSER or BYPASSRLS");
            sql.ShouldNotContain("DISABLE ROW LEVEL SECURITY", Case.Insensitive);
            sql.ShouldNotContain("NO FORCE ROW LEVEL SECURITY", Case.Insensitive);
        }
    }

    [Fact]
    public void Partition_maintenance_functions_are_security_definer_with_pinned_search_path()
    {
        var sql = SqlScripts.Load(InitialSchema.MigrationId, "01_foundation.sql");

        foreach (var function in new[] { "meta.ensure_partitions", "meta.purge_partitions" })
        {
            var body = sql[sql.IndexOf($"CREATE OR REPLACE FUNCTION {function}", StringComparison.Ordinal)..];
            body = body[..body.IndexOf("AS $fn$", StringComparison.Ordinal)];

            body.ShouldContain("SECURITY DEFINER");
            body.ShouldContain("SET search_path = pg_catalog, pg_temp");
        }
    }

    // SUPERUSER / BYPASSRLS as an attribute, not prefixed by NO.
    [GeneratedRegex(@"(?<!NO)\b(SUPERUSER|BYPASSRLS)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenAttribute();
}
