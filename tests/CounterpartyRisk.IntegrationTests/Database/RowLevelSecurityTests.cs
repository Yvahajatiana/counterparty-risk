using System.Diagnostics.CodeAnalysis;
using Npgsql;
using static CounterpartyRisk.IntegrationTests.Database.DatabaseAssert;

namespace CounterpartyRisk.IntegrationTests.Database;

/// <summary>Row-level security per legal entity (TM-TB4-01, SEC-108, SEC-212).</summary>
[Collection(PostgresDatabase.Name)]
[Trait("Category", "Database")]
[SuppressMessage("Security", "CA2100", Justification = "Constant SQL; values are parameters.")]
public sealed class RowLevelSecurityTests(PostgresFixture db)
{
    private const string CountTrades = "SELECT count(*) FROM portfolio.trade";

    [Fact]
    public async Task All_Tables_With_LegalEntityId_Should_Have_Forced_Rls()
    {
        // Every table (partitions included) that carries legal_entity_id, in any schema.
        var offenders = await ListAsync(
            """
            SELECT n.nspname || '.' || c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'legal_entity_id' AND NOT a.attisdropped
            WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity
                AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = 'le_isolation'
                            AND p.polqual IS NOT NULL AND p.polwithcheck IS NOT NULL))
            ORDER BY 1
            """);

        offenders.ShouldBeEmpty("Tables carrying legal_entity_id without FORCE RLS + le_isolation USING / WITH CHECK");
    }

    [Fact]
    public async Task Rls_Check_Covers_The_Expected_Tables()
    {
        // Guards the test above against an empty selection.
        var tables = await ListAsync(
            """
            SELECT n.nspname || '.' || c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'p' AND c.relforcerowsecurity ORDER BY 1
            """);

        tables.ShouldBe(
        [
            "calc.allocation_result", "calc.cva_result", "calc.exposure_result", "calc.profile_point", "calc.rwa_result",
            "calc.saccr_result", "portfolio.collateral_position", "portfolio.trade",
        ]);
    }

    [Fact]
    public async Task Should_Return_Zero_Rows_When_Scope_Is_Not_Set()
    {
        (await db.ScalarAsync<long>("cr_reader", CountTrades)).ShouldBe(0);
        (await db.ScalarAsync<long>("cr_reader", "SELECT count(*) FROM portfolio.netting_set")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Return_Zero_Rows_When_Scope_Is_Empty()
    {
        (await db.ScalarAsync<long>("cr_reader", CountTrades, [])).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Only_Return_Rows_Of_The_Scope_Including_Aggregates()
    {
        (await db.ScalarAsync<long>("cr_reader", CountTrades, [Seed.EntityA])).ShouldBe(Seed.TradesA);
        (await db.ScalarAsync<long>("cr_reader", CountTrades, [Seed.EntityB])).ShouldBe(Seed.TradesB);
        (await db.ScalarAsync<long>("cr_reader", CountTrades, [Seed.EntityA, Seed.EntityB])).ShouldBe(Seed.TradesA + Seed.TradesB);
        (await db.ScalarAsync<decimal>("cr_reader", "SELECT coalesce(sum(notional), 0) FROM portfolio.trade", [Seed.EntityA]))
            .ShouldBe(Seed.TradesA * 1_000_000m);

        // An explicit filter on the other entity does not widen the scope.
        (await db.ScalarAsync<long>(
            "cr_reader", "SELECT count(*) FROM portfolio.trade WHERE legal_entity_id = @other", [Seed.EntityA], ("other", Seed.EntityB.Value)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Should_Not_Leak_Other_Entity_Through_A_Direct_Partition_Read()
    {
        var partition = $"portfolio.trade_p{db.AsOfDate:yyyyMM}";

        (await db.ScalarAsync<long>("cr_reader", $"SELECT count(*) FROM {partition}", [Seed.EntityA])).ShouldBe(Seed.TradesA);
        (await db.ScalarAsync<long>("cr_reader", $"SELECT count(*) FROM {partition}")).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Not_Leak_Scope_To_The_Next_Transaction_Of_A_Pooled_Connection()
    {
        await using var connection = await db.OpenAsync("cr_reader");

        await using (var first = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await PostgresFixture.SetScopeAsync(connection, first, [Seed.EntityA]);
            (await CountAsync(connection, first)).ShouldBe(Seed.TradesA);
            await first.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var second = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        (await CountAsync(connection, second)).ShouldBe(0);
    }

    [Fact]
    public async Task Should_Fail_Closed_On_A_Malformed_Scope()
    {
        await using var connection = await db.OpenAsync("cr_reader");
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var set = new NpgsqlCommand("SELECT set_config('app.legal_entity_ids', @scope, true)", connection, transaction))
        {
            set.Parameters.AddWithValue("scope", $"{Seed.EntityA}' OR true --");
            await set.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await FailsWithAsync(InvalidTextRepresentation, () => CountAsync(connection, transaction));
    }

    [Fact]
    public async Task Should_Reject_Insert_Outside_Scope()
    {
        var exception = await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_writer",
            """
            INSERT INTO portfolio.trade (id, as_of_date, netting_set_id, legal_entity_id, counterparty_id, trade_type, asset_class,
                business_line_id, desk_id, notional, notional_currency, mtm, mtm_currency, trade_date, maturity_date, lineage)
            VALUES (gen_random_uuid(), @asOf, @nettingSet, @entityB, @counterparty, 'Otc', 'InterestRate', @businessLine, @desk,
                1, 'EUR', 0, 'EUR', @asOf, @asOf, 'Synthetic')
            """,
            [Seed.EntityA],
            ("asOf", db.AsOfDate),
            ("nettingSet", Seed.NettingSetB),
            ("entityB", Seed.EntityB.Value),
            ("counterparty", Seed.Counterparty),
            ("businessLine", Seed.BusinessLine),
            ("desk", Seed.Desk)));

        exception.MessageText.ShouldContain("row-level security");
    }

    [Fact]
    public async Task Should_Reject_Moving_A_Row_Out_Of_Scope()
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_writer",
            "UPDATE portfolio.netting_set SET legal_entity_id = @entityB WHERE id = @nettingSetA",
            [Seed.EntityA],
            ("entityB", Seed.EntityB.Value),
            ("nettingSetA", Seed.NettingSetA)));
    }

    [Fact]
    public async Task Should_Not_Update_Rows_Outside_Scope()
    {
        var affected = await db.ExecuteAsync(
            "cr_writer", "UPDATE portfolio.trade SET mtm = mtm WHERE legal_entity_id = @entityB", [Seed.EntityA], ("entityB", Seed.EntityB.Value));

        affected.ShouldBe(0);
    }

    [Fact]
    public async Task Owner_Is_Subject_To_Rls_Too()
    {
        (await db.ScalarAsync<long>("cr_migrator", CountTrades)).ShouldBe(0);
    }

    private async Task<List<string>> ListAsync(string sql)
    {
        await using var connection = await db.OpenAsync("cr_reader");
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(CountTrades, connection, transaction);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
