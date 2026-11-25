using CounterpartyRisk.Domain.Contracts.Identifiers;
using CounterpartyRisk.Infrastructure.Persistence.Configuration;
using CounterpartyRisk.Infrastructure.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CounterpartyRisk.IntegrationTests.Database;

/// <summary>
/// The pattern every module DbContext follows: its own mapping (no migration), a least-privilege
/// connection and the legal entity scope interceptor (#11, ADR-001 rules 7 and 8).
/// </summary>
[Collection(PostgresDatabase.Name)]
[Trait("Category", "Database")]
public sealed class LegalEntityScopeEfCoreTests(PostgresFixture db)
{
    [Fact]
    public async Task Queries_In_A_Transaction_See_Only_The_Scope()
    {
        await using var provider = Build("cr_reader", [Seed.EntityA]);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TradeReadContext>();

        var trades = await context.Database.InLegalEntityScopeAsync(
            ct => context.Trades.AsNoTracking().ToListAsync(ct), TestContext.Current.CancellationToken);

        trades.Count.ShouldBe(Seed.TradesA);
        trades.ShouldAllBe(t => t.LegalEntityId == Seed.EntityA.Value);
    }

    [Fact]
    public async Task Commands_Outside_A_Transaction_Fail_Loudly()
    {
        await using var provider = Build("cr_writer", [Seed.EntityA]);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TradeReadContext>();
        var ct = TestContext.Current.CancellationToken;

        // Without a transaction RLS would silently read or change zero row: refused instead.
        await Should.ThrowAsync<InvalidOperationException>(() => context.Trades.CountAsync(ct));
        await Should.ThrowAsync<InvalidOperationException>(() => context.Trades.ExecuteUpdateAsync(s => s.SetProperty(t => t.Mtm, t => t.Mtm), ct));
        await Should.ThrowAsync<InvalidOperationException>(() => context.Trades.Where(t => t.Mtm < 0).ExecuteDeleteAsync(ct));
        await Should.ThrowAsync<InvalidOperationException>(() => context.Database.ExecuteSqlAsync($"UPDATE portfolio.trade SET mtm = mtm", ct));

        // The same bulk update inside the scope transaction works.
        var updated = await context.Database.InLegalEntityScopeAsync(
            token => context.Trades.ExecuteUpdateAsync(s => s.SetProperty(t => t.Mtm, t => t.Mtm), token), ct);
        updated.ShouldBe(Seed.TradesA);
    }

    [Fact]
    public async Task Scope_Helper_Works_With_Retry_On_Failure()
    {
        await using var provider = Build("cr_reader", [Seed.EntityA], retry: true);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TradeReadContext>();

        var count = await context.Database.InLegalEntityScopeAsync(
            ct => context.Trades.CountAsync(ct), TestContext.Current.CancellationToken);

        count.ShouldBe(Seed.TradesA);
    }

    [Fact]
    public async Task Default_Empty_Scope_Sees_Nothing()
    {
        await using var provider = Build("cr_reader", scope: null);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TradeReadContext>();

        var count = await context.Database.InLegalEntityScopeAsync(
            ct => context.Trades.CountAsync(ct), TestContext.Current.CancellationToken);

        count.ShouldBe(0);
    }

    [Fact]
    public async Task SaveChanges_Transaction_Carries_The_Scope_And_With_Check_Applies()
    {
        await using var provider = Build("cr_writer", [Seed.EntityA]);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TradeReadContext>();

        // Same logical change on a row of A (in scope) then of B (out of scope).
        var tradeA = await context.Database.InLegalEntityScopeAsync(
            ct => context.Trades.FirstAsync(ct), TestContext.Current.CancellationToken);
        tradeA.Mtm += 0m;
        context.Entry(tradeA).Property(t => t.Mtm).IsModified = true;
        (await context.SaveChangesAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        // The forced transaction is limited to SaveChanges: the context setting is restored.
        context.Database.AutoTransactionBehavior.ShouldBe(AutoTransactionBehavior.WhenNeeded);

        context.Trades.Add(new TradeRow
        {
            Id = Guid.CreateVersion7(),
            AsOfDate = db.AsOfDate,
            LegalEntityId = Seed.EntityB.Value,
            Mtm = 0m,
        });
        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        exception.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe(DatabaseAssert.InsufficientPrivilege);
    }

    private ServiceProvider Build(string role, LegalEntityId[]? scope, bool retry = false)
    {
        var services = new ServiceCollection();
        if (scope is not null)
        {
            services.AddScoped<ILegalEntityScope>(_ => new FixedLegalEntityScope(scope));
        }

        // Development: the test container has no TLS.
        services.AddCounterpartyRiskDbContext<TradeReadContext>(
            db.ConnectionString(role), isDevelopment: true, npgsql => { if (retry) { npgsql.EnableRetryOnFailure(); } });
        return services.BuildServiceProvider();
    }

    public sealed class TradeRow
    {
        public Guid Id { get; set; }

        public DateOnly AsOfDate { get; set; }

        public Guid LegalEntityId { get; set; }

        public decimal Mtm { get; set; }
    }

    public sealed class TradeReadContext(DbContextOptions<TradeReadContext> options) : DbContext(options)
    {
        public DbSet<TradeRow> Trades => Set<TradeRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TradeRow>(trade =>
            {
                trade.ToTable("trade", "portfolio", t => t.ExcludeFromMigrations());
                trade.HasKey(t => new { t.AsOfDate, t.Id });
                trade.Property(t => t.Id).HasColumnName("id");
                trade.Property(t => t.AsOfDate).HasColumnName("as_of_date");
                trade.Property(t => t.LegalEntityId).HasColumnName("legal_entity_id");
                trade.Property(t => t.Mtm).HasColumnName("mtm");
            });
        }
    }
}
