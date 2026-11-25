using CounterpartyRisk.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CounterpartyRisk.Api.Security;

/// <summary>
/// User of the current HTTP request, built once per request from the validated access token.
/// Used instead of an <c>IClaimsTransformation</c> that would add capability claims: a transformation can
/// run several times per request and any claim it adds could be confused with a claim of the token.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, IOptions<ApiAuthenticationOptions> options) : ICurrentUser
{
    private AuthenticatedUser? _user;
    private bool _resolved;

    public AuthenticatedUser? User
    {
        get
        {
            if (!_resolved)
            {
                _user = AuthenticatedUserFactory.Create(accessor.HttpContext?.User, options.Value.ToMultiFactorPolicy());
                _resolved = true;
            }

            return _user;
        }
    }
}

/// <summary>Token identity and scope claims are consistent (no incompatible roles, valid scope): 403 otherwise.</summary>
internal sealed class ValidPrincipalRequirement : IAuthorizationRequirement;

/// <summary>Capability of the matrix, with its MFA and scope conditions (<see cref="AccessRules"/>).</summary>
internal sealed class CapabilityRequirement(Capability capability) : IAuthorizationRequirement
{
    public Capability Capability { get; } = capability;
}

/// <summary>
/// Evaluates both requirements against <see cref="ICurrentUser"/>, i.e. the same decision as
/// <c>AuthorizationBehavior</c> in the Application layer.
/// </summary>
internal sealed class CapabilityAuthorizationHandler(IOptions<ApiAuthenticationOptions> options) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Built from the principal being authorized (set by the policy evaluation), not from a cached value.
        var user = AuthenticatedUserFactory.Create(context.User, options.Value.ToMultiFactorPolicy());

        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case ValidPrincipalRequirement when user is { IsValid: true }:
                    context.Succeed(requirement);
                    break;
                case ValidPrincipalRequirement:
                    context.Fail();
                    break;
                case CapabilityRequirement capability when AccessRules.Check(user, [capability.Capability]) is null:
                    context.Succeed(requirement);
                    break;
                case CapabilityRequirement:
                    context.Fail();
                    break;
            }
        }

        return Task.CompletedTask;
    }
}
