using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;

namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Declares the capabilities a command or query requires. Several attributes (or capabilities) are
/// cumulative: an export of W6 needs <see cref="Capability.Export"/> AND <see cref="Capability.ReadCapital"/>
/// (rule R5). A request without this attribute is refused by <c>AuthorizationBehavior</c> (deny by default)
/// and flagged by the architecture tests.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresCapabilityAttribute(params Capability[] capabilities) : Attribute
{
    public IReadOnlyList<Capability> Capabilities { get; } = capabilities;
}

/// <summary>
/// Query filtered on the data scope axes. A null or empty filter means "my whole scope"; a filter
/// outside the user scope is refused with 403. The effective scope is published through
/// <see cref="IRequestScope"/> for the handler and the RLS interceptor (#11).
/// </summary>
public interface IScopedQuery
{
    /// <summary>Requested legal entities (LEI), or null for the whole user scope.</summary>
    IReadOnlyCollection<string>? LegalEntities { get; }

    /// <summary>Requested business line codes, or null for the whole user scope.</summary>
    IReadOnlyCollection<string>? BusinessLines { get; }
}

/// <summary>Capabilities required by a request type, read once from its attributes.</summary>
internal static class RequestAuthorization
{
    private static readonly ConcurrentDictionary<Type, FrozenSet<Capability>> Cache = new();

    public static IReadOnlySet<Capability> RequiredCapabilities(Type requestType) =>
        Cache.GetOrAdd(requestType, static type => type
            .GetCustomAttributes<RequiresCapabilityAttribute>(inherit: false)
            .SelectMany(a => a.Capabilities)
            .Where(Enum.IsDefined)
            .ToFrozenSet());
}
