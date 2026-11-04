using System.Collections.Concurrent;
using CounterpartyRisk.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace CounterpartyRisk.SharedKernel.Application.Messaging;

/// <summary>
/// Minimal in-process dispatcher (replaces MediatR, now under a commercial licence).
/// Resolves the handler and the ordered pipeline behaviors from the current DI scope.
/// </summary>
internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    // Closed wrapper per request type, built once with reflection then reused.
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            command.GetType(),
            static (type, resultType) => Activator.CreateInstance(
                typeof(CommandHandlerWrapper<,>).MakeGenericType(type, resultType))!,
            typeof(TResult));

        return wrapper.Handle(command, serviceProvider, cancellationToken);
    }

    public Task<Result<TResult>> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            query.GetType(),
            static (type, resultType) => Activator.CreateInstance(
                typeof(QueryHandlerWrapper<,>).MakeGenericType(type, resultType))!,
            typeof(TResult));

        return wrapper.Handle(query, serviceProvider, cancellationToken);
    }
}

internal abstract class RequestHandlerWrapper<TResult>
{
    public abstract Task<Result<TResult>> Handle(IRequest<TResult> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);

    protected static Task<Result<TResult>> RunPipeline<TRequest>(
        TRequest request,
        RequestContinuation<TResult> handler,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        // Wrap from the last registered behavior to the first, so the first one runs outermost.
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResult>>().Reverse();
        var next = handler;
        foreach (var behavior in behaviors)
        {
            var continuation = next;
            next = () => behavior.Handle(request, continuation, cancellationToken);
        }

        return next();
    }
}

internal sealed class CommandHandlerWrapper<TCommand, TResult> : RequestHandlerWrapper<TResult>
    where TCommand : ICommand<TResult>
{
    public override Task<Result<TResult>> Handle(IRequest<TResult> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var command = (TCommand)request;
        var handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        return RunPipeline(command, () => handler.Handle(command, cancellationToken), serviceProvider, cancellationToken);
    }
}

internal sealed class QueryHandlerWrapper<TQuery, TResult> : RequestHandlerWrapper<TResult>
    where TQuery : IQuery<TResult>
{
    public override Task<Result<TResult>> Handle(IRequest<TResult> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var query = (TQuery)request;
        var handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return RunPipeline(query, () => handler.Handle(query, cancellationToken), serviceProvider, cancellationToken);
    }
}
