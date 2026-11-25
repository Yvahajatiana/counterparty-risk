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

        var transaction = await database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var result = await work(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
    }
}
