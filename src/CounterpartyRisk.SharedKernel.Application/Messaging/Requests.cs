namespace CounterpartyRisk.SharedKernel.Application.Messaging;

/// <summary>
/// Common marker for commands and queries dispatched through <see cref="IDispatcher"/>.
/// </summary>
/// <typeparam name="TResult">Value returned on success.</typeparam>
#pragma warning disable CA1040 // Marker interfaces are intended: they carry the result type for the dispatcher
public interface IRequest<TResult>
{
}

/// <summary>
/// Write-side request that changes state through an aggregate. Returns no read model (ADR-001).
/// </summary>
public interface ICommand<TResult> : IRequest<TResult>
{
}

/// <summary>
/// Command without a meaningful return value.
/// </summary>
public interface ICommand : ICommand<Unit>
{
}

/// <summary>
/// Read-side request served by a read model (no tracking, no side effect).
/// </summary>
public interface IQuery<TResult> : IRequest<TResult>
{
}
#pragma warning restore CA1040

/// <summary>
/// Empty result of a command that returns nothing.
/// </summary>
public readonly record struct Unit
{
    public static Unit Value { get; }
}
