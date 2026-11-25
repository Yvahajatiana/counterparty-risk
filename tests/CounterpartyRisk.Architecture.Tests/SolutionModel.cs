using System.Reflection;

namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// Description of the solution as fixed by ADR-001: modules, layers and assemblies.
/// A new module or project must be added here so that the rules cover it.
/// </summary>
internal static class SolutionModel
{
    public const string Root = "CounterpartyRisk";

    /// <summary>Bounded contexts that own a Domain layer.</summary>
    public static readonly string[] DomainModules = ["ReferenceData", "MarketData", "Portfolio", "RiskEngine", "Capital"];

    /// <summary>All bounded contexts (Reporting has read models only, no Domain).</summary>
    public static readonly string[] Modules = [.. DomainModules, "Reporting"];

    /// <summary>Libraries banned for licence reasons (ADR-001).</summary>
    public static readonly string[] BannedLibraries =
        ["MediatR", "AutoMapper", "MassTransit", "FluentAssertions", "EPPlus", "OfficeOpenXml", "Duende"];

    public static Assembly SharedKernel => Load("SharedKernel");

    public static Assembly SharedKernelApplication => Load("SharedKernel.Application");

    public static Assembly DomainContracts => Load("Domain.Contracts");

    public static Assembly Api => Load("Api");

    public static Assembly Bff => Load("Bff");

    public static Assembly Workers => Load("Workers");

    public static Assembly ServiceDefaults => Load("ServiceDefaults");

    /// <summary>Shared PostgreSQL foundation (#11): migrations, RLS scope, connection policy.</summary>
    public static Assembly Persistence => Load("Infrastructure.Persistence");

    public static Assembly Layer(string module, string layer) => Load($"{module}.{layer}");

    public static string Namespace(string module, string layer) => $"{Root}.{module}.{layer}";

    /// <summary>Namespace roots of every module other than <paramref name="module"/>.</summary>
    /// <remarks>NetArchTest matches whole namespace segments: no trailing dot.</remarks>
    public static IEnumerable<string> OtherModules(string module) =>
        Modules.Where(m => m != module).Select(m => $"{Root}.{m}");

    public static IEnumerable<Assembly> AllProductionAssemblies()
    {
        yield return SharedKernel;
        yield return SharedKernelApplication;
        yield return DomainContracts;
        yield return ServiceDefaults;
        yield return Persistence;
        yield return Api;
        yield return Bff;
        yield return Workers;
        foreach (var module in DomainModules)
        {
            yield return Layer(module, "Domain");
        }

        foreach (var module in Modules)
        {
            yield return Layer(module, "Application");
            yield return Layer(module, "Infrastructure");
        }
    }

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CounterpartyRisk.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("CounterpartyRisk.slnx not found above the test output directory.");
    }

    private static Assembly Load(string name) => Assembly.Load($"{Root}.{name}");
}
