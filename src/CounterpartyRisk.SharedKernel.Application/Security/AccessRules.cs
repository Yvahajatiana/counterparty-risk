using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Function-level rules shared by the API endpoint policies and <c>AuthorizationBehavior</c>, so that
/// both layers (defence in depth) take exactly the same decision.
/// </summary>
public static class AccessRules
{
    /// <summary>Null when every capability is granted, otherwise the error to return.</summary>
    public static Error? Check(AuthenticatedUser? user, IEnumerable<Capability> required)
    {
        ArgumentNullException.ThrowIfNull(required);
        if (user is null)
        {
            return AuthorizationErrors.Unauthenticated;
        }

        if (!user.IsValid)
        {
            return AuthorizationErrors.InvalidPrincipal;
        }

        var capabilities = required.ToList();
        if (capabilities.Count == 0)
        {
            // Deny by default: a request that declares nothing is refused.
            return AuthorizationErrors.Forbidden;
        }

        foreach (var capability in capabilities)
        {
            if (!user.Has(capability))
            {
                return AuthorizationErrors.Forbidden;
            }

            if (Policies.RequiresMultiFactor(capability) && !user.MultiFactor)
            {
                return AuthorizationErrors.MultiFactorRequired;
            }

            if (Policies.RequiresAllLegalEntities(capability) && !user.Scope.AllLegalEntities)
            {
                return AuthorizationErrors.OutOfScope;
            }
        }

        if (user.Scope.IsEmpty && capabilities.Any(Policies.RequiresDataScope))
        {
            return AuthorizationErrors.EmptyScope;
        }

        return null;
    }
}
