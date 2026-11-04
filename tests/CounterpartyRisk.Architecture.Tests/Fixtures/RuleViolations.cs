// Deliberately broken types used by RuleCanaryTests. Never copy these patterns into src/.
#pragma warning disable CA1812 // Fixture types are inspected, never instantiated
#pragma warning disable IDE0161 // Several namespaces in one file on purpose

namespace CounterpartyRisk.Architecture.Tests.Fixtures.FrameworkLeak
{
    /// <summary>A "domain" type that performs HTTP calls (forbidden in Domain).</summary>
    internal sealed class DomainServiceUsingHttp(HttpClient client)
    {
        public HttpClient Client { get; } = client;
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.CrossModule
{
    /// <summary>A "Capital" type that reaches into the RiskEngine domain (forbidden).</summary>
    internal sealed class CapitalTypeUsingRiskEngine
    {
        public System.Reflection.Assembly RiskEngine { get; } = CounterpartyRisk.RiskEngine.Domain.AssemblyReference.Assembly;
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.DomainLeak
{
    /// <summary>An "Api" type that serialises a domain entity instead of a DTO (BOPLA).</summary>
    internal sealed class EndpointReturningEntity
    {
        public SharedKernel.Domain.IDomainEvent? Payload { get; init; }
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.Clean
{
    /// <summary>Depends on the shared kernel only.</summary>
    internal sealed record CleanValue(SharedKernel.Monetary.Money Amount);
}
