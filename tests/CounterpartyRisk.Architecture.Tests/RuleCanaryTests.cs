namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// Proves that each rule detects a violation and names the offending type, using
/// deliberately broken fixtures compiled into this test assembly only.
/// </summary>
public class RuleCanaryTests
{
    private const string FixtureRoot = "CounterpartyRisk.Architecture.Tests.Fixtures";

    [Fact]
    public void Domain_exclusions_fail_on_infrastructure_framework_dependency()
    {
        var result = Rules.DomainExclusions(Fixture("FrameworkLeak"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.FrameworkLeak.DomainServiceUsingHttp).FullName);
    }

    [Fact]
    public void Domain_allow_list_fails_on_a_library_nobody_listed()
    {
        var result = Rules.DomainAllowList("Capital", Fixture("UnlistedLibrary"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.UnlistedLibrary.DomainServiceUsingLogging).FullName);
    }

    [Fact]
    public void Domain_allow_list_fails_when_capital_references_risk_engine_domain()
    {
        var result = Rules.DomainAllowList("Capital", Fixture("CrossModule"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.CrossModule.CapitalTypeUsingRiskEngine).FullName);
    }

    [Fact]
    public void Domain_rules_pass_on_a_clean_type()
    {
        Rules.DomainAllowList("Capital", Fixture("Clean")).ShouldPass("Domain allow-list");
        Rules.DomainExclusions(Fixture("Clean")).ShouldPass("Domain exclusions");
    }

    [Fact]
    public void Application_rule_fails_on_infrastructure_dependency()
    {
        var result = Rules.Application("Capital", Fixture("ApplicationLeak"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.ApplicationLeak.ApplicationTypeUsingInfrastructure).FullName);
    }

    [Fact]
    public void Application_rule_fails_on_service_defaults_dependency()
    {
        var result = Rules.Application("Capital", Fixture("HostLeak"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.HostLeak.ApplicationTypeUsingServiceDefaults).FullName);
    }

    [Fact]
    public void Infrastructure_rule_fails_on_another_module()
    {
        var result = Rules.Infrastructure("Capital", Fixture("CrossModule"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.CrossModule.CapitalTypeUsingRiskEngine).FullName);
    }

    [Fact]
    public void Shared_persistence_rule_fails_on_a_module_dependency()
    {
        var result = Rules.SharedPersistence(Fixture("CrossModule"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.CrossModule.CapitalTypeUsingRiskEngine).FullName);
    }

    [Fact]
    public void Api_rule_fails_when_a_domain_type_is_exposed()
    {
        var result = Rules.ApiWithoutDomain(Fixture("DomainLeak"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.DomainLeak.EndpointReturningEntity).FullName);
    }

    [Fact]
    public void Host_rule_fails_when_a_handler_is_injected_directly()
    {
        var result = Rules.HostWithoutHandlers(Fixture("HandlerBypass"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.HandlerBypass.EndpointCallingCommandHandler).FullName);
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.HandlerBypass.EndpointCallingQueryHandler).FullName);
    }

    [Fact]
    public void Licence_rule_fails_on_a_banned_library()
    {
        var result = Rules.NoBannedLibrary(Fixture("BannedLibrary"));

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.BannedLibrary.ServiceUsingMediatR).FullName);
    }

    [Theory]
    [InlineData("MediatR")]
    [InlineData("MediatR.Contracts")]
    [InlineData("fluentassertions")]
    [InlineData("Duende.IdentityServer")]
    [InlineData("MassTransit.RabbitMQ")]
    public void Banned_package_names_are_recognised(string package)
    {
        ConventionTests.IsBanned(package).ShouldBeTrue();
    }

    [Fact]
    public void A_scope_selecting_nothing_is_an_error()
    {
        Should.Throw<InvalidOperationException>(() => Rules.ApiWithoutDomain(Fixture("DoesNotExist")));
    }

    private static RuleScope Fixture(string name) => new(typeof(RuleCanaryTests).Assembly, $"{FixtureRoot}.{name}");
}
