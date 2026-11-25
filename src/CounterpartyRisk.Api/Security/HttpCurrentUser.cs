using CounterpartyRisk.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace CounterpartyRisk.Api.Security;

/// <summary>
/// User of the current HTTP request, built once per request from the validated access token.
/// Used instead of an <c>IClaimsTransformation</c> that would add capability claims: a transformation can
/// run several times per request and any claim it adds could be confused with a claim of the token.
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, MultiFactorPolicy multiFactor) : ICurrentUser
{
    private ClaimsPrincipal? _principal;
    private AuthenticatedUser? _user;

    public AuthenticatedUser? User => Resolve(accessor.HttpContext?.User);

    /// <summary>
    /// Builds the user once per principal: the authorization handler and the Application layer share it.
    /// A different principal (re-authentication during the request) is resolved again, never reused.
    /// </summary>
    public AuthenticatedUser? Resolve(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        if (!ReferenceEquals(principal, _principal))
        {
            _user = AuthenticatedUserFactory.Create(principal, multiFactor);
            _principal = principal;
        }

        return _user;
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
/// Evaluates both requirements on the authorized principal, with the same rules (<see cref="AccessRules"/>) as
/// <c>AuthorizationBehavior</c> in the Application layer.
/// </summary>
internal sealed class CapabilityAuthorizationHandler(IHttpContextAccessor accessor, MultiFactorPolicy multiFactor) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // The principal being authorized, resolved through the per-request user when one is available.
        var user = accessor.HttpContext?.RequestServices.GetService<HttpCurrentUser>() is { } current
            ? current.Resolve(context.User)
            : AuthenticatedUserFactory.Create(context.User, multiFactor);

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
