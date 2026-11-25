using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CounterpartyRisk.Infrastructure.Persistence.Security;

public static class LegalEntityScopeExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction, so that the legal entity scope set by
    /// <see cref="LegalEntityScopeInterceptor"/> applies to its queries. Reuses the current
    /// transaction when there is one.
    /// </summary>
    public static async Task<T> InLegalEntityScopeAsync<T>(
        this DatabaseFacade database,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(work);

        if (database.CurrentTransaction is not null)
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }

        // Through the execution strategy, so that EnableRetryOnFailure retries the whole unit
        // (transaction + scope + work); a retrying strategy refuses a user transaction otherwise.
        var strategy = database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (database, work),
            static async (_, state, ct) =>
            {
                var transaction = await state.database.BeginTransactionAsync(ct).ConfigureAwait(false);
                await using (transaction.ConfigureAwait(false))
                {
                    var result = await state.work(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    return result;
                }
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }
}
