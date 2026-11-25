using System.Collections.Frozen;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CounterpartyRisk.Domain.Contracts.ReferenceData;

namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>Multi-factor settings of the host (SEC-083, SEC-122).</summary>
/// <param name="Required">False only in Development, where local test users may skip MFA.</param>
/// <param name="AcceptedAcrValues"><c>acr</c> values that prove a multi-factor authentication.</param>
public sealed record MultiFactorPolicy(bool Required, IReadOnlySet<string> AcceptedAcrValues)
{
    /// <summary>Fail-closed default: MFA required and no accepted value until configured.</summary>
    public static MultiFactorPolicy Strict { get; } = new(true, FrozenSet<string>.Empty);
}

/// <summary>
/// Builds the <see cref="AuthenticatedUser"/> from the claims of an access token already validated
/// by the host (signature, issuer, audience, lifetime). Pure function, unit tested.
/// </summary>
public static partial class AuthenticatedUserFactory
{
    /// <summary>Upper-case code, 2 to 32 characters (e.g. MARKETS, FIN_TREASURY).</summary>
    [GeneratedRegex("^[A-Z][A-Z0-9_-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex BusinessLineCode();

    public static AuthenticatedUser? Create(ClaimsPrincipal? principal, MultiFactorPolicy multiFactor)
    {
        ArgumentNullException.ThrowIfNull(multiFactor);
        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var issuer = Single(principal, CcrClaimTypes.Issuer);
        var subject = Single(principal, CcrClaimTypes.Subject);

        // Unknown roles grant nothing and are dropped.
        var roles = Values(principal, CcrClaimTypes.Roles)
            .Where(r => CcrRoles.All.Contains(r, StringComparer.Ordinal))
            .ToFrozenSet(StringComparer.Ordinal);

        var legalEntities = Values(principal, CcrClaimTypes.LegalEntities).ToList();
        var businessLines = Values(principal, CcrClaimTypes.BusinessLines).ToList();
        var allLegalEntities = legalEntities.Contains(CcrClaimTypes.Wildcard, StringComparer.Ordinal);
        var allBusinessLines = businessLines.Contains(CcrClaimTypes.Wildcard, StringComparer.Ordinal);

        var fault = PrincipalFault.None;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            fault = PrincipalFault.MissingIdentity;
        }
        else if (Policies.FindIncompatiblePair(roles) is not null)
        {
            fault = PrincipalFault.IncompatibleRoles;
        }
        else if (!legalEntities.All(IsValidLegalEntity) || !businessLines.All(IsValidBusinessLine))
        {
            fault = PrincipalFault.InvalidScopeClaim;
        }
        else if (roles.Contains(CcrRoles.FrontOffice) && (allLegalEntities || allBusinessLines))
        {
            fault = PrincipalFault.FrontOfficeWildcardScope;
        }

        var scope = fault == PrincipalFault.None
            ? UserScope.Create(legalEntities, allLegalEntities, businessLines, allBusinessLines)
            : UserScope.Empty;

        var acr = Single(principal, CcrClaimTypes.AuthenticationContext);
        var mfa = !multiFactor.Required || (acr is not null && multiFactor.AcceptedAcrValues.Contains(acr));

        return new AuthenticatedUser(
            issuer ?? string.Empty,
            subject ?? string.Empty,
            roles,
            fault == PrincipalFault.None ? Policies.CapabilitiesFor(roles) : FrozenSet<Capability>.Empty,
            scope,
            mfa,
            fault);
    }

    private static bool IsValidLegalEntity(string value) =>
        value == CcrClaimTypes.Wildcard || Lei.Create(value).IsSuccess;

    private static bool IsValidBusinessLine(string value) =>
        value == CcrClaimTypes.Wildcard || BusinessLineCode().IsMatch(value);

    private static IEnumerable<string> Values(ClaimsPrincipal principal, string type) =>
        principal.FindAll(type).Select(c => c.Value).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal);

    /// <summary>Single-valued claim; a duplicated identity claim is treated as missing (fail closed).</summary>
    private static string? Single(ClaimsPrincipal principal, string type)
    {
        var values = principal.FindAll(type).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : null;
    }
}
