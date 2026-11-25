using System.Collections.Frozen;

namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Authorization matrix of threat model section 7 (validated by the Expert CCR, PR #48 and #52), coded once.
/// The API builds one named ASP.NET Core policy per capability from this class, and
/// <c>AuthorizationBehavior</c> applies the same rules to every dispatched request.
/// Any change of this class needs a new business review.
/// </summary>
public static class Policies
{
    // Policy names, one per capability (used by endpoints: RequireAuthorization(Policies.ReadExposure)).
    public const string ReadExposure = nameof(Capability.ReadExposure);
    public const string ReadLimits = nameof(Capability.ReadLimits);
    public const string ReadCva = nameof(Capability.ReadCva);
    public const string ReadRiskOversight = nameof(Capability.ReadRiskOversight);
    public const string ReadCapital = nameof(Capability.ReadCapital);
    public const string ReadMarketAndBenchmark = nameof(Capability.ReadMarketAndBenchmark);
    public const string Export = nameof(Capability.Export);
    public const string ManageLimitThresholds = nameof(Capability.ManageLimitThresholds);
    public const string ManageLimits = nameof(Capability.ManageLimits);
    public const string ApproveLimitChange = nameof(Capability.ApproveLimitChange);
    public const string ReadAudit = nameof(Capability.ReadAudit);
    public const string ManageMethodVersions = nameof(Capability.ManageMethodVersions);
    public const string ApproveMethodVersion = nameof(Capability.ApproveMethodVersion);
    public const string ManageJobRuns = nameof(Capability.ManageJobRuns);
    public const string ManageHistoryCorrections = nameof(Capability.ManageHistoryCorrections);
    public const string ApproveHistoryCorrection = nameof(Capability.ApproveHistoryCorrection);
    public const string ReadDataQualityDetail = nameof(Capability.ReadDataQualityDetail);

    private static readonly FrozenDictionary<Capability, FrozenSet<string>> RolesByCapability =
        new Dictionary<Capability, string[]>
        {
            [Capability.ReadExposure] = [CcrRoles.RiskManager, CcrRoles.FrontOffice, CcrRoles.Direction, CcrRoles.Finance],
            [Capability.ReadLimits] = [CcrRoles.RiskManager, CcrRoles.FrontOffice, CcrRoles.Direction, CcrRoles.Finance, CcrRoles.LimitAdmin],
            [Capability.ReadCva] = [CcrRoles.RiskManager, CcrRoles.FrontOffice, CcrRoles.Direction, CcrRoles.Finance],
            [Capability.ReadRiskOversight] = [CcrRoles.RiskManager, CcrRoles.Direction, CcrRoles.Finance],
            [Capability.ReadCapital] = [CcrRoles.RiskManager, CcrRoles.Direction, CcrRoles.Finance],
            [Capability.ReadMarketAndBenchmark] = [CcrRoles.RiskManager, CcrRoles.FrontOffice, CcrRoles.Direction, CcrRoles.Finance],
            [Capability.Export] = [CcrRoles.RiskManager, CcrRoles.Direction, CcrRoles.Finance],
            [Capability.ManageLimitThresholds] = [CcrRoles.LimitAdmin],
            [Capability.ManageLimits] = [CcrRoles.LimitAdmin],
            [Capability.ApproveLimitChange] = [CcrRoles.Direction],
            [Capability.ReadAudit] = [CcrRoles.Direction, CcrRoles.Auditor],
            [Capability.ManageMethodVersions] = [CcrRoles.RiskManager],
            [Capability.ApproveMethodVersion] = [CcrRoles.Direction],
            [Capability.ManageJobRuns] = [CcrRoles.RiskManager],
            [Capability.ManageHistoryCorrections] = [CcrRoles.RiskManager],
            [Capability.ApproveHistoryCorrection] = [CcrRoles.Direction],
            [Capability.ReadDataQualityDetail] = [CcrRoles.RiskManager, CcrRoles.Direction, CcrRoles.Finance, CcrRoles.Auditor],
        }.ToFrozenDictionary(e => e.Key, e => e.Value.ToFrozenSet(StringComparer.Ordinal));

    /// <summary>Capabilities that require a multi-factor authentication level (SEC-073, SEC-083, SEC-122).</summary>
    private static readonly FrozenSet<Capability> MultiFactorCapabilities =
        new[] { Capability.Export, Capability.ManageLimits, Capability.ApproveLimitChange, Capability.ManageLimitThresholds }.ToFrozenSet();

    /// <summary>
    /// Capabilities served without any data scope (auditor, section 7.4): the audit trail and the
    /// aggregated data quality view. Every other capability returns 403 on an empty scope.
    /// </summary>
    private static readonly FrozenSet<Capability> ScopeFreeCapabilities =
        new[] { Capability.ReadAudit, Capability.ReadDataQualityDetail }.ToFrozenSet();

    /// <summary>Governance capabilities that require a scope covering every legal entity (rule R12).</summary>
    private static readonly FrozenSet<Capability> GlobalScopeCapabilities =
        new[]
        {
            Capability.ManageMethodVersions, Capability.ApproveMethodVersion, Capability.ManageJobRuns,
            Capability.ManageHistoryCorrections, Capability.ApproveHistoryCorrection,
        }.ToFrozenSet();

    /// <summary>Incompatible role pairs (segregation of duties, section 7.3).</summary>
    public static IReadOnlyList<(string First, string Second)> IncompatibleRolePairs { get; } =
    [
        (CcrRoles.LimitAdmin, CcrRoles.FrontOffice),
        (CcrRoles.LimitAdmin, CcrRoles.RiskManager),
        (CcrRoles.LimitAdmin, CcrRoles.Direction),
        (CcrRoles.RiskManager, CcrRoles.Direction),
        (CcrRoles.FrontOffice, CcrRoles.RiskManager),
        (CcrRoles.FrontOffice, CcrRoles.Direction),
        .. CcrRoles.Business.Select(role => (CcrRoles.Auditor, role)),
    ];

    /// <summary>Every capability, in declaration order.</summary>
    public static IReadOnlyList<Capability> AllCapabilities { get; } = Enum.GetValues<Capability>();

    public static string PolicyName(Capability capability) => capability.ToString();

    public static IReadOnlySet<string> RolesFor(Capability capability) =>
        RolesByCapability.TryGetValue(capability, out var roles) ? roles : FrozenSet<string>.Empty;

    public static bool RequiresMultiFactor(Capability capability) => MultiFactorCapabilities.Contains(capability);

    public static bool RequiresDataScope(Capability capability) => !ScopeFreeCapabilities.Contains(capability);

    public static bool RequiresAllLegalEntities(Capability capability) => GlobalScopeCapabilities.Contains(capability);

    /// <summary>Capabilities granted by a set of realm roles. Unknown roles grant nothing.</summary>
    public static IReadOnlySet<Capability> CapabilitiesFor(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var roleSet = roles.ToHashSet(StringComparer.Ordinal);
        return RolesByCapability
            .Where(entry => entry.Value.Overlaps(roleSet))
            .Select(entry => entry.Key)
            .ToFrozenSet();
    }

    /// <summary>First incompatible pair held by <paramref name="roles"/>, or null when duties are segregated.</summary>
    public static (string First, string Second)? FindIncompatiblePair(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var roleSet = roles.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in IncompatibleRolePairs)
        {
            if (roleSet.Contains(pair.First) && roleSet.Contains(pair.Second))
            {
                return pair;
            }
        }

        return null;
    }

    /// <summary>
    /// Startup check (fail closed, section 7.3): every capability has at least one role, every role
    /// is known, and the incompatible pairs table is loaded and non-empty.
    /// </summary>
    public static void EnsureConsistent()
    {
        if (IncompatibleRolePairs.Count == 0)
        {
            throw new InvalidOperationException("The incompatible role pairs table is empty: refusing to start.");
        }

        foreach (var capability in AllCapabilities)
        {
            var roles = RolesFor(capability);
            if (roles.Count == 0)
            {
                throw new InvalidOperationException($"Capability {capability} is granted to no role: refusing to start.");
            }

            var unknown = roles.Where(r => !CcrRoles.All.Contains(r, StringComparer.Ordinal)).ToList();
            if (unknown.Count > 0)
            {
                throw new InvalidOperationException($"Capability {capability} references unknown roles: refusing to start.");
            }
        }
    }
}
