using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using CounterpartyRisk.Domain.Contracts.Identifiers;
using CounterpartyRisk.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CounterpartyRisk.Infrastructure.Persistence.UnitTests;

public class LegalEntityScopeInterceptorTests
{
    private static readonly LegalEntityId EntityA = new(Guid.Parse("0193a3c0-0000-7000-8000-00000000000a"));
    private static readonly LegalEntityId EntityB = new(Guid.Parse("0193a3c0-0000-7000-8000-00000000000b"));

    [Fact]
    public void Empty_scope_is_formatted_as_empty_string()
    {
        LegalEntityScopeInterceptor.Format([]).ShouldBe(string.Empty);
    }

    [Fact]
    public void Scope_is_sorted_deduplicated_and_ignores_empty_ids()
    {
        var formatted = LegalEntityScopeInterceptor.Format([EntityB, EntityA, EntityB, new LegalEntityId(Guid.Empty)]);

        formatted.ShouldBe($"{EntityA},{EntityB}");
    }

    [Fact]
    public void Oversized_scope_is_refused()
    {
        var ids = Enumerable.Range(0, LegalEntityScopeInterceptor.MaxLegalEntities + 1).Select(_ => LegalEntityId.New()).ToList();

        Should.Throw<InvalidOperationException>(() => LegalEntityScopeInterceptor.Format(ids));
    }

    [Fact]
    public void Empty_scope_default_sees_nothing()
    {
        new EmptyLegalEntityScope().LegalEntityIds.ShouldBeEmpty();
    }

    [Fact]
    public void Fixed_scope_deduplicates()
    {
        new FixedLegalEntityScope([EntityA, EntityA, EntityB]).LegalEntityIds.Count.ShouldBe(2);
    }

    [Fact]
    public void Transaction_start_sets_the_scope_as_a_parameter_local_to_the_transaction()
    {
        using var connection = new RecordingConnection();
        using var transaction = connection.BeginTransaction();
        var interceptor = new LegalEntityScopeInterceptor(new FixedLegalEntityScope([EntityA]));

        interceptor.TransactionStarted(connection, null!, transaction).ShouldBeSameAs(transaction);

        var command = connection.Executed.ShouldHaveSingleItem();
        command.CommandText.ShouldBe(LegalEntityScopeInterceptor.SetScopeSql);
        command.CommandText.ShouldContain(", true)");
        command.Transaction.ShouldBeSameAs(transaction);
        var parameter = command.Parameters.Cast<DbParameter>().ShouldHaveSingleItem();
        parameter.ParameterName.ShouldBe("scope");
        parameter.Value.ShouldBe(EntityA.ToString());
    }

    [Fact]
    public async Task Async_transaction_start_and_reuse_set_the_scope()
    {
        await using var connection = new RecordingConnection();
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var interceptor = new LegalEntityScopeInterceptor(new EmptyLegalEntityScope());

        await interceptor.TransactionStartedAsync(connection, null!, transaction, TestContext.Current.CancellationToken);
        await interceptor.TransactionUsedAsync(connection, null!, transaction, TestContext.Current.CancellationToken);
        interceptor.TransactionUsed(connection, null!, transaction);

        connection.Executed.Count.ShouldBe(3);
        connection.Executed.ShouldAllBe(c => (string)c.Parameters.Cast<DbParameter>().Single().Value! == string.Empty);
    }

    [Fact]
    public void Invalid_scope_fails_before_any_command()
    {
        using var connection = new RecordingConnection();
        using var transaction = connection.BeginTransaction();
        var tooMany = Enumerable.Range(0, LegalEntityScopeInterceptor.MaxLegalEntities + 1).Select(_ => LegalEntityId.New());
        var interceptor = new LegalEntityScopeInterceptor(new FixedLegalEntityScope(tooMany));

        Should.Throw<InvalidOperationException>(() => interceptor.TransactionStarted(connection, null!, transaction));
        connection.Executed.ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveChanges_always_runs_in_a_transaction_so_that_it_carries_the_scope()
    {
        var options = new DbContextOptionsBuilder<SampleContext>().UseNpgsql("Host=localhost;Database=ccr;Username=cr_writer").Options;
        await using var context = new SampleContext(options);
        var interceptor = new LegalEntityScopeInterceptor(new EmptyLegalEntityScope());
        context.Database.AutoTransactionBehavior.ShouldBe(AutoTransactionBehavior.WhenNeeded);

        interceptor.SavingChanges(new DbContextEventData(null!, null!, context), default);
        context.Database.AutoTransactionBehavior.ShouldBe(AutoTransactionBehavior.Always);

        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
        await interceptor.SavingChangesAsync(new DbContextEventData(null!, null!, context), default, TestContext.Current.CancellationToken);
        context.Database.AutoTransactionBehavior.ShouldBe(AutoTransactionBehavior.Always);
    }

    private sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options);

    // Minimal ADO.NET fakes: they record the commands executed by the interceptor.
    [SuppressMessage("Security", "CA2100", Justification = "Test fake, no database.")]
    private sealed class RecordingConnection : DbConnection
    {
        public List<DbCommand> Executed { get; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "17";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FakeTransaction(this);

        protected override DbCommand CreateDbCommand() => new RecordingCommand(this);
    }

    private sealed class FakeTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection DbConnection => connection;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    [SuppressMessage("Security", "CA2100", Justification = "Test fake, no database.")]
    private sealed class RecordingCommand(RecordingConnection connection) : DbCommand
    {
        private readonly FakeParameters _parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; } = connection;

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            connection.Executed.Add(this);
            return -1;
        }

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class FakeParameters : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];

        public override int Count => _items.Count;

        public override object SyncRoot => _items;

        public override int Add(object value)
        {
            _items.Add((DbParameter)value);
            return _items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public override void Clear() => _items.Clear();

        public override bool Contains(object value) => _items.Contains((DbParameter)value);

        public override bool Contains(string value) => _items.Exists(p => p.ParameterName == value);

        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();

        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);

        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);

        public override void Remove(object value) => _items.Remove((DbParameter)value);

        public override void RemoveAt(int index) => _items.RemoveAt(index);

        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => _items[index];

        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }
}
