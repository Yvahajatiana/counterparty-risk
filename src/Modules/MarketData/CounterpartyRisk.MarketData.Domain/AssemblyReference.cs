using System.Reflection;

namespace CounterpartyRisk.MarketData.Domain;

/// <summary>
/// Assembly marker used for assembly scanning and architecture tests.
/// </summary>
public static class AssemblyReference
{
    public static Assembly Assembly { get; } = typeof(AssemblyReference).Assembly;
}
