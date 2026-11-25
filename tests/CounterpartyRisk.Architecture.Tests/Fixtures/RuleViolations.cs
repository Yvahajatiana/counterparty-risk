// Deliberately broken types used by RuleCanaryTests. Never copy these patterns into src/.
#pragma warning disable CA1812 // Fixture types are inspected, never instantiated
#pragma warning disable IDE0161 // Several namespaces in one file on purpose

using CounterpartyRisk.SharedKernel.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CounterpartyRisk.Architecture.Tests.Fixtures.FrameworkLeak
{
    /// <summary>A "domain" type that performs HTTP calls (forbidden in Domain).</summary>
    internal sealed class DomainServiceUsingHttp(HttpClient client)
    {
        public HttpClient Client { get; } = client;
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.UnlistedLibrary
{
    /// <summary>Uses a library on no exclusion list: only the allow-list can catch it.</summary>
    internal sealed class DomainServiceUsingLogging(ILogger logger)
    {
        public ILogger Logger { get; } = logger;
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

namespace CounterpartyRisk.Architecture.Tests.Fixtures.ApplicationLeak
{
    /// <summary>A "Capital" application type that reaches into its own Infrastructure (forbidden).</summary>
    internal sealed class ApplicationTypeUsingInfrastructure
    {
        public System.Reflection.Assembly Infrastructure { get; } = CounterpartyRisk.Capital.Infrastructure.AssemblyReference.Assembly;
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.HostLeak
{
    /// <summary>An application type that calls a host-level project (forbidden).</summary>
    internal static class ApplicationTypeUsingServiceDefaults
    {
        public static IServiceCollection Configure(IServiceCollection services) =>
            CounterpartyRisk.ServiceDefaults.ServiceDefaultsExtensions.AddDefaultProblemDetails(services);
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

namespace CounterpartyRisk.Architecture.Tests.Fixtures.HandlerBypass
{
    internal sealed record BypassCommand : ICommand<int>;

    internal sealed record BypassQuery : IQuery<int>;

    /// <summary>A host type that injects a command handler, skipping the pipeline.</summary>
    internal sealed class EndpointCallingCommandHandler(ICommandHandler<BypassCommand, int> handler)
    {
        public Task Run() => handler.Handle(new BypassCommand(), CancellationToken.None);
    }

    /// <summary>A host type that injects a query handler, skipping the pipeline.</summary>
    internal sealed class EndpointCallingQueryHandler(IQueryHandler<BypassQuery, int> handler)
    {
        public Task Run() => handler.Handle(new BypassQuery(), CancellationToken.None);
    }
}

namespace MediatR
{
    /// <summary>Local stand-in: the real package must never be referenced, even by tests.</summary>
    internal interface IMediator
    {
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.BannedLibrary
{
    /// <summary>A type that depends on a banned library.</summary>
    internal sealed class ServiceUsingMediatR(MediatR.IMediator mediator)
    {
        public MediatR.IMediator Mediator { get; } = mediator;
    }
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.Clean
{
    /// <summary>Depends on the shared contracts only.</summary>
    internal sealed record CleanValue(Domain.Contracts.Monetary.Money Amount);
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.HybridRequest
{
    /// <summary>Both a command and a query: forbidden, the authorization pipeline could not tell them apart.</summary>
    [SharedKernel.Application.Security.RequiresCapability(SharedKernel.Application.Security.Capability.ReadExposure)]
    internal sealed record CommandAndQuery : ICommand<int>, IQuery<int>;

    [SharedKernel.Application.Security.RequiresCapability(SharedKernel.Application.Security.Capability.ReadExposure)]
    internal sealed record CommandAndQueryWithTwoResults : ICommand<int>, IQuery<string>;
}

namespace CounterpartyRisk.Architecture.Tests.Fixtures.UndeclaredRequest
{
    /// <summary>No [RequiresCapability]: always refused at runtime, flagged at build time.</summary>
    internal sealed record QueryWithoutCapability : IQuery<int>;

    [SharedKernel.Application.Security.RequiresCapability(SharedKernel.Application.Security.Capability.ReadCapital)]
    internal sealed record DeclaredQuery : IQuery<int>;
}