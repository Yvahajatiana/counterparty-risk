using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using CounterpartyRisk.Domain.Contracts.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CounterpartyRisk.Infrastructure.Persistence.Security;

/// <summary>
/// Sets <c>app.legal_entity_ids</c> at the start of every transaction opened through EF Core
/// (explicit ones and those of <c>SaveChanges</c>), with <c>set_config(..., true)</c>: the value is
/// local to the transaction, so it can never leak to the next user of a pooled connection.
/// A command run outside a transaction would carry no scope and silently read or change zero row
/// (queries, <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, <c>ExecuteSql</c>): it is refused with an
/// exception instead. Read paths use <see cref="LegalEntityScopeExtensions.InLegalEntityScopeAsync"/>.
/// </summary>
public sealed class LegalEntityScopeInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor, IDbCommandInterceptor
{
    /// <summary>Constant statement; the scope is always passed as a parameter.</summary>
    internal const string SetScopeSql = "SELECT pg_catalog.set_config('app.legal_entity_ids', @scope, true)";

    /// <summary>Upper bound of a scope, far above the number of entities of a bank.</summary>
    internal const int MaxLegalEntities = 1000;

    private readonly ILegalEntityScope _scope;

    // SaveChanges forces a transaction; the previous behaviour of the context is restored afterwards.
    private readonly ConditionalWeakTable<DbContext, StrongBox<AutoTransactionBehavior>> _savedBehaviours = new();

    public LegalEntityScopeInterceptor(ILegalEntityScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    // ------------------------------------------------------------------ SaveChanges

    /// <summary>
    /// EF Core skips the transaction of a single-statement SaveChanges by default: that statement
    /// would run without scope. Always open one for this call, then restore the context setting.
    /// </summary>
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RequireTransaction(eventData);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RequireTransaction(eventData);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RestoreTransactionBehaviour(eventData);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        RestoreTransactionBehaviour(eventData);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => RestoreTransactionBehaviour(eventData);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RestoreTransactionBehaviour(eventData);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData) => RestoreTransactionBehaviour(eventData);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        RestoreTransactionBehaviour(eventData);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ transactions

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        SetScope(connection, result);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await SetScopeAsync(connection, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        SetScope(connection, result);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await SetScopeAsync(connection, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ------------------------------------------------------------------ commands (fail loudly)

    public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        RequireScopedTransaction(command);
        return result;
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        RequireScopedTransaction(command);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        RequireScopedTransaction(command);
        return result;
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RequireScopedTransaction(command);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        RequireScopedTransaction(command);
        return result;
    }

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        RequireScopedTransaction(command);
        return ValueTask.FromResult(result);
    }

    /// <summary>Comma-separated, sorted, de-duplicated identifiers; empty for an empty scope.</summary>
    internal static string Format(IReadOnlyCollection<LegalEntityId> legalEntityIds)
    {
        ArgumentNullException.ThrowIfNull(legalEntityIds);

        var ids = legalEntityIds.Select(id => id.Value).Where(id => id != Guid.Empty).Distinct().Order().ToList();
        if (ids.Count > MaxLegalEntities)
        {
            throw new InvalidOperationException($"A legal entity scope holds at most {MaxLegalEntities} entities.");
        }

        return string.Join(',', ids.Select(id => id.ToString("D")));
    }

    /// <summary>
    /// Every command of a scoped DbContext runs in a transaction opened through EF Core, which is
    /// where the scope was set. Outside one, RLS would return or change nothing without any error.
    /// </summary>
    internal static void RequireScopedTransaction(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Transaction is null)
        {
            throw new InvalidOperationException(
                "A command of a legal-entity-scoped DbContext must run inside a transaction "
                + "(Database.InLegalEntityScopeAsync, SaveChanges or BeginTransaction): row-level security "
                + "would otherwise silently read or change no row.");
        }
    }

    private void RequireTransaction(DbContextEventData eventData)
    {
        if (eventData?.Context is not { } context || context.Database.AutoTransactionBehavior == AutoTransactionBehavior.Always)
        {
            return;
        }

        _savedBehaviours.AddOrUpdate(context, new StrongBox<AutoTransactionBehavior>(context.Database.AutoTransactionBehavior));
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    private void RestoreTransactionBehaviour(DbContextEventData eventData)
    {
        if (eventData?.Context is { } context && _savedBehaviours.TryGetValue(context, out var previous))
        {
            context.Database.AutoTransactionBehavior = previous.Value;
            _savedBehaviours.Remove(context);
        }
    }

    private void SetScope(DbConnection connection, DbTransaction transaction)
    {
        using var command = CreateCommand(connection, transaction);
        command.ExecuteNonQuery();
    }

    private async Task SetScopeAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var command = CreateCommand(connection, transaction);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private DbCommand CreateCommand(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Formatted first: an invalid scope throws before any command exists (transaction unusable).
        var scope = Format(_scope.LegalEntityIds);
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SetScopeSql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "scope";
        parameter.DbType = DbType.String;
        parameter.Value = scope;
        command.Parameters.Add(parameter);

        return command;
    }
}
