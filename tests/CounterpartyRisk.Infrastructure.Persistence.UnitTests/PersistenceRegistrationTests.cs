using CounterpartyRisk.Infrastructure.Persistence.Configuration;
using CounterpartyRisk.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CounterpartyRisk.Infrastructure.Persistence.UnitTests;

public class PersistenceRegistrationTests
{
    private const string Local = "Host=localhost;Database=ccr;Username=cr_reader";

    [Fact]
    public void DbContext_defaults_to_the_empty_scope_and_the_rls_interceptor()
    {
        var services = new ServiceCollection();
        services.AddCounterpartyRiskDbContext<SampleContext>(Local, isDevelopment: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILegalEntityScope>().ShouldBeOfType<EmptyLegalEntityScope>();
        scope.ServiceProvider.GetRequiredService<LegalEntityScopeInterceptor>().ShouldNotBeNull();
        using var context = scope.ServiceProvider.GetRequiredService<SampleContext>();
        context.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public void Host_scope_registered_first_is_kept()
    {
        var services = new ServiceCollection();
        services.AddScoped<ILegalEntityScope>(_ => new FixedLegalEntityScope([]));
        services.AddCounterpartyRiskDbContext<SampleContext>(Local, isDevelopment: true);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ILegalEntityScope>().ShouldBeOfType<FixedLegalEntityScope>();
    }

    [Fact]
    public void DbContext_registration_hardens_the_connection_string()
    {
        var services = new ServiceCollection();

        Should.Throw<InvalidOperationException>(() => services.AddCounterpartyRiskDbContext<SampleContext>(Local, isDevelopment: false));
    }

    [Fact]
    public async Task Readiness_check_is_tagged_and_reports_unhealthy_without_details()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresReadinessCheck(
            "postgres", "Host=127.0.0.1;Port=1;Database=ccr;Username=cr_reader;Timeout=1", isDevelopment: true, ["ready"]);

        using var provider = services.BuildServiceProvider();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ShouldHaveSingleItem();
        registration.Tags.ShouldContain("ready");

        var result = await registration.Factory(provider).CheckHealthAsync(
            new HealthCheckContext { Registration = registration }, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Exception.ShouldBeNull();
        result.Description.ShouldBeNull();
    }

    private sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options);
}
