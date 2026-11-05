using System.CodeDom.Compiler;
using System.Reflection;

namespace CounterpartyRisk.Architecture.Tests;

public class LayerDependencyTests
{
    public static TheoryData<string> DomainModules => [.. SolutionModel.DomainModules];

    public static TheoryData<string> Modules => [.. SolutionModel.Modules];

    public static TheoryData<string> HostNames => ["Api", "Bff", "Workers"];

    [Theory]
    [MemberData(nameof(DomainModules))]
    public void Domain_depends_only_on_shared_kernel_and_contracts(string module)
    {
        var scope = new RuleScope(SolutionModel.Layer(module, "Domain"));

        Rules.DomainAllowList(module, scope).ShouldPass($"Domain allow-list ({module})");
        Rules.DomainExclusions(scope).ShouldPass($"Domain exclusions ({module})");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_does_not_depend_on_infrastructure_hosts_or_ef_core(string module)
    {
        var scope = new RuleScope(SolutionModel.Layer(module, "Application"));

        Rules.Application(module, scope).ShouldPass($"Application rule ({module})");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Infrastructure_stays_inside_its_module(string module)
    {
        var scope = new RuleScope(SolutionModel.Layer(module, "Infrastructure"));

        Rules.Infrastructure(module, scope).ShouldPass($"Infrastructure rule ({module})");
    }

    [Fact]
    public void Shared_kernel_does_not_depend_on_frameworks_or_modules()
    {
        var scope = new RuleScope(SolutionModel.SharedKernel);

        Rules.SharedKernelAllowList(scope).ShouldPass("SharedKernel allow-list");
        Rules.DomainExclusions(scope).ShouldPass("SharedKernel exclusions");
    }

    [Fact]
    public void Domain_contracts_reference_the_bcl_only()
    {
        var references = SolutionModel.DomainContracts.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !IsBcl(name))
            .ToList();

        references.ShouldBeEmpty("Domain.Contracts must reference the BCL only (ADR-001 rule 5)");
    }

    [Fact]
    public void Api_does_not_expose_domain_types()
    {
        Rules.ApiWithoutDomain(new RuleScope(SolutionModel.Api)).ShouldPass("Api rule");
    }

    [Fact]
    public void Api_rule_inspects_the_composition_root()
    {
        new RuleScope(SolutionModel.Api).TypeNames().ShouldContain("Program");
    }

    [Theory]
    [MemberData(nameof(HostNames))]
    public void Hosts_go_through_the_dispatcher_only(string host)
    {
        var assembly = System.Reflection.Assembly.Load($"{SolutionModel.Root}.{host}");

        Rules.HostWithoutHandlers(new RuleScope(assembly)).ShouldPass($"Host rule ({host})");
    }

    [Fact]
    public void Api_does_not_reference_domain_projects_directly()
    {
        // Assembly-level check in addition to the type-level one: only Application and
        // Infrastructure projects may be composed by the Api.
        var domainReferences = SolutionModel.Api.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.EndsWith(".Domain", StringComparison.Ordinal))
            .ToList();

        domainReferences.ShouldBeEmpty();
    }

    private static bool IsBcl(string assemblyName) =>
        assemblyName is "netstandard" or "mscorlib"
        || assemblyName.StartsWith("System", StringComparison.Ordinal)
        || assemblyName.StartsWith("Microsoft.Win32", StringComparison.Ordinal);
}

public class ConventionTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] SourceFolders = ["src", "tests"];

    [Fact]
    public void Handlers_are_internal_sealed_and_named_handler()
    {
        var offenders = SolutionModel.AllProductionAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(ImplementsHandlerContract)
            .Where(t => t.IsPublic || t.IsNestedPublic || !t.IsSealed || !t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty("Handlers must be internal sealed and end with 'Handler'");
    }

    [Fact]
    public void No_mvc_controllers_minimal_apis_only()
    {
        var offenders = SolutionModel.AllProductionAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(t => (t.IsPublic && t.Name.EndsWith("Controller", StringComparison.Ordinal))
                || InheritsFrom(t, "Microsoft.AspNetCore.Mvc.ControllerBase"))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty("Controllers are not allowed: use Minimal APIs");
    }

    [Fact]
    public void Assemblies_do_not_use_banned_libraries()
    {
        foreach (var assembly in SolutionModel.AllProductionAssemblies())
        {
            Rules.NoBannedLibrary(new RuleScope(assembly)).ShouldPass($"Licence rule ({assembly.GetName().Name})");

            assembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(IsBanned)
                .ShouldBeEmpty($"{assembly.GetName().Name} references a banned library");
        }
    }

    [Fact]
    public void Logging_uses_the_generator_that_honours_data_classification()
    {
        // The default generator ignores [PersonalData]/[SensitiveData] and logs them in clear text.
        // Its output carries this tool name in GeneratedCodeAttribute.
        var offenders = SolutionModel.AllProductionAssemblies()
            .SelectMany(GetLoadableTypes)
            .SelectMany(t => t.GetMethods(AllDeclared).Cast<MemberInfo>().Append(t))
            .Where(m => m.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "Microsoft.Extensions.Logging.Generators")
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty("Reference Microsoft.Extensions.Telemetry.Abstractions (through SharedKernel.Application)");
    }

    [Fact]
    public void Build_files_do_not_declare_banned_packages()
    {
        var root = SolutionModel.RepositoryRoot();
        // Root props files plus every project under src/ and tests/ (other folders such as
        // .claude/worktrees may hold unrelated checkouts).
        var buildFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
            .Concat(SourceFolders
                .Select(d => Path.Combine(root, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories)))
            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var offenders = new List<string>();
        foreach (var file in buildFiles)
        {
            var packages = System.Xml.Linq.XDocument.Load(file).Descendants()
                .Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion")
                .Select(e => (string?)e.Attribute("Include") ?? string.Empty);

            offenders.AddRange(packages.Where(IsBanned).Select(p => $"{Path.GetRelativePath(root, file)}: {p}"));
        }

        offenders.ShouldBeEmpty("Banned package declared (licence)");
    }

    internal static bool IsBanned(string name) =>
        SolutionModel.BannedLibraries.Any(b => name.Equals(b, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(b + ".", StringComparison.OrdinalIgnoreCase));

    private static bool ImplementsHandlerContract(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetInterfaces().Any(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(SharedKernel.Application.Messaging.ICommandHandler<,>)
                || i.GetGenericTypeDefinition() == typeof(SharedKernel.Application.Messaging.IQueryHandler<,>)));

    private static bool InheritsFrom(Type type, string baseTypeFullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == baseTypeFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
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
