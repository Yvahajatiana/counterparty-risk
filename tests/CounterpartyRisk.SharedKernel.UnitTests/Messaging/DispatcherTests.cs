using CounterpartyRisk.SharedKernel.Application;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CounterpartyRisk.SharedKernel.UnitTests.Messaging;

public class DispatcherTests
{
    [Fact]
    public async Task Send_routes_command_to_its_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new DoubleCommand(21), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task Query_routes_query_to_its_handler()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Query(new EchoQuery("ping"), TestContext.Current.CancellationToken);

        result.Value.ShouldBe("ping");
    }

    [Fact]
    public async Task Command_without_result_returns_unit()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var result = await dispatcher.Send(new NoOpCommand(), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(Unit.Value);
    }

    [Fact]
    public async Task Pipeline_runs_validation_then_appended_behaviors_then_handler()
    {
        using var provider = BuildProvider(services =>
            services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RecordingBehavior<,>)));
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var recorder = scope.ServiceProvider.GetRequiredService<CallRecorder>();

        await dispatcher.Send(new DoubleCommand(1), TestContext.Current.CancellationToken);

        recorder.Calls.ShouldBe(["validator", "behavior:before", "handler", "behavior:after"]);
    }

    [Fact]
    public async Task Invalid_request_short_circuits_with_validation_error()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var recorder = scope.ServiceProvider.GetRequiredService<CallRecorder>();

        var result = await dispatcher.Send(new DoubleCommand(-1), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        var error = result.Error.ShouldBeOfType<ValidationError>();
        error.Errors.ShouldHaveSingleItem().Field.ShouldBe(nameof(DoubleCommand.Value));
        recorder.Calls.ShouldNotContain("handler");
    }

    [Fact]
    public async Task Missing_handler_fails_fast()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.Query(new OrphanQuery(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AddCqrs_registers_internal_handlers_once()
    {
        var services = new ServiceCollection();

        services.AddCqrs(typeof(DispatcherTests).Assembly);
        services.AddCqrs(typeof(DispatcherTests).Assembly);

        services.Count(d => d.ServiceType == typeof(ICommandHandler<DoubleCommand, int>)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IPipelineBehavior<,>)).ShouldBe(2);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<CallRecorder>();
        services.AddCqrs(typeof(DispatcherTests).Assembly);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

public sealed class CallRecorder
{
    public List<string> Calls { get; } = [];
}

public sealed record DoubleCommand(int Value) : ICommand<int>;

public sealed record EchoQuery(string Text) : IQuery<string>;

public sealed record NoOpCommand : ICommand;

public sealed record OrphanQuery : IQuery<int>;

internal sealed class DoubleCommandHandler(CallRecorder recorder) : ICommandHandler<DoubleCommand, int>
{
    public Task<Result<int>> Handle(DoubleCommand command, CancellationToken cancellationToken)
    {
        recorder.Calls.Add("handler");
        return Task.FromResult(Result.Success(command.Value * 2));
    }
}

internal sealed class EchoQueryHandler : IQueryHandler<EchoQuery, string>
{
    public Task<Result<string>> Handle(EchoQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(query.Text));
}

internal sealed class NoOpCommandHandler : ICommandHandler<NoOpCommand, Unit>
{
    public Task<Result<Unit>> Handle(NoOpCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(Unit.Value));
}

internal sealed class DoubleCommandValidator : AbstractValidator<DoubleCommand>
{
    public DoubleCommandValidator(CallRecorder recorder)
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

internal sealed class RecordingBehavior<TRequest, TResult>(CallRecorder recorder) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<Result<TResult>> Handle(TRequest request, RequestContinuation<TResult> continuation, CancellationToken cancellationToken)
    {
        recorder.Calls.Add("behavior:before");
        var result = await continuation();
        recorder.Calls.Add("behavior:after");
        return result;
    }
}
