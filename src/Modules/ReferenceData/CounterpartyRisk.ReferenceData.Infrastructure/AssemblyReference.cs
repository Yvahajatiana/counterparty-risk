using System.Reflection;

namespace CounterpartyRisk.ReferenceData.Infrastructure;

/// <summary>
/// Assembly marker used for assembly scanning and architecture tests.
/// </summary>
public static class AssemblyReference
{
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
