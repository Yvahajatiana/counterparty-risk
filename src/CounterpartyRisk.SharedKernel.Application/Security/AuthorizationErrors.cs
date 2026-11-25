using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>
/// Errors returned by the authorization layer. Messages are generic: they never say which legal
/// entity, business line or resource exists.
/// </summary>
public static class AuthorizationErrors
{
    public static Error Unauthenticated { get; } =
        Error.Unauthorized("authorization.unauthenticated", "Authentication is required.");

    /// <summary>The capability is not granted, or the request declares no authorization (deny by default).</summary>
    public static Error Forbidden { get; } =
        Error.Forbidden("authorization.forbidden", "You are not allowed to perform this operation.");

    /// <summary>The token carries an incompatible role pair or an invalid scope (fail closed).</summary>
    public static Error InvalidPrincipal { get; } =
        Error.Forbidden("authorization.invalid_principal", "Your access rights are inconsistent. Contact your administrator.");

    public static Error EmptyScope { get; } =
        Error.Forbidden("authorization.empty_scope", "No data scope is assigned to your account.");

    /// <summary>A requested legal entity or business line filter is outside the user scope.</summary>
    public static Error OutOfScope { get; } =
        Error.Forbidden("authorization.out_of_scope", "The requested scope exceeds your access rights.");

    public static Error MultiFactorRequired { get; } =
        Error.Forbidden("authorization.mfa_required", "This operation requires multi-factor authentication.");

    /// <summary>
    /// Resource addressed by identifier that is unknown or outside the scope: one identical answer,
    /// so that the existence of a foreign resource is never revealed (API1:2023 BOLA).
    /// </summary>
    public static Error NotFound { get; } =
        Error.NotFound("resource.not_found", "The resource was not found.");
}
