using CounterpartyRisk.SharedKernel.Results;

namespace CounterpartyRisk.SharedKernel.Application.Messaging;

/// <summary>
/// Single entry point from the API, BFF and workers into the Application layer.
/// Every request goes through the pipeline behaviors, so authorization and audit
/// cannot be bypassed by calling a handler directly.
/// </summary>
public interface IDispatcher
{
    Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<Result<TResult>> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
