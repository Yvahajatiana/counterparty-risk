using CounterpartyRisk.SharedKernel.Results;

namespace CounterpartyRisk.SharedKernel.Application.Messaging;

/// <summary>
/// Continuation of the pipeline: the next behavior, or the handler itself.
/// </summary>
public delegate Task<Result<TResult>> RequestContinuation<TResult>();

/// <summary>
/// Cross-cutting step wrapped around every handler. Behaviors run in registration order
/// (first registered is outermost): Logging, Validation, Authorization (#9), Audit (#10), handler.
/// </summary>
/// <typeparam name="TRequest">Command or query type.</typeparam>
/// <typeparam name="TResult">Value returned on success.</typeparam>
public interface IPipelineBehavior<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<Result<TResult>> Handle(TRequest request, RequestContinuation<TResult> continuation, CancellationToken cancellationToken);
}
