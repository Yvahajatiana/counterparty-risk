using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.Domain.Contracts.Results;
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
    private const string GenericMessage = "The value of this field is invalid.";

    public async Task<Result<TResult>> Handle(TRequest request, RequestContinuation<TResult> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        var failures = new List<FieldError>();
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            // ErrorMessage is deliberately not used: it can embed the attempted value ({PropertyValue}),
            // which may be sensitive. Clients map the stable error code to a localised message.
            failures.AddRange(validation.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorCode, GenericMessage)));
        }

        if (failures.Count > 0)
        {
            return Result.Failure<TResult>(new ValidationError(failures));
        }

        return await continuation().ConfigureAwait(false);
    }
}
