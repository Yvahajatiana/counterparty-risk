using CounterpartyRisk.SharedKernel.Results;

namespace CounterpartyRisk.SharedKernel.Application.Messaging;

/// <summary>
/// Single entry point from the API, BFF and workers into the Application layer.
/// Every request dispatched here goes through the pipeline behaviors. Handlers stay reachable
/// from the DI container, so the guarantee relies on hosts never depending on the handler
/// interfaces: the architecture tests enforce this rule.
/// </summary>
public interface IDispatcher
{
    Task<Result<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<Result<TResult>> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
