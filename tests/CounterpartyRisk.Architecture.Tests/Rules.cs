using System.Reflection;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// Types a rule applies to: a whole production assembly, or one fixture namespace of this
/// test assembly. Selecting nothing is an error, so that a rule can never pass on an empty set.
/// </summary>
internal sealed record RuleScope(Assembly Assembly, string? FixtureNamespace = null)
{
    public Conditions Should() => Select(out var types) is { } selection ? selection.Should() : types.Should();

    public Conditions ShouldNot() => Select(out var types) is { } selection ? selection.ShouldNot() : types.ShouldNot();

    public IReadOnlyList<string> TypeNames()
    {
        var selection = Select(out var types);
        var selected = selection is null ? types.GetTypes() : selection.GetTypes();
        return [.. selected.Select(t => t.FullName ?? t.Name)];
    }

    private PredicateList? Select(out Types types)
    {
        types = Types.InAssembly(Assembly);
        var selection = FixtureNamespace is null ? null : types.That().ResideInNamespaceStartingWith(FixtureNamespace);
        var selected = selection is null ? types.GetTypes() : selection.GetTypes();
        if (!selected.Any())
        {
            throw new InvalidOperationException($"Rule scope {this} selects no type: the rule would pass without checking anything.");
        }

        return selection;
    }
}

/// <summary>
/// Dependency rules of ADR-001, written once and applied both to the real assemblies
/// and to the canary fixtures that prove each rule actually detects a violation.
/// </summary>
internal static class Rules
{
    private static readonly string[] InfrastructureFrameworks =
        ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Net.Http", "Npgsql"];

    private static readonly string[] Hosts =
    [
        $"{SolutionModel.Root}.Api",
        $"{SolutionModel.Root}.Bff",
        $"{SolutionModel.Root}.Workers",
        $"{SolutionModel.Root}.AppHost",
        $"{SolutionModel.Root}.ServiceDefaults",
    ];

    /// <summary>
    /// Handler contracts, in the metadata form used by NetArchTest (generic arity suffix).
    /// </summary>
    private static readonly string[] HandlerContracts =
    [
        $"{SolutionModel.Root}.SharedKernel.Application.Messaging.ICommandHandler`2",
        $"{SolutionModel.Root}.SharedKernel.Application.Messaging.IQueryHandler`2",
    ];

    /// <summary>Rule 1, allow-list: Domain depends only on the BCL, SharedKernel and Domain.Contracts.</summary>
    public static TestResult DomainAllowList(string module, RuleScope scope) =>
        scope.Should()
            .OnlyHaveDependenciesOn(
            [
                "System",
                "Microsoft.CodeAnalysis",
                $"{SolutionModel.Root}.SharedKernel",
                $"{SolutionModel.Root}.Domain.Contracts",
                SolutionModel.Namespace(module, "Domain"),
                .. FixtureAllowance(scope),
            ])
            .GetResult();

    /// <summary>
    /// Rule 1, exclusions: the allow-list matches by prefix, so "System" and
    /// "CounterpartyRisk.SharedKernel" still let these namespaces through.
    /// </summary>
    public static TestResult DomainExclusions(RuleScope scope) =>
        scope.ShouldNot()
            .HaveDependencyOnAny([.. InfrastructureFrameworks, $"{SolutionModel.Root}.SharedKernel.Application"])
            .GetResult();

    /// <summary>SharedKernel: the BCL and data classification attributes only.</summary>
    public static TestResult SharedKernelAllowList(RuleScope scope) =>
        scope.Should()
            .OnlyHaveDependenciesOn(
            [
                "System",
                "Microsoft.CodeAnalysis",
                "Microsoft.Extensions.Compliance",
                $"{SolutionModel.Root}.SharedKernel",
            ])
            .GetResult();

    /// <summary>Rule 2: Application never depends on Infrastructure, hosts or EF Core.</summary>
    public static TestResult Application(string module, RuleScope scope) =>
        scope.ShouldNot()
            .HaveDependencyOnAny(
            [
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql",
                .. Hosts,
                SolutionModel.Namespace(module, "Infrastructure"),
                .. SolutionModel.OtherModules(module),
            ])
            .GetResult();

    /// <summary>Rules 3 and 4: Infrastructure stays inside its module and never calls a host.</summary>
    public static TestResult Infrastructure(string module, RuleScope scope) =>
        scope.ShouldNot()
            .HaveDependencyOnAny([.. Hosts, .. SolutionModel.OtherModules(module)])
            .GetResult();

    /// <summary>
    /// Shared persistence (#11) is used by every module Infrastructure: it never depends on a module
    /// or a host, otherwise one module would reach another through it (rule 4).
    /// </summary>
    public static TestResult SharedPersistence(RuleScope scope) =>
        scope.ShouldNot()
            .HaveDependencyOnAny([.. Hosts, .. SolutionModel.Modules.Select(m => $"{SolutionModel.Root}.{m}")])
            .GetResult();

    /// <summary>Rule 6: the Api exposes DTOs only, never a domain type.</summary>
    public static TestResult ApiWithoutDomain(RuleScope scope) =>
        scope.ShouldNot()
            .HaveDependencyOnAny(
            [
                .. SolutionModel.DomainModules.Select(m => SolutionModel.Namespace(m, "Domain")),
                $"{SolutionModel.Root}.SharedKernel.Domain",
            ])
            .GetResult();

    /// <summary>Rule 6: hosts go through IDispatcher, a direct handler call skips the pipeline.</summary>
    public static TestResult HostWithoutHandlers(RuleScope scope) =>
        scope.ShouldNot().HaveDependencyOnAny(HandlerContracts).GetResult();

    /// <summary>Licence rule: no banned library in any assembly.</summary>
    public static TestResult NoBannedLibrary(RuleScope scope) =>
        scope.ShouldNot().HaveDependencyOnAny(SolutionModel.BannedLibraries).GetResult();

    // A fixture lives in this test assembly: its own namespace is not a dependency to report.
    private static string[] FixtureAllowance(RuleScope scope) =>
        scope.FixtureNamespace is null ? [] : [scope.FixtureNamespace];
}

internal static class TestResultExtensions
{
    /// <summary>Fails with the full name of every offending type.</summary>
    public static void ShouldPass(this TestResult result, string rule)
    {
        var offenders = result.FailingTypeNames ?? [];
        result.IsSuccessful.ShouldBeTrue($"{rule} violated by: {string.Join(", ", offenders)}");
    }
}
