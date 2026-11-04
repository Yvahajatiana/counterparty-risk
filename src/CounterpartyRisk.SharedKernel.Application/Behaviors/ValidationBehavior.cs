using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Results;
using FluentValidation;

namespace CounterpartyRisk.SharedKernel.Application.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for the request and short-circuits
/// with a <see cref="ValidationError"/> before the handler is reached.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<Result<TResult>> Handle(TRequest request, RequestContinuation<TResult> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        var failures = new List<FieldError>();
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            // The attempted value is intentionally dropped: it may be sensitive.
            failures.AddRange(validation.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorCode, e.ErrorMessage)));
        }

        if (failures.Count > 0)
        {
            return Result.Failure<TResult>(new ValidationError(failures));
        }

        return await continuation().ConfigureAwait(false);
    }
}
