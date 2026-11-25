using Microsoft.EntityFrameworkCore.Design;

namespace CounterpartyRisk.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> only (migrations script / bundle). No real connection here: the bundle
/// receives the <c>cr_migrator</c> connection at run time (<c>efbundle --connection</c>), from the
/// secret store, never from the repository.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CounterpartyRiskDbContext>
{
    public CounterpartyRiskDbContext CreateDbContext(string[] args) =>
        CounterpartyRiskDbContext.ForMigrations("Host=localhost;Database=ccr;Username=cr_migrator");
}
