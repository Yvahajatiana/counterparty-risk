using CounterpartyRisk.SharedKernel.Application.Security;

namespace CounterpartyRisk.SharedKernel.UnitTests.Security;

/// <summary>Authorization matrix of threat model section 7.1, row by row (any change needs a business review).</summary>
public class PoliciesTests
{
    private const string Rm = CcrRoles.RiskManager;
    private const string Fo = CcrRoles.FrontOffice;
    private const string Dir = CcrRoles.Direction;
    private const string Fin = CcrRoles.Finance;
    private const string Lim = CcrRoles.LimitAdmin;
    private const string Aud = CcrRoles.Auditor;

    public static TheoryData<Capability, string[]> Matrix => new()
    {
        { Capability.ReadExposure, [Rm, Fo, Dir, Fin] },
        { Capability.ReadLimits, [Rm, Fo, Dir, Fin, Lim] },
        { Capability.ReadCva, [Rm, Fo, Dir, Fin] },
        { Capability.ReadRiskOversight, [Rm, Dir, Fin] },
        { Capability.ReadCapital, [Rm, Dir, Fin] },
        { Capability.ReadMarketAndBenchmark, [Rm, Fo, Dir, Fin] },
        { Capability.Export, [Rm, Dir, Fin] },
        { Capability.ManageLimitThresholds, [Lim] },
        { Capability.ManageLimits, [Lim] },
        { Capability.ApproveLimitChange, [Dir] },
        { Capability.ReadAudit, [Dir, Aud] },
        { Capability.ManageMethodVersions, [Rm] },
        { Capability.ApproveMethodVersion, [Dir] },
        { Capability.ManageJobRuns, [Rm] },
        { Capability.ManageHistoryCorrections, [Rm] },
        { Capability.ApproveHistoryCorrection, [Dir] },
        { Capability.ReadDataQualityDetail, [Rm, Dir, Fin, Aud] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Each_capability_is_granted_exactly_to_the_roles_of_the_matrix(Capability capability, string[] roles)
    {
        Policies.RolesFor(capability).Order(StringComparer.Ordinal).ShouldBe(roles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_matrix_covers_every_capability()
    {
        Matrix.Select(row => (Capability)row.Data.Item1).ShouldBe(Policies.AllCapabilities, ignoreOrder: true);
    }

    [Fact]
    public void Policy_names_are_the_capability_names()
    {
        Policies.ReadCapital.ShouldBe(Policies.PolicyName(Capability.ReadCapital));
        Policies.AllCapabilities.Select(Policies.PolicyName).Distinct(StringComparer.Ordinal).Count().ShouldBe(Policies.AllCapabilities.Count);
    }

    [Fact]
    public void Front_office_never_reads_capital_oversight_or_exports()
    {
        var capabilities = Policies.CapabilitiesFor([Fo]);

        capabilities.ShouldNotContain(Capability.ReadCapital);
        capabilities.ShouldNotContain(Capability.ReadRiskOversight);
        capabilities.ShouldNotContain(Capability.Export);
        capabilities.ShouldContain(Capability.ReadExposure);
    }

    [Fact]
    public void Unknown_roles_grant_nothing()
    {
        Policies.CapabilitiesFor(["offline_access", "admin", "CCR-DIRECTION"]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Lim, Fo)]
    [InlineData(Lim, Rm)]
    [InlineData(Lim, Dir)]
    [InlineData(Rm, Dir)]
    [InlineData(Fo, Rm)]
    [InlineData(Fo, Dir)]
    [InlineData(Aud, Rm)]
    [InlineData(Aud, Fo)]
    [InlineData(Aud, Dir)]
    [InlineData(Aud, Fin)]
    [InlineData(Aud, Lim)]
    public void Incompatible_pairs_are_detected_in_any_order(string first, string second)
    {
        Policies.FindIncompatiblePair([first, second]).ShouldNotBeNull();
        Policies.FindIncompatiblePair([second, first]).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(Fin, Rm)]
    [InlineData(Fin, Dir)]
    [InlineData(Fin, Fo)]
    [InlineData(Fin, Lim)]
    public void Compatible_pairs_are_accepted(string first, string second)
    {
        Policies.FindIncompatiblePair([first, second]).ShouldBeNull();
    }

    [Fact]
    public void The_matrix_is_consistent_at_startup()
    {
        Should.NotThrow(Policies.EnsureConsistent);
        Policies.IncompatibleRolePairs.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(Capability.Export, true)]
    [InlineData(Capability.ManageLimits, true)]
    [InlineData(Capability.ApproveLimitChange, true)]
    [InlineData(Capability.ManageLimitThresholds, true)]
    [InlineData(Capability.ReadExposure, false)]
    public void Sensitive_capabilities_require_multi_factor(Capability capability, bool expected)
    {
        Policies.RequiresMultiFactor(capability).ShouldBe(expected);
    }

    [Theory]
    [InlineData(Capability.ReadAudit, false)]
    [InlineData(Capability.ReadDataQualityDetail, false)]
    [InlineData(Capability.ReadExposure, true)]
    [InlineData(Capability.ReadLimits, true)]
    public void Only_audit_capabilities_are_served_without_data_scope(Capability capability, bool expected)
    {
        Policies.RequiresDataScope(capability).ShouldBe(expected);
    }

    [Theory]
    [InlineData(Capability.ManageMethodVersions)]
    [InlineData(Capability.ApproveMethodVersion)]
    [InlineData(Capability.ManageJobRuns)]
    [InlineData(Capability.ManageHistoryCorrections)]
    [InlineData(Capability.ApproveHistoryCorrection)]
    public void Governance_capabilities_require_every_legal_entity(Capability capability)
    {
        Policies.RequiresAllLegalEntities(capability).ShouldBeTrue();
    }

    [Fact]
    public void An_undefined_capability_has_no_role()
    {
        Policies.RolesFor((Capability)999).ShouldBeEmpty();
    }
}
