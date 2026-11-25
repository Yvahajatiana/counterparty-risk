using System.Reflection;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Application.Security;

namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// Structural authorization rules (#9): every request declares its capabilities, and no request is both
/// a command and a query (the shared pipeline could not tell the operation apart).
/// </summary>
public class SecurityConventionTests
{
    [Fact]
    public void Requests_are_never_both_command_and_query()
    {
        SecurityRules.HybridRequests(ProductionTypes()).ShouldBeEmpty("A request is either a command or a query (#9)");
    }

    [Fact]
    public void Every_request_declares_its_capabilities()
    {
        SecurityRules.UndeclaredRequests(ProductionTypes())
            .ShouldBeEmpty("Add [RequiresCapability(...)]: a request without it is always refused (deny by default)");
    }

    [Fact]
    public void Hybrid_request_canary_is_detected()
    {
        SecurityRules.HybridRequests(FixtureTypes("HybridRequest"))
            .ShouldBe([typeof(Fixtures.HybridRequest.CommandAndQuery).FullName, typeof(Fixtures.HybridRequest.CommandAndQueryWithTwoResults).FullName],
                ignoreOrder: true);
    }

    [Fact]
    public void Undeclared_request_canary_is_detected()
    {
        SecurityRules.UndeclaredRequests(FixtureTypes("UndeclaredRequest"))
            .ShouldBe([typeof(Fixtures.UndeclaredRequest.QueryWithoutCapability).FullName]);
    }

    private static IEnumerable<Type> ProductionTypes() =>
        SolutionModel.AllProductionAssemblies().SelectMany(LoadableTypes);

    private static IEnumerable<Type> FixtureTypes(string fixture) =>
        LoadableTypes(typeof(SecurityConventionTests).Assembly)
            .Where(t => t.Namespace == $"CounterpartyRisk.Architecture.Tests.Fixtures.{fixture}");

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}

internal static class SecurityRules
{
    public static IReadOnlyList<string?> HybridRequests(IEnumerable<Type> types) =>
        [.. types.Where(t => t is { IsClass: true, IsAbstract: false } && Implements(t, typeof(ICommand<>)) && Implements(t, typeof(IQuery<>)))
            .Select(t => t.FullName)];

    public static IReadOnlyList<string?> UndeclaredRequests(IEnumerable<Type> types) =>
        [.. types.Where(t => t is { IsClass: true, IsAbstract: false } && Implements(t, typeof(IRequest<>)))
            .Where(t => !t.GetCustomAttributes<RequiresCapabilityAttribute>(inherit: false).SelectMany(a => a.Capabilities).Any())
            .Select(t => t.FullName)];

    private static bool Implements(Type type, Type openGeneric) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric);
}
