using CounterpartyRisk.Domain.Contracts.Results;
using CounterpartyRisk.SharedKernel.Application;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Application.Security;
using CounterpartyRisk.SharedKernel.UnitTests.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static CounterpartyRisk.SharedKernel.UnitTests.Security.TestUsers;

namespace CounterpartyRisk.SharedKernel.UnitTests.Security;

public class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Anonymous_caller_is_unauthenticated()
    {
        var (result, recorder) = await Run(null, new ExposureQuery(null, null));

        result.Error.ShouldBe(AuthorizationErrors.Unauthenticated);
        recorder.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_without_declared_capability_is_denied_by_default()
    {
        var (result, recorder) = await Run(Direction(), new UndeclaredQuery());

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
        recorder.Calls.ShouldNotContain("handler");
    }

    [Fact]
    public async Task Undefined_capability_value_is_denied_not_ignored()
    {
        // [RequiresCapability((Capability)999, Capability.ReadExposure)]: the unknown value must not be filtered out.
        var (result, recorder) = await Run(Direction(), new UndefinedCapabilityQuery());

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
        recorder.Calls.ShouldBeEmpty();
        AccessRules.Check(Direction(), [(Capability)999]).ShouldBe(AuthorizationErrors.Forbidden);
    }

    [Fact]
    public async Task Should_Return403_When_FrontOffice_Calls_Capital_Endpoint()
    {
        var (result, _) = await Run(FrontOfficeFr(), new CapitalQuery());

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
    }

    [Fact]
    public async Task Should_Return403_When_FrontOffice_Exports()
    {
        var (result, _) = await Run(FrontOfficeFr(), new ExportCapitalCommand());

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
    }

    [Fact]
    public async Task Should_Return403_When_Exporting_W6_Without_ReadCapital()
    {
        // Rule R5: Export AND the read capability of the exported widget. No role of the matrix holds Export
        // without ReadCapital, so the principal is built by hand to prove the capabilities are cumulative.
        var exportOnly = RiskManagerFr() with { Capabilities = new HashSet<Capability> { Capability.Export } };

        var (result, _) = await Run(exportOnly, new ExportCapitalCommand());

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
        AccessRules.Check(RiskManagerFr(), [Capability.Export, Capability.ReadCapital]).ShouldBeNull();
    }

    [Fact]
    public async Task Unauthorized_caller_gets_403_never_a_field_error()
    {
        // The command is invalid AND the caller lacks the capability: authorization runs first.
        var (result, recorder) = await Run(FrontOfficeFr(), new GuardedCommand(-1));

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
        result.Error.ShouldNotBeOfType<ValidationError>();
        recorder.Calls.ShouldNotContain("validator");
    }

    [Fact]
    public async Task Authorized_caller_then_gets_the_field_error()
    {
        var (result, recorder) = await Run(RiskManagerFr(), new GuardedCommand(-1));

        result.Error.ShouldBeOfType<ValidationError>();
        recorder.Calls.ShouldContain("validator");
    }

    [Fact]
    public async Task Should_Return403_And_Issue_No_Sql_When_Scope_Is_Empty()
    {
        var (result, recorder) = await Run(RiskManagerWithoutEntity(), new ExposureQuery(null, null));

        result.Error.ShouldBe(AuthorizationErrors.EmptyScope);
        // The handler (the only code that would query the database) is never created.
        recorder.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Return403_When_User_Requests_Other_LegalEntity()
    {
        var (result, recorder) = await Run(RiskManagerFr(), new ExposureQuery([SubsidiaryDe], null));

        result.Error.ShouldBe(AuthorizationErrors.OutOfScope);
        recorder.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_filter_is_replaced_by_the_user_scope_and_published_to_the_handler()
    {
        var (result, _) = await Run(RiskManagerFr(), new ExposureQuery(null, null));

        result.Value.LegalEntities.ShouldBe([ParentFr]);
        result.Value.BusinessLines.ShouldBe([Markets]);
    }

    [Fact]
    public async Task Filter_inside_the_scope_is_applied()
    {
        var (result, _) = await Run(Direction(), new ExposureQuery([SubsidiaryDe], [Financing]));

        result.Value.LegalEntities.ShouldBe([SubsidiaryDe]);
        result.Value.AllLegalEntities.ShouldBeFalse();
    }

    [Fact]
    public async Task Invalid_principal_is_refused_before_any_capability()
    {
        var user = Build("x", [CcrRoles.Auditor, CcrRoles.Direction], ["*"], ["*"]);

        var (result, _) = await Run(user, new AuditQuery());

        result.Error.ShouldBe(AuthorizationErrors.InvalidPrincipal);
    }

    [Fact]
    public async Task Auditor_reads_the_audit_trail_with_an_empty_scope()
    {
        var (result, _) = await Run(Auditor(), new AuditQuery());

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task Auditor_gets_403_on_business_data()
    {
        var (result, _) = await Run(Auditor(), new ExposureQuery(null, null));

        result.Error.ShouldBe(AuthorizationErrors.Forbidden);
    }

    [Fact]
    public async Task Sensitive_capability_requires_multi_factor()
    {
        var (withoutMfa, _) = await Run(LimitAdminFr("pwd", MfaRequired), new ManageThresholdsCommand());
        var (withMfa, _) = await Run(LimitAdminFr("mfa", MfaRequired), new ManageThresholdsCommand());

        withoutMfa.Error.ShouldBe(AuthorizationErrors.MultiFactorRequired);
        withMfa.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Return403_When_Governance_Scope_Does_Not_Cover_All_Entities()
    {
        var (scoped, _) = await Run(RiskManagerFr(), new PublishMethodVersionCommand());
        var global = Build("rm-global", [CcrRoles.RiskManager], ["*"], ["*"]);
        var (allowed, _) = await Run(global, new PublishMethodVersionCommand());

        scoped.Error.ShouldBe(AuthorizationErrors.OutOfScope);
        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Legal_entity_scope_for_rls_is_set_before_the_handler()
    {
        using var provider = DispatcherTests.BuildProvider(user: FinanceDe());
        using var scope = provider.CreateScope();
        var rls = scope.ServiceProvider.GetRequiredService<ILegalEntityScope>();
        rls.IsSet.ShouldBeFalse();

        await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(new ExposureQuery(null, null), TestContext.Current.CancellationToken);

        rls.IsSet.ShouldBeTrue();
        rls.AllLegalEntities.ShouldBeFalse();
        rls.LegalEntityIds.ShouldBe([SubsidiaryDe]);
    }

    [Fact]
    public void Request_scope_fails_closed_outside_an_authorized_request()
    {
        using var provider = DispatcherTests.BuildProvider();
        using var scope = provider.CreateScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IRequestScope>().Current);
        scope.ServiceProvider.GetRequiredService<ILegalEntityScope>().LegalEntityIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Denial_is_logged_with_the_error_code_only()
    {
        using var provider = DispatcherTests.BuildProvider(user: FrontOfficeFr());
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(new CapitalQuery(), TestContext.Current.CancellationToken);

        var entry = provider.GetRequiredService<LogRecords>().Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("denied", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        entry.Message.ShouldBe("CapitalQuery denied with authorization.forbidden");
    }

    [Fact]
    public async Task Missing_current_user_fails_at_dispatch()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCqrs(typeof(AuthorizationBehaviorTests).Assembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(new CapitalQuery(), CancellationToken.None));
    }

    private static async Task<(Result<TResult> Result, CallRecorder Recorder)> Run<TResult>(AuthenticatedUser? user, IQuery<TResult> query)
    {
        using var provider = BuildFor(user);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(query, TestContext.Current.CancellationToken);
        return (result, scope.ServiceProvider.GetRequiredService<CallRecorder>());
    }

    private static async Task<(Result<TResult> Result, CallRecorder Recorder)> Run<TResult>(AuthenticatedUser? user, ICommand<TResult> command)
    {
        using var provider = BuildFor(user);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, TestContext.Current.CancellationToken);
        return (result, scope.ServiceProvider.GetRequiredService<CallRecorder>());
    }

    private static ServiceProvider BuildFor(AuthenticatedUser? user) =>
        user is null
            ? DispatcherTests.BuildProvider(services => services.AddSingleton<ICurrentUser>(new FixedCurrentUser(null)))
            : DispatcherTests.BuildProvider(user: user);
}

[RequiresCapability(Capability.ReadExposure)]
public sealed record ExposureQuery(IReadOnlyCollection<string>? LegalEntities, IReadOnlyCollection<string>? BusinessLines)
    : IQuery<UserScope>, IScopedQuery;

public sealed record UndeclaredQuery : IQuery<int>;

[RequiresCapability((Capability)999, Capability.ReadExposure)]
public sealed record UndefinedCapabilityQuery : IQuery<int>;

[RequiresCapability(Capability.ReadCapital)]
public sealed record CapitalQuery : IQuery<int>;

[RequiresCapability(Capability.Export)]
[RequiresCapability(Capability.ReadCapital)]
public sealed record ExportCapitalCommand : ICommand<int>;

[RequiresCapability(Capability.ReadCapital)]
public sealed record GuardedCommand(int Value) : ICommand<int>;

[RequiresCapability(Capability.ReadAudit)]
public sealed record AuditQuery : IQuery<UserScope>;

[RequiresCapability(Capability.ManageLimitThresholds)]
public sealed record ManageThresholdsCommand : ICommand<int>;

[RequiresCapability(Capability.ManageMethodVersions)]
public sealed record PublishMethodVersionCommand : ICommand<int>;

internal sealed class ExposureQueryHandler(IRequestScope scope, CallRecorder recorder) : IQueryHandler<ExposureQuery, UserScope>
{
    public Task<Result<UserScope>> Handle(ExposureQuery query, CancellationToken cancellationToken)
    {
        recorder.Calls.Add("handler");
        return Task.FromResult(Result.Success(scope.Current));
    }
}

internal sealed class UndeclaredQueryHandler(CallRecorder recorder) : IQueryHandler<UndeclaredQuery, int>
{
    public Task<Result<int>> Handle(UndeclaredQuery query, CancellationToken cancellationToken)
    {
        recorder.Calls.Add("handler");
        return Task.FromResult(Result.Success(1));
    }
}

internal sealed class UndefinedCapabilityQueryHandler(CallRecorder recorder) : IQueryHandler<UndefinedCapabilityQuery, int>
{
    public Task<Result<int>> Handle(UndefinedCapabilityQuery query, CancellationToken cancellationToken)
    {
        recorder.Calls.Add("handler");
        return Task.FromResult(Result.Success(1));
    }
}

internal sealed class CapitalQueryHandler : IQueryHandler<CapitalQuery, int>
{
    public Task<Result<int>> Handle(CapitalQuery query, CancellationToken cancellationToken) => Task.FromResult(Result.Success(1));
}

internal sealed class ExportCapitalCommandHandler : ICommandHandler<ExportCapitalCommand, int>
{
    public Task<Result<int>> Handle(ExportCapitalCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success(1));
}

internal sealed class GuardedCommandHandler : ICommandHandler<GuardedCommand, int>
{
    public Task<Result<int>> Handle(GuardedCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success(1));
}

internal sealed class GuardedCommandValidator : AbstractValidator<GuardedCommand>
{
    public GuardedCommandValidator(CallRecorder recorder)
    {
        RuleFor(c => c.Value)
            .Must(value =>
            {
                recorder.Calls.Add("validator");
                return value >= 0;
            })
            .WithErrorCode("NotNegative");
    }
}

internal sealed class AuditQueryHandler(IRequestScope scope) : IQueryHandler<AuditQuery, UserScope>
{
    public Task<Result<UserScope>> Handle(AuditQuery query, CancellationToken cancellationToken) => Task.FromResult(Result.Success(scope.Current));
}

internal sealed class ManageThresholdsCommandHandler : ICommandHandler<ManageThresholdsCommand, int>
{
    public Task<Result<int>> Handle(ManageThresholdsCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success(1));
}

internal sealed class PublishMethodVersionCommandHandler : ICommandHandler<PublishMethodVersionCommand, int>
{
    public Task<Result<int>> Handle(PublishMethodVersionCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success(1));
}
