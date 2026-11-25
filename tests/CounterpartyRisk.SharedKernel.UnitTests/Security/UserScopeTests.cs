using CounterpartyRisk.SharedKernel.Application.Security;
using static CounterpartyRisk.SharedKernel.UnitTests.Security.TestUsers;

namespace CounterpartyRisk.SharedKernel.UnitTests.Security;

public class UserScopeTests
{
    private static readonly UserScope Fr = UserScope.Create([ParentFr], false, [Markets], false);

    [Fact]
    public void Missing_filter_is_replaced_by_the_user_scope()
    {
        var effective = Fr.Narrow(null, []).Value;

        effective.LegalEntities.ShouldBe([ParentFr]);
        effective.BusinessLines.ShouldBe([Markets]);
    }

    [Fact]
    public void Filter_inside_the_scope_is_kept()
    {
        var global = UserScope.Create([], true, [], true);

        var effective = global.Narrow([SubsidiaryDe], [Financing]).Value;

        effective.AllLegalEntities.ShouldBeFalse();
        effective.LegalEntities.ShouldBe([SubsidiaryDe]);
        effective.BusinessLines.ShouldBe([Financing]);
        effective.RequiresAllocatedMeasures.ShouldBeTrue();
    }

    [Fact]
    public void Should_Return403_When_User_Requests_Other_LegalEntity()
    {
        var result = Fr.Narrow([SubsidiaryDe], null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AuthorizationErrors.OutOfScope);
    }

    [Fact]
    public void Filter_mixing_own_and_foreign_entities_is_refused_not_silently_filtered()
    {
        Fr.Narrow([ParentFr, SubsidiaryDe], null).Error.ShouldBe(AuthorizationErrors.OutOfScope);
    }

    [Fact]
    public void Foreign_business_line_is_refused()
    {
        Fr.Narrow(null, [Financing]).Error.ShouldBe(AuthorizationErrors.OutOfScope);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_client_can_never_request_everything(string value)
    {
        UserScope.Create([], true, [], true).Narrow([value], null).Error.ShouldBe(AuthorizationErrors.OutOfScope);
        UserScope.Create([], true, [], true).Narrow(null, [value]).Error.ShouldBe(AuthorizationErrors.OutOfScope);
    }

    [Fact]
    public void Comparison_is_exact()
    {
        Fr.Narrow([ParentFr.ToLowerInvariant()], null).Error.ShouldBe(AuthorizationErrors.OutOfScope);
    }

    [Fact]
    public void Empty_scope_refuses_every_narrowing()
    {
        UserScope.Empty.Narrow(null, null).Error.ShouldBe(AuthorizationErrors.EmptyScope);
        UserScope.Create([ParentFr], false, [], false).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Ownership_check_of_a_resource()
    {
        Fr.Covers(ParentFr, Markets).ShouldBeTrue();
        Fr.Covers(SubsidiaryDe, Markets).ShouldBeFalse();
        Fr.Covers(ParentFr, Financing).ShouldBeFalse();
        UserScope.Create([SubsidiaryDe], false, [], true).Covers(SubsidiaryDe, Financing).ShouldBeTrue();
    }

    [Fact]
    public void Wildcard_scope_ignores_explicit_values()
    {
        var scope = UserScope.Create([ParentFr], true, [Markets], true);

        scope.LegalEntities.ShouldBeEmpty();
        scope.BusinessLines.ShouldBeEmpty();
        scope.CoversLegalEntity(SubsidiaryDe).ShouldBeTrue();
    }
}
