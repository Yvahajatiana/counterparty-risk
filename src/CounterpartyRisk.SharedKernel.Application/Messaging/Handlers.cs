using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.SharedKernel.Application.Messaging;

/// <summary>
/// Handles a command. Implementations must be <c>internal sealed</c> and named <c>*Handler</c>
/// (enforced by the architecture tests).
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a query. Implementations must be <c>internal sealed</c> and named <c>*Handler</c>
/// (enforced by the architecture tests).
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> Handle(TQuery query, CancellationToken cancellationToken);
}
