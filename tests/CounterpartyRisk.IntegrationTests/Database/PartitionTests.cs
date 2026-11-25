using System.Diagnostics.CodeAnalysis;
using static CounterpartyRisk.IntegrationTests.Database.DatabaseAssert;

namespace CounterpartyRisk.IntegrationTests.Database;

/// <summary>Monthly partitions on as_of_date and their maintenance functions (TM-TB4-09, TM-TB4-14).</summary>
[Collection(PostgresDatabase.Name)]
[Trait("Category", "Database")]
[SuppressMessage("Security", "CA2100", Justification = "Constant SQL; values are parameters.")]
public sealed class PartitionTests(PostgresFixture db)
{
    private const string CountPartitions =
        """
        SELECT count(*) FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
        WHERE i.inhparent = 'portfolio.trade'::regclass
        """;

    [Fact]
    public async Task Migration_Creates_Partitions_From_M_Minus_1_To_M_Plus_3_And_Default()
    {
        foreach (var table in new[] { "portfolio.trade", "market.fx_rate", "calc.exposure_result" })
        {
            for (var offset = -1; offset <= 3; offset++)
            {
                var partition = $"{table}_p{db.CurrentMonth.AddMonths(offset):yyyyMM}";
                (await db.ScalarAsync<bool>("cr_reader", "SELECT to_regclass(@name) IS NOT NULL", null, ("name", partition)))
                    .ShouldBeTrue(partition);
            }

            (await db.ScalarAsync<bool>("cr_reader", "SELECT to_regclass(@name) IS NOT NULL", null, ("name", $"{table}_default")))
                .ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Row_Of_Month_M_Plus_2_Lands_In_Its_Partition_Not_In_Default()
    {
        var asOf = db.CurrentMonth.AddMonths(2).AddDays(14);
        await db.ExecuteAsync(
            "cr_writer",
            """
            INSERT INTO market.fx_rate (as_of_date, base_ccy, quote_ccy, rate, lineage, source_id)
            VALUES (@asOf, 'EUR', 'USD', 1.08, 'Real', 'synthetic-portfolio')
            """,
            null,
            ("asOf", asOf));

        var partition = await db.ScalarAsync<string>(
            "cr_reader", "SELECT tableoid::regclass::text FROM market.fx_rate WHERE as_of_date = @asOf", null, ("asOf", asOf));

        partition.ShouldBe($"market.fx_rate_p{asOf:yyyyMM}");
    }

    [Fact]
    public async Task Default_Partitions_Stay_Empty()
    {
        var rows = await db.ScalarAsync<long>(
            "cr_writer",
            """
            SELECT (SELECT count(*) FROM portfolio.trade_default) + (SELECT count(*) FROM market.fx_rate_default)
                 + (SELECT count(*) FROM calc.exposure_result_default)
            """,
            // The writer works with an explicit all-entities scope (#26).
            [Seed.EntityA, Seed.EntityB]);

        rows.ShouldBe(0);
    }

    [Fact]
    public async Task Ensure_Partitions_Is_Idempotent()
    {
        var before = await db.ScalarAsync<long>("cr_reader", CountPartitions);

        (await db.ScalarAsync<int>("cr_partition_job", "SELECT meta.ensure_partitions(@from, 5)", null, ("from", db.CurrentMonth.AddMonths(-1))))
            .ShouldBe(0);

        (await db.ScalarAsync<long>("cr_reader", CountPartitions)).ShouldBe(before);
    }

    [Theory]
    [InlineData("SELECT meta.ensure_partitions(current_date, 0)")]
    [InlineData("SELECT meta.ensure_partitions(current_date, 37)")]
    [InlineData("SELECT meta.ensure_partitions(NULL, 1)")]
    [InlineData("SELECT meta.ensure_partitions('1999-12-31', 1)")]
    [InlineData("SELECT meta.ensure_partitions((current_date + interval '2 years')::date, 1)")]
    public async Task Ensure_Partitions_Validates_Its_Parameters(string sql)
    {
        await Should.ThrowAsync<Npgsql.PostgresException>(() => db.ScalarAsync<int>("cr_partition_job", sql));
    }

    [Theory]
    [InlineData("audit", "audit_event")]
    [InlineData("portfolio", "netting_set")]
    [InlineData("ref'; DROP TABLE portfolio.trade; --", "trade")]
    public async Task Purge_Refuses_Audit_And_Unregistered_Tables(string schema, string table)
    {
        await Should.ThrowAsync<Npgsql.PostgresException>(() => db.ScalarAsync<int>(
            "cr_partition_job", "SELECT meta.purge_partitions(@schema, @table, DATE '2001-01-01')", null, ("schema", schema), ("table", table)));

        (await db.ScalarAsync<bool>("cr_reader", "SELECT to_regclass('portfolio.trade') IS NOT NULL")).ShouldBeTrue();
    }

    [Fact]
    public async Task Purge_Refuses_A_Date_Inside_The_Retention()
    {
        var exception = await Should.ThrowAsync<Npgsql.PostgresException>(() => db.ScalarAsync<int>(
            "cr_partition_job", "SELECT meta.purge_partitions('market', 'fx_rate', current_date)"));

        exception.MessageText.ShouldContain("retention");
    }

    [Fact]
    public async Task Purge_Drops_Only_Old_Partitions_Of_The_Table()
    {
        // Three years back: outside the 13-month minimum retention, inside the back-fill window.
        var old = db.CurrentMonth.AddMonths(-36);
        (await db.ScalarAsync<int>("cr_partition_job", "SELECT meta.ensure_partitions(@from, 2)", null, ("from", old))).ShouldBeGreaterThan(0);

        var dropped = await db.ScalarAsync<int>(
            "cr_partition_job", "SELECT meta.purge_partitions('market', 'yield_curve_point', @before)", null, ("before", old.AddMonths(1)));

        dropped.ShouldBe(1);
        (await Exists($"market.yield_curve_point_p{old:yyyyMM}")).ShouldBeFalse();
        (await Exists($"market.yield_curve_point_p{old.AddMonths(1):yyyyMM}")).ShouldBeTrue();
        (await Exists($"market.fx_rate_p{old:yyyyMM}")).ShouldBeTrue();
    }

    [Fact]
    public async Task New_Partitions_Of_A_Protected_Table_Inherit_Forced_Rls()
    {
        var old = db.CurrentMonth.AddMonths(-36);
        await db.ScalarAsync<int>("cr_partition_job", "SELECT meta.ensure_partitions(@from, 1)", null, ("from", old));

        (await db.ScalarAsync<bool>(
            "cr_reader",
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = @name::regclass",
            null,
            ("name", $"calc.exposure_result_p{old:yyyyMM}")))
            .ShouldBeTrue();
    }
    [Fact]
    public async Task Calc_Result_Without_Method_Version_Or_Lineage_Is_Rejected()
    {
        const string Insert =
            """
            INSERT INTO calc.exposure_result (id, as_of_date, legal_entity_id, counterparty_id, netting_set_id, exposure_level,
                measure_code, amount, currency, method_version_id, lineage)
            VALUES (gen_random_uuid(), @asOf, @entityA, @counterparty, @nettingSet, 'Ead', 'ExposureAtDefault', 10, 'EUR', {0}, {1})
            """;

        foreach (var (method, lineage) in new[] { ("NULL", "'Synthetic'"), ("@method", "NULL") })
        {
            await FailsWithAsync(NotNullViolation, () => db.ExecuteAsync(
                "cr_writer",
                string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, method, lineage),
                [Seed.EntityA],
                ("asOf", db.AsOfDate),
                ("entityA", Seed.EntityA.Value),
                ("counterparty", Seed.Counterparty),
                ("nettingSet", Seed.NettingSetA),
                ("method", Seed.MethodVersion)));
        }

        // The complete row is accepted (control).
        (await db.ExecuteAsync(
            "cr_writer",
            string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "@method", "'Synthetic'"),
            [Seed.EntityA],
            ("asOf", db.AsOfDate),
            ("entityA", Seed.EntityA.Value),
            ("counterparty", Seed.Counterparty),
            ("nettingSet", Seed.NettingSetA),
            ("method", Seed.MethodVersion))).ShouldBe(1);
    }

    [Fact]
    public async Task Trade_Must_Belong_To_The_Legal_Entity_Of_Its_Netting_Set()
    {
        // Netting set of A declared with entity B (both in scope): the composite foreign key refuses it.
        var exception = await Should.ThrowAsync<Npgsql.PostgresException>(() => db.ExecuteAsync(
            "cr_writer",
            """
            INSERT INTO portfolio.trade (id, as_of_date, netting_set_id, legal_entity_id, counterparty_id, trade_type, asset_class,
                business_line_id, desk_id, notional, notional_currency, mtm, mtm_currency, trade_date, maturity_date, lineage)
            VALUES (gen_random_uuid(), @asOf, @nettingSetA, @entityB, @counterparty, 'Otc', 'InterestRate', @businessLine, @desk,
                1, 'EUR', 0, 'EUR', @asOf, @asOf, 'Synthetic')
            """,
            [Seed.EntityA, Seed.EntityB],
            ("asOf", db.AsOfDate),
            ("nettingSetA", Seed.NettingSetA),
            ("entityB", Seed.EntityB.Value),
            ("counterparty", Seed.Counterparty),
            ("businessLine", Seed.BusinessLine),
            ("desk", Seed.Desk)));

        exception.SqlState.ShouldBe("23503");
    }

    [Fact]
    public async Task Ensure_Partitions_Moves_Rows_Already_In_Default_Into_The_New_Partition()
    {
        // Beyond M+3: no partition yet, so the rows land in DEFAULT.
        var month = db.CurrentMonth.AddMonths(6);
        var asOf = month.AddDays(3);
        await db.ExecuteAsync(
            "cr_writer",
            "INSERT INTO market.fx_rate (as_of_date, base_ccy, quote_ccy, rate, lineage, source_id) VALUES (@asOf, 'EUR', 'GBP', 0.85, 'Real', 'ecb-exr')",
            null,
            ("asOf", asOf));
        await db.ExecuteAsync(
            "cr_writer",
            """
            INSERT INTO portfolio.trade (id, as_of_date, netting_set_id, legal_entity_id, counterparty_id, trade_type, asset_class,
                business_line_id, desk_id, notional, notional_currency, mtm, mtm_currency, trade_date, maturity_date, lineage)
            VALUES (gen_random_uuid(), @asOf, @nettingSet, @entityA, @counterparty, 'Otc', 'InterestRate', @businessLine, @desk,
                1, 'EUR', 0, 'EUR', @asOf, @asOf, 'Synthetic')
            """,
            [Seed.EntityA],
            ("asOf", asOf),
            ("nettingSet", Seed.NettingSetA),
            ("entityA", Seed.EntityA.Value),
            ("counterparty", Seed.Counterparty),
            ("businessLine", Seed.BusinessLine),
            ("desk", Seed.Desk));
        (await db.ScalarAsync<string>("cr_reader", "SELECT tableoid::regclass::text FROM market.fx_rate WHERE as_of_date = @asOf", null, ("asOf", asOf)))
            .ShouldBe("market.fx_rate_default");

        (await db.ScalarAsync<int>("cr_partition_job", "SELECT meta.ensure_partitions(@from, 1)", null, ("from", month))).ShouldBeGreaterThan(0);

        (await db.ScalarAsync<string>("cr_reader", "SELECT tableoid::regclass::text FROM market.fx_rate WHERE as_of_date = @asOf", null, ("asOf", asOf)))
            .ShouldBe($"market.fx_rate_p{month:yyyyMM}");
        (await db.ScalarAsync<string>(
            "cr_reader", "SELECT tableoid::regclass::text FROM portfolio.trade WHERE as_of_date = @asOf", [Seed.EntityA], ("asOf", asOf)))
            .ShouldBe($"portfolio.trade_p{month:yyyyMM}");
        (await db.ScalarAsync<bool>(
            "cr_reader", "SELECT relforcerowsecurity FROM pg_class WHERE oid = @name::regclass", null, ("name", $"portfolio.trade_p{month:yyyyMM}")))
            .ShouldBeTrue();
        (await db.ScalarAsync<long>(
            "cr_writer", "SELECT (SELECT count(*) FROM portfolio.trade_default) + (SELECT count(*) FROM market.fx_rate_default)",
            [Seed.EntityA, Seed.EntityB]))
            .ShouldBe(0);
        // The all-entities scope used for the move does not leak into the caller's transaction.
        (await db.ScalarAsync<string>(
            "cr_partition_job",
            "SELECT meta.ensure_partitions(@from, 1)::text || coalesce(current_setting('app.legal_entity_ids', true), '')",
            null,
            ("from", month)))
            .ShouldBe("0");

        // Leaves the seeded portfolio unchanged for the other tests (the owner may delete, the writer may not).
        await db.ExecuteAsync("cr_migrator", "DELETE FROM portfolio.trade WHERE as_of_date = @asOf", [Seed.EntityA], ("asOf", asOf));
    }

    [Fact]
    public async Task Ensure_Partitions_Skips_A_Failing_Table_Records_It_And_Continues()
    {
        var month = db.CurrentMonth.AddMonths(8);
        // Sorted before every real table, so the run must continue after it.
        await db.ExecuteAsync("cr_migrator", "INSERT INTO meta.partitioned_table (table_schema, table_name) VALUES ('bench', 'aaa_missing')");
        try
        {
            (await db.ScalarAsync<int>("cr_partition_job", "SELECT meta.ensure_partitions(@from, 1)", null, ("from", month))).ShouldBeGreaterThan(0);

            (await Exists($"market.fx_rate_p{month:yyyyMM}")).ShouldBeTrue();
            (await Exists($"calc.exposure_result_p{month:yyyyMM}")).ShouldBeTrue();
            (await db.ScalarAsync<long>(
                "cr_reader",
                "SELECT count(*) FROM meta.risk_event WHERE event_type = 'PartitionMaintenanceFailed' AND details ->> 'table' = 'bench.aaa_missing'"))
                .ShouldBeGreaterThan(0);
        }
        finally
        {
            await db.ExecuteAsync("cr_migrator", "DELETE FROM meta.partitioned_table WHERE table_name = 'aaa_missing'");
        }
    }

    private Task<bool> Exists(string name) =>        db.ScalarAsync<bool>("cr_reader", "SELECT to_regclass(@name) IS NOT NULL", null, ("name", name));
}