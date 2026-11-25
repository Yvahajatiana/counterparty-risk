using System.Security.Claims;
using CounterpartyRisk.SharedKernel.Application.Security;
using static CounterpartyRisk.SharedKernel.UnitTests.Security.TestUsers;

namespace CounterpartyRisk.SharedKernel.UnitTests.Security;

public class AuthenticatedUserFactoryTests
{
    [Fact]
    public void Anonymous_principal_gives_no_user()
    {
        AuthenticatedUserFactory.Create(null, NoMfa).ShouldBeNull();
        AuthenticatedUserFactory.Create(new ClaimsPrincipal(new ClaimsIdentity()), NoMfa).ShouldBeNull();
        AuthenticatedUserFactory.Create(Principal("x", [CcrRoles.Direction], ["*"], ["*"], authenticated: false), NoMfa).ShouldBeNull();
    }

    [Fact]
    public void Scope_and_capabilities_come_from_the_token_claims()
    {
        var user = RiskManagerFr();

        user.IsValid.ShouldBeTrue();
        user.Issuer.ShouldBe(Issuer);
        user.Subject.ShouldBe("rm");
        user.Scope.LegalEntities.ShouldBe([ParentFr]);
        user.Scope.BusinessLines.ShouldBe([Markets]);
        user.Scope.IsGlobal.ShouldBeFalse();
        user.Has(Capability.ReadCapital).ShouldBeTrue();
        user.Has(Capability.ApproveLimitChange).ShouldBeFalse();
    }

    [Fact]
    public void Wildcards_grant_every_entity_and_business_line()
    {
        var user = Direction();

        user.Scope.AllLegalEntities.ShouldBeTrue();
        user.Scope.AllBusinessLines.ShouldBeTrue();
        user.Scope.IsGlobal.ShouldBeTrue();
        user.Scope.RequiresAllocatedMeasures.ShouldBeFalse();
    }

    [Fact]
    public void Unknown_roles_are_dropped()
    {
        var user = Build("x", [CcrRoles.Finance, "offline_access", "default-roles-ccr"], [SubsidiaryDe], ["*"])!;

        user.Roles.ShouldBe([CcrRoles.Finance]);
    }

    [Fact]
    public void Should_Return403_When_Token_Has_Incompatible_Roles()
    {
        var user = Build("x", [CcrRoles.LimitAdmin, CcrRoles.FrontOffice], [ParentFr], [Markets])!;

        user.Fault.ShouldBe(PrincipalFault.IncompatibleRoles);
        user.IsValid.ShouldBeFalse();
        user.Capabilities.ShouldBeEmpty();
        user.Has(Capability.ReadLimits).ShouldBeFalse();
        user.Scope.IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("*", "MARKETS")]
    [InlineData("CCRSYNTHPARENTFR0057", "*")]
    public void Should_Return403_When_FrontOffice_Scope_Is_Wildcard(string legalEntity, string businessLine)
    {
        var user = Build("fo", [CcrRoles.FrontOffice], [legalEntity], [businessLine])!;

        user.Fault.ShouldBe(PrincipalFault.FrontOfficeWildcardScope);
        user.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("CCRSYNTHPARENTFR0058", "MARKETS")]
    [InlineData("not-a-lei", "MARKETS")]
    [InlineData("CCRSYNTHPARENTFR0057", "markets")]
    [InlineData("CCRSYNTHPARENTFR0057", "MARKETS;DROP")]
    public void Malformed_scope_claims_invalidate_the_principal(string legalEntity, string businessLine)
    {
        var user = Build("x", [CcrRoles.RiskManager], [legalEntity], [businessLine])!;

        user.Fault.ShouldBe(PrincipalFault.InvalidScopeClaim);
        user.Scope.ShouldBe(UserScope.Empty);
    }

    [Fact]
    public void Missing_subject_or_issuer_invalidates_the_principal()
    {
        Build(null, [CcrRoles.Direction], ["*"], ["*"])!.Fault.ShouldBe(PrincipalFault.MissingIdentity);

        var noIssuer = AuthenticatedUserFactory.Create(Principal("x", [CcrRoles.Direction], ["*"], ["*"], issuer: null), NoMfa)!;
        noIssuer.Fault.ShouldBe(PrincipalFault.MissingIdentity);
    }

    [Fact]
    public void Duplicated_subject_claims_are_refused()
    {
        var principal = Principal("a", [CcrRoles.Direction], ["*"], ["*"]);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(CcrClaimTypes.Subject, "b"));

        AuthenticatedUserFactory.Create(principal, NoMfa)!.Fault.ShouldBe(PrincipalFault.MissingIdentity);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("pwd", false)]
    [InlineData("1", false)]
    [InlineData("mfa", true)]
    public void Multi_factor_level_comes_from_the_acr_claim(string? acr, bool expected)
    {
        LimitAdminFr(acr, MfaRequired).MultiFactor.ShouldBe(expected);
    }

    [Fact]
    public void Multi_factor_is_not_checked_when_disabled()
    {
        LimitAdminFr(null, NoMfa).MultiFactor.ShouldBeTrue();
        MultiFactorPolicy.Strict.Required.ShouldBeTrue();
        MultiFactorPolicy.Strict.AcceptedAcrValues.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_scope_claims_give_an_empty_scope()
    {
        RiskManagerWithoutEntity().Scope.IsEmpty.ShouldBeTrue();
        Auditor().Scope.IsEmpty.ShouldBeTrue();
    }
}
