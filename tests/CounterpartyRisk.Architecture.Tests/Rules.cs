using System.Reflection;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// Dependency rules of ADR-001, written once and applied both to the real assemblies
/// and to the canary fixtures that prove each rule actually detects a violation.
/// </summary>
internal static class Rules
{
    private static readonly string[] InfrastructureFrameworks =
        ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Net.Http", "Npgsql"];

    private static readonly string[] Hosts =
        [$"{SolutionModel.Root}.Api", $"{SolutionModel.Root}.Bff", $"{SolutionModel.Root}.Workers", $"{SolutionModel.Root}.AppHost"];

    /// <summary>Rule 1: Domain depends only on SharedKernel and Domain.Contracts.</summary>
    public static TestResult Domain(string module, Assembly assembly, string namespacePrefix) =>
        Select(assembly, namespacePrefix)
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                .. InfrastructureFrameworks,
                .. Hosts,
                $"{SolutionModel.Root}.SharedKernel.Application",
                SolutionModel.Namespace(module, "Application"),
                SolutionModel.Namespace(module, "Infrastructure"),
                .. SolutionModel.OtherModules(module),
            ])
            .GetResult();

    /// <summary>Rule 2: Application never depends on Infrastructure, hosts or EF Core.</summary>
    public static TestResult Application(string module, Assembly assembly, string namespacePrefix) =>
        Select(assembly, namespacePrefix)
            .ShouldNot()
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
    public static TestResult Infrastructure(string module, Assembly assembly, string namespacePrefix) =>
        Select(assembly, namespacePrefix)
            .ShouldNot()
            .HaveDependencyOnAny([.. Hosts, .. SolutionModel.OtherModules(module)])
            .GetResult();

    /// <summary>Rule 6: the Api exposes DTOs only, never a domain type.</summary>
    public static TestResult ApiWithoutDomain(Assembly assembly, string namespacePrefix) =>
        Select(assembly, namespacePrefix)
            .ShouldNot()
            .HaveDependencyOnAny(
            [
                .. SolutionModel.DomainModules.Select(m => SolutionModel.Namespace(m, "Domain")),
                $"{SolutionModel.Root}.SharedKernel.Domain",
            ])
            .GetResult();

    /// <summary>Licence rule: no banned library in any assembly.</summary>
    public static TestResult NoBannedLibrary(Assembly assembly) =>
        Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(SolutionModel.BannedLibraries)
            .GetResult();

    private static PredicateList Select(Assembly assembly, string namespacePrefix) =>
        Types.InAssembly(assembly).That().ResideInNamespaceStartingWith(namespacePrefix);
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
