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
    // Closed wrapper built once with reflection, then reused. The key holds the operation and the result:
    // one request type may implement both ICommand and IQuery, possibly with different result types.
    private static readonly ConcurrentDictionary<WrapperKey, object> Wrappers = new();

    public Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = GetWrapper<TResult>(command.GetType(), typeof(CommandHandlerWrapper<,>));
        return wrapper.Handle(command, serviceProvider, cancellationToken);
    }

    public Task<Result<TResult>> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = GetWrapper<TResult>(query.GetType(), typeof(QueryHandlerWrapper<,>));
        return wrapper.Handle(query, serviceProvider, cancellationToken);
    }

    private static RequestHandlerWrapper<TResult> GetWrapper<TResult>(Type requestType, Type operation) =>
        (RequestHandlerWrapper<TResult>)Wrappers.GetOrAdd(
            new WrapperKey(requestType, operation, typeof(TResult)),
            static key => Activator.CreateInstance(key.Operation.MakeGenericType(key.RequestType, key.ResultType))!);

    private readonly record struct WrapperKey(Type RequestType, Type Operation, Type ResultType);
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

        // Resolved in the final continuation: behaviors run before the handler is instantiated.
        return RunPipeline(
            command,
            () => serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle(command, cancellationToken),
            serviceProvider,
            cancellationToken);
    }
}

internal sealed class QueryHandlerWrapper<TQuery, TResult> : RequestHandlerWrapper<TResult>
    where TQuery : IQuery<TResult>
{
    public override Task<Result<TResult>> Handle(IRequest<TResult> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var query = (TQuery)request;

        // Resolved in the final continuation: behaviors run before the handler is instantiated.
        return RunPipeline(
            query,
            () => serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().Handle(query, cancellationToken),
            serviceProvider,
            cancellationToken);
    }
}
