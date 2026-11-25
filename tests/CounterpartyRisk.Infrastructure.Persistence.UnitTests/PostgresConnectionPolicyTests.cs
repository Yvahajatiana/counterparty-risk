using CounterpartyRisk.Infrastructure.Persistence.Configuration;
using Npgsql;

namespace CounterpartyRisk.Infrastructure.Persistence.UnitTests;

public class PostgresConnectionPolicyTests
{
    private const string Local = "Host=localhost;Database=ccr;Username=cr_reader";
    private const string Tls = "Host=db.internal;Database=ccr;Username=cr_reader;SSL Mode=VerifyFull";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Refuse_Missing_Connection_String(string? connectionString)
    {
        Should.Throw<InvalidOperationException>(() => PostgresConnectionPolicy.Harden(connectionString, isDevelopment: true));
    }

    [Fact]
    public void Should_Not_Echo_A_Malformed_Connection_String()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            PostgresConnectionPolicy.Harden("Host=localhost;Username=leak-me;Nonsense", isDevelopment: true));

        ex.Message.ShouldNotContain("leak-me");
        ex.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData("Database=ccr;Username=cr_reader")]
    [InlineData("Host=localhost;Username=cr_reader")]
    [InlineData("Host=localhost;Database=ccr")]
    public void Should_Require_Host_Database_And_Identity(string connectionString)
    {
        Should.Throw<InvalidOperationException>(() => PostgresConnectionPolicy.Harden(connectionString, isDevelopment: true));
    }

    [Theory]
    [InlineData("postgres")]
    [InlineData("cr_owner")]
    [InlineData("CR_MIGRATOR")]
    public void Should_Refuse_Owner_Migrator_Or_Superuser_Identity(string user)
    {
        Should.Throw<InvalidOperationException>(() =>
            PostgresConnectionPolicy.Harden($"Host=localhost;Database=ccr;Username={user}", isDevelopment: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(";SSL Mode=Disable")]
    [InlineData(";SSL Mode=Require")]
    [InlineData(";SSL Mode=VerifyCA")]
    public void Should_Require_VerifyFull_Outside_Development(string sslMode)
    {
        Should.Throw<InvalidOperationException>(() => PostgresConnectionPolicy.Harden(Local + sslMode, isDevelopment: false));
    }

    [Fact]
    public void Should_Accept_VerifyFull_Outside_Development_Without_Error_Detail()
    {
        var hardened = new NpgsqlConnectionStringBuilder(
            PostgresConnectionPolicy.Harden(Tls + ";Include Error Detail=true", isDevelopment: false));

        hardened.SslMode.ShouldBe(SslMode.VerifyFull);
        hardened.IncludeErrorDetail.ShouldBeFalse();
    }

    [Fact]
    public void Should_Allow_Plain_Local_Connection_With_Error_Detail_In_Development()
    {
        var hardened = new NpgsqlConnectionStringBuilder(PostgresConnectionPolicy.Harden(Local, isDevelopment: true));

        hardened.IncludeErrorDetail.ShouldBeTrue();
        hardened.Username.ShouldBe("cr_reader");
    }

    [Fact]
    public void Transport_policy_accepts_the_migrator_but_still_requires_tls_outside_development()
    {
        const string Migrator = "Host=db.internal;Database=ccr;Username=cr_migrator";

        new NpgsqlConnectionStringBuilder(PostgresConnectionPolicy.HardenTransport(Migrator, isDevelopment: true)).Username.ShouldBe("cr_migrator");
        Should.Throw<InvalidOperationException>(() => PostgresConnectionPolicy.HardenTransport(Migrator, isDevelopment: false));
        new NpgsqlConnectionStringBuilder(PostgresConnectionPolicy.HardenTransport(Migrator + ";SSL Mode=VerifyFull;Include Error Detail=true", isDevelopment: false))
            .IncludeErrorDetail.ShouldBeFalse();
    }

    [Fact]
    public void Migration_context_applies_the_transport_policy()
    {
        Should.Throw<InvalidOperationException>(() =>
            CounterpartyRiskDbContext.ForMigrations("Host=db.internal;Database=ccr;Username=cr_migrator", isDevelopment: false));

        using var context = CounterpartyRiskDbContext.ForMigrations(
            "Host=db.internal;Database=ccr;Username=cr_migrator;SSL Mode=VerifyFull", isDevelopment: false);
        context.ShouldNotBeNull();
    }
}