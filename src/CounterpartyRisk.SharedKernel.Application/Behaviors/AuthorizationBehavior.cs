using CounterpartyRisk.Domain.Contracts.Results;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Application.Security;
using Microsoft.Extensions.Logging;

namespace CounterpartyRisk.SharedKernel.Application.Behaviors;

/// <summary>
/// ABAC check of every dispatched request, right after logging and before validation (Lead decision on #9):
/// an unauthorized caller always gets 403, never a field error that could reveal data.
/// The decision depends only on the capability and the scope, never on the validated content.
/// <list type="number">
/// <item>anonymous caller: Unauthorized;</item>
/// <item>invalid principal (incompatible roles, malformed or front-office wildcard scope): Forbidden;</item>
/// <item>request without <see cref="RequiresCapabilityAttribute"/>: Forbidden (deny by default);</item>
/// <item>missing capability or missing MFA level: Forbidden;</item>
/// <item>empty scope (except audit capabilities), or governance without a global scope: Forbidden, before any SQL;</item>
/// <item><see cref="IScopedQuery"/> filter outside the scope: Forbidden; otherwise the effective scope is published
/// to <see cref="IRequestScope"/> and <see cref="ILegalEntityScope"/> (RLS, #11).</item>
/// </list>
/// </summary>
internal sealed partial class AuthorizationBehavior<TRequest, TResult>(
    ICurrentUser currentUser,
    ILegalEntityScope legalEntityScope,
    ILogger<AuthorizationBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<Result<TResult>> Handle(TRequest request, RequestContinuation<TResult> continuation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        var decision = Authorize(request, currentUser.User);
        if (decision.IsFailure)
        {
            // Only the request type and the stable error code are logged (no subject, no scope values).
            LogDenied(typeof(TRequest).Name, decision.Error.Code);
            return Result.Failure<TResult>(decision.Error);
        }

        legalEntityScope.Apply(decision.Value);
        return await continuation().ConfigureAwait(false);
    }

    private static Result<UserScope> Authorize(TRequest request, AuthenticatedUser? user)
    {
        var required = RequestAuthorization.RequiredCapabilities(typeof(TRequest));
        if (AccessRules.Check(user, required) is { } error)
        {
            return error;
        }

        var scope = user!.Scope;
        if (scope.IsEmpty)
        {
            // Only scope-free capabilities (audit trail, aggregated data quality view of the auditor)
            // reach this point; the handler then reads no scoped business data.
            return scope;
        }

        return request is IScopedQuery scoped
            ? scope.Narrow(scoped.LegalEntities, scoped.BusinessLines)
            : scope;
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Warning, Message = "{RequestName} denied with {ErrorCode}")]
    private partial void LogDenied(string requestName, string errorCode);
}
