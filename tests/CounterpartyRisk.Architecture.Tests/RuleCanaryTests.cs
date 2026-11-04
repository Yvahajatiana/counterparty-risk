namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// Proves that each rule detects a violation and names the offending type, using
/// deliberately broken fixtures compiled into this test assembly only.
/// </summary>
public class RuleCanaryTests
{
    private static readonly System.Reflection.Assembly Fixtures = typeof(RuleCanaryTests).Assembly;

    [Fact]
    public void Domain_rule_fails_on_infrastructure_framework_dependency()
    {
        var result = Rules.Domain("Capital", Fixtures, "CounterpartyRisk.Architecture.Tests.Fixtures.FrameworkLeak");

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.FrameworkLeak.DomainServiceUsingHttp).FullName);
    }

    [Fact]
    public void Domain_rule_fails_when_capital_references_risk_engine_domain()
    {
        var result = Rules.Domain("Capital", Fixtures, "CounterpartyRisk.Architecture.Tests.Fixtures.CrossModule");

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.CrossModule.CapitalTypeUsingRiskEngine).FullName);
    }

    [Fact]
    public void Api_rule_fails_when_a_domain_type_is_exposed()
    {
        var result = Rules.ApiWithoutDomain(Fixtures, "CounterpartyRisk.Architecture.Tests.Fixtures.DomainLeak");

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(Fixtures.DomainLeak.EndpointReturningEntity).FullName);
    }

    [Fact]
    public void Domain_rule_passes_on_a_clean_type()
    {
        Rules.Domain("Capital", Fixtures, "CounterpartyRisk.Architecture.Tests.Fixtures.Clean").IsSuccessful.ShouldBeTrue();
    }
}
