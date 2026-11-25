using System.Diagnostics.CodeAnalysis;
using static CounterpartyRisk.IntegrationTests.Database.DatabaseAssert;

namespace CounterpartyRisk.IntegrationTests.Database;

/// <summary>Least-privilege identities of docs/security/data-flow.md (TM-TB4-02, -03, -08, -12, -13, -14).</summary>
[Collection(PostgresDatabase.Name)]
[Trait("Category", "Database")]
[SuppressMessage("Security", "CA2100", Justification = "Constant SQL from the test data; values are parameters.")]
public sealed class RoleTests(PostgresFixture db)
{
    public static TheoryData<string> ReaderForbiddenStatements =>
    [
        "INSERT INTO ref.country (code, name) VALUES ('IT', 'Italy')",
        "UPDATE portfolio.trade SET mtm = 0",
        "DELETE FROM portfolio.trade",
        "TRUNCATE portfolio.trade",
        "ALTER TABLE portfolio.trade ADD COLUMN x int",
        "DROP TABLE ref.country",
        "CREATE TABLE calc.rogue (id int)",
        "CREATE TABLE public.rogue (id int)",
        "SELECT meta.ensure_partitions(current_date, 1)",
    ];

    public static TheoryData<string> WriterForbiddenStatements =>
    [
        "DROP TABLE portfolio.trade",
        "DELETE FROM portfolio.trade",
        "TRUNCATE calc.exposure_result",
        "ALTER TABLE portfolio.trade DISABLE ROW LEVEL SECURITY",
        "DROP POLICY le_isolation ON portfolio.trade",
        "CREATE TABLE staging.rogue (id int)",
        "UPDATE meta.active_method_version SET method_version_id = NULL",
        "INSERT INTO meta.method_version (id) VALUES ('rogue')",
        "SELECT seq FROM audit.audit_event",
        "SELECT id FROM dpkeys.api_key",
    ];

    public static TheoryData<string> ReaderUnreadableTables =>
    [
        "audit.audit_event",
        "dpkeys.api_key",
        "dpkeys.bff_key",
        "session.ticket",
        "meta.partitioned_table",
        "meta.ef_migrations_history",
    ];

    [Fact]
    public async Task No_Service_Role_Is_Superuser_Or_Bypasses_Rls()
    {
        var offenders = await db.ScalarAsync<long>(
            "cr_reader",
            """
            SELECT count(*) FROM pg_roles
            WHERE rolname LIKE 'cr\_%' AND (rolsuper OR rolbypassrls OR rolcreaterole OR rolcreatedb OR rolreplication)
            """);

        offenders.ShouldBe(0);
        (await db.ScalarAsync<bool>("cr_reader", "SELECT NOT rolcanlogin FROM pg_roles WHERE rolname = 'cr_owner'")).ShouldBeTrue();
    }

    [Fact]
    public async Task Public_Schema_And_Database_Are_Locked_Down()
    {
        (await db.ScalarAsync<bool>("cr_reader", "SELECT has_schema_privilege('public', 'public', 'CREATE')")).ShouldBeFalse();
        (await db.ScalarAsync<bool>("cr_reader", "SELECT has_database_privilege('public', current_database(), 'CONNECT')")).ShouldBeFalse();
        (await db.ScalarAsync<bool>("cr_reader", "SELECT has_database_privilege('public', current_database(), 'TEMPORARY')")).ShouldBeFalse();
    }

    [Fact]
    public async Task Functions_Are_Not_Executable_By_Public()
    {
        var offenders = await db.ScalarAsync<long>(
            "cr_reader",
            """
            SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname IN ('ref', 'portfolio', 'financing', 'market', 'bench', 'calc', 'reporting', 'meta', 'staging', 'audit')
              AND has_function_privilege('public', p.oid, 'EXECUTE')
            """);

        offenders.ShouldBe(0);
    }

    [Fact]
    public async Task Objects_Are_Owned_By_CrOwner()
    {
        var offenders = await db.ScalarAsync<long>(
            "cr_reader",
            """
            SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname IN ('ref', 'portfolio', 'financing', 'market', 'bench', 'calc', 'reporting', 'meta', 'staging',
                                'audit', 'dpkeys', 'session', 'quartz')
              AND pg_get_userbyid(c.relowner) <> 'cr_owner'
            """);

        offenders.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(ReaderForbiddenStatements))]
    public async Task CrReader_Should_Not_Insert_Update_Delete_Or_Alter(string sql)
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_reader", sql, [Seed.EntityA]));
    }

    [Theory]
    [MemberData(nameof(ReaderUnreadableTables))]
    public async Task CrReader_Should_Not_Read_Audit_Key_Rings_Sessions_Or_Internals(string table)
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_reader", $"SELECT count(*) FROM {table}"));
    }

    [Theory]
    [MemberData(nameof(WriterForbiddenStatements))]
    public async Task CrWriter_Should_Not_Drop_Delete_Or_Touch_Governance_Audit_And_Keys(string sql)
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_writer", sql, [Seed.EntityA]));
    }

    [Fact]
    public async Task CrLimitWriter_Should_Not_Write_Outside_Limit_Tables()
    {
        string[] statements =
        [
            "UPDATE portfolio.trade SET mtm = 0",
            "INSERT INTO portfolio.netting_set (id) VALUES (gen_random_uuid())",
            "SELECT count(*) FROM portfolio.trade",
            "INSERT INTO calc.exposure_result (id) VALUES (gen_random_uuid())",
            "UPDATE meta.active_method_version SET method_version_id = NULL",
            "INSERT INTO audit.audit_event (event_type, payload, hash) VALUES ('x', '{}', '\\x00')",
        ];

        foreach (var sql in statements)
        {
            await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_limit_writer", sql, [Seed.EntityA]));
        }
    }

    [Fact]
    public async Task CrGovernanceWriter_Should_Not_Write_Outside_Governance_Tables()
    {
        string[] statements =
        [
            "INSERT INTO calc.exposure_result (id) VALUES (gen_random_uuid())",
            "SELECT count(*) FROM calc.exposure_result",
            "UPDATE portfolio.trade SET mtm = 0",
            "UPDATE meta.method_version SET parameters = '{\"pfe_quantile\": 0.5}'::jsonb",
            "UPDATE meta.method_version SET parameters_sha256 = repeat('b', 64)",
            "DELETE FROM meta.method_version",
            "INSERT INTO meta.ingestion_run (id, source_id, status) VALUES (gen_random_uuid(), 'synthetic-portfolio', 'Running')",
        ];

        foreach (var sql in statements)
        {
            await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_governance_writer", sql));
        }

        // Status transition allowed (column-level grant), then reverted.
        (await db.ExecuteAsync(
            "cr_governance_writer", "UPDATE meta.method_version SET status = 'PendingApproval' WHERE id = @id", null, ("id", Seed.MethodVersion)))
            .ShouldBe(1);
        await db.ExecuteAsync("cr_governance_writer", "UPDATE meta.method_version SET status = 'Computing' WHERE id = @id", null, ("id", Seed.MethodVersion));
    }

    [Fact]
    public async Task CrGovernanceWriter_Should_Only_Insert_HistoryCorrection_Events()
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_governance_writer", "INSERT INTO meta.risk_event (id, event_type) VALUES (gen_random_uuid(), 'MethodSwitch')"));

        (await db.ExecuteAsync(
            "cr_governance_writer", "INSERT INTO meta.risk_event (id, event_type) VALUES (gen_random_uuid(), 'HistoryCorrection')"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task CrScheduler_Should_Not_Write_Active_Method_Version()
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_scheduler", "UPDATE meta.active_method_version SET method_version_id = @id", null, ("id", Seed.MethodVersion)));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_scheduler", "UPDATE meta.method_version SET status = 'Active'"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_scheduler", "SELECT count(*) FROM portfolio.trade"));

        (await db.ExecuteAsync(
            "cr_scheduler",
            "INSERT INTO meta.ingestion_run (id, source_id, status) VALUES (gen_random_uuid(), 'synthetic-portfolio', 'Running')"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task CrAuditWriter_Should_Only_Read_Seq_And_Hash()
    {
        (await db.ExecuteAsync(
            "cr_audit_writer",
            "INSERT INTO audit.audit_event (event_type, payload, hash) VALUES ('Test', '{}', decode(repeat('ab', 32), 'hex'))"))
            .ShouldBe(1);

        (await db.ScalarAsync<long>("cr_audit_writer", "SELECT count(*) FROM (SELECT seq, hash FROM audit.audit_event) e")).ShouldBeGreaterThan(0);

        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<object>("cr_audit_writer", "SELECT payload FROM audit.audit_event LIMIT 1"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<object>("cr_audit_writer", "SELECT * FROM audit.audit_event LIMIT 1"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_audit_writer", "UPDATE audit.audit_event SET event_type = 'x'"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_audit_writer", "DELETE FROM audit.audit_event"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_audit_writer", "TRUNCATE audit.audit_event"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_audit_writer", "SELECT count(*) FROM portfolio.trade"));
    }

    [Fact]
    public async Task CrAuditVerifier_Should_Only_Read_Audit_Events()
    {
        (await db.ScalarAsync<long>("cr_audit_verifier", "SELECT count(*) FROM audit.audit_event")).ShouldBeGreaterThanOrEqualTo(0);

        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_audit_verifier", "INSERT INTO audit.audit_event (event_type, payload, hash) VALUES ('x', '{}', decode(repeat('ab', 32), 'hex'))"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_audit_verifier", "SELECT count(*) FROM portfolio.trade"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_audit_verifier", "SELECT count(*) FROM meta.method_version"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_audit_reader", "SELECT count(*) FROM portfolio.trade"));
    }

    [Fact]
    public async Task Audit_Events_Are_Append_Only_Even_For_The_Owner()
    {
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_migrator", "UPDATE audit.audit_event SET event_type = 'x'"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_migrator", "DELETE FROM audit.audit_event"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_migrator", "TRUNCATE audit.audit_event"));
    }

    [Fact]
    public async Task Key_Ring_And_Session_Roles_Are_Confined_To_Their_Table()
    {
        (await db.ExecuteAsync("cr_api_keys", "INSERT INTO dpkeys.api_key (friendly_name, xml) VALUES ('k', '<key/>')")).ShouldBe(1);
        (await db.ExecuteAsync("cr_bff_keys", "INSERT INTO dpkeys.bff_key (friendly_name, xml) VALUES ('k', '<key/>')")).ShouldBe(1);
        (await db.ExecuteAsync(
            "cr_bff_session", "INSERT INTO session.ticket (id, value, expires_at) VALUES ('t1', '\\x01', now() + interval '1 hour')"))
            .ShouldBe(1);
        (await db.ExecuteAsync("cr_bff_session", "DELETE FROM session.ticket WHERE id = 't1'")).ShouldBe(1);

        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_api_keys", "SELECT count(*) FROM dpkeys.bff_key"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_bff_keys", "SELECT count(*) FROM dpkeys.api_key"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_api_keys", "DELETE FROM dpkeys.api_key"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_bff_session", "SELECT count(*) FROM dpkeys.bff_key"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_bff_session", "SELECT count(*) FROM portfolio.trade"));
    }

    [Fact]
    public async Task CrPartitionJob_Should_Only_Execute_Partition_Functions()
    {
        (await db.ScalarAsync<int>("cr_partition_job", "SELECT meta.ensure_partitions(current_date, 1)")).ShouldBeGreaterThanOrEqualTo(0);

        await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync(
            "cr_partition_job", "CREATE TABLE portfolio.trade_p209912 PARTITION OF portfolio.trade FOR VALUES FROM ('2099-12-01') TO ('2100-01-01')"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_partition_job", "SELECT count(*) FROM portfolio.trade"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_partition_job", "SELECT count(*) FROM meta.partitioned_table"));
        await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<object>("cr_partition_job", "SELECT meta.apply_le_isolation('portfolio.trade')"));
    }

    [Fact]
    public async Task Default_Privileges_Grant_Future_Tables_To_The_Right_Roles()
    {
        // A table created later by a migration (as cr_owner) is readable by cr_reader and writable by
        // cr_writer without any explicit grant; in staging only, cr_writer may also delete.
        await db.ExecuteAsync("cr_migrator", "CREATE TABLE calc.future_result (id int PRIMARY KEY); CREATE TABLE staging.future_load (id int)");
        try
        {
            (await db.ExecuteAsync("cr_writer", "INSERT INTO calc.future_result VALUES (1)")).ShouldBe(1);
            (await db.ScalarAsync<long>("cr_reader", "SELECT count(*) FROM calc.future_result")).ShouldBe(1);
            await FailsWithAsync(InsufficientPrivilege, () => db.ExecuteAsync("cr_writer", "DELETE FROM calc.future_result"));

            (await db.ExecuteAsync("cr_writer", "INSERT INTO staging.future_load VALUES (1)")).ShouldBe(1);
            (await db.ExecuteAsync("cr_writer", "DELETE FROM staging.future_load")).ShouldBe(1);
            await FailsWithAsync(InsufficientPrivilege, () => db.ScalarAsync<long>("cr_reader", "SELECT count(*) FROM staging.future_load"));
        }
        finally
        {
            await db.ExecuteAsync("cr_migrator", "DROP TABLE calc.future_result; DROP TABLE staging.future_load");
        }
    }
}
