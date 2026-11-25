using System.Data;
using System.Data.Common;
using CounterpartyRisk.Domain.Contracts.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CounterpartyRisk.Infrastructure.Persistence.Security;

/// <summary>
/// Sets <c>app.legal_entity_ids</c> at the start of every transaction opened through EF Core
/// (explicit ones and those of <c>SaveChanges</c>), with <c>set_config(..., true)</c>: the value is
/// local to the transaction, so it can never leak to the next user of a pooled connection.
/// A query run outside a transaction carries no scope and therefore reads zero row (fail closed):
/// read paths open a transaction (see <see cref="LegalEntityScopeExtensions"/>).
/// </summary>
public sealed class LegalEntityScopeInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor
{
    /// <summary>Constant statement; the scope is always passed as a parameter.</summary>
    internal const string SetScopeSql = "SELECT pg_catalog.set_config('app.legal_entity_ids', @scope, true)";

    /// <summary>Upper bound of a scope, far above the number of entities of a bank.</summary>
    internal const int MaxLegalEntities = 1000;

    private readonly ILegalEntityScope _scope;

    public LegalEntityScopeInterceptor(ILegalEntityScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <summary>
    /// EF Core skips the transaction of a single-statement SaveChanges by default: that statement
    /// would run without scope. Always open one, so that every write carries the scope.
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

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        using var command = CreateCommand(connection, result);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(connection, result);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        using var command = CreateCommand(connection, result);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(connection, result);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
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

    private static void RequireTransaction(DbContextEventData eventData)
    {
        if (eventData?.Context is { } context)
        {
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
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
