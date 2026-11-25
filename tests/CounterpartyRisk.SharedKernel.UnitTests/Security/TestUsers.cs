using System.Collections.Frozen;
using System.Security.Claims;
using CounterpartyRisk.SharedKernel.Application.Security;

namespace CounterpartyRisk.SharedKernel.UnitTests.Security;

/// <summary>Synthetic principals mirroring the local test users of the realm (Realms/ccr-users-0.json).</summary>
internal static class TestUsers
{
    public const string Issuer = "https://idp.test/realms/ccr";
    public const string ParentFr = "CCRSYNTHPARENTFR0057";
    public const string SubsidiaryDe = "CCRSYNTHSUBSIDDE0022";
    public const string Markets = "MARKETS";
    public const string Financing = "FINANCING";

    public static MultiFactorPolicy NoMfa { get; } = new(false, FrozenSet<string>.Empty);

    public static MultiFactorPolicy MfaRequired { get; } = new(true, new[] { "mfa" }.ToFrozenSet());

    public static AuthenticatedUser RiskManagerFr() =>
        Build(subject: "rm", roles: [CcrRoles.RiskManager], legalEntities: [ParentFr], businessLines: [Markets])!;

    public static AuthenticatedUser FrontOfficeFr() =>
        Build(subject: "fo", roles: [CcrRoles.FrontOffice], legalEntities: [ParentFr], businessLines: [Markets])!;

    public static AuthenticatedUser Direction() =>
        Build(subject: "dir", roles: [CcrRoles.Direction], legalEntities: ["*"], businessLines: ["*"])!;

    public static AuthenticatedUser FinanceDe() =>
        Build(subject: "fin", roles: [CcrRoles.Finance], legalEntities: [SubsidiaryDe], businessLines: ["*"])!;

    public static AuthenticatedUser RiskManagerWithoutEntity() =>
        Build(subject: "rm0", roles: [CcrRoles.RiskManager], legalEntities: [], businessLines: [Markets])!;

    public static AuthenticatedUser Auditor() =>
        Build(subject: "aud", roles: [CcrRoles.Auditor], legalEntities: [], businessLines: [])!;

    public static AuthenticatedUser LimitAdminFr(string? acr = null, MultiFactorPolicy? mfa = null) =>
        Build(subject: "lim", roles: [CcrRoles.LimitAdmin], legalEntities: [ParentFr], businessLines: ["*"], acr: acr, mfa: mfa)!;

    public static ClaimsPrincipal Principal(
        string? subject,
        IEnumerable<string> roles,
        IEnumerable<string> legalEntities,
        IEnumerable<string> businessLines,
        string? acr = null,
        string? issuer = Issuer,
        bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (issuer is not null)
        {
            claims.Add(new Claim(CcrClaimTypes.Issuer, issuer));
        }

        if (subject is not null)
        {
            claims.Add(new Claim(CcrClaimTypes.Subject, subject));
        }

        if (acr is not null)
        {
            claims.Add(new Claim(CcrClaimTypes.AuthenticationContext, acr));
        }

        claims.AddRange(roles.Select(r => new Claim(CcrClaimTypes.Roles, r)));
        claims.AddRange(legalEntities.Select(l => new Claim(CcrClaimTypes.LegalEntities, l)));
        claims.AddRange(businessLines.Select(b => new Claim(CcrClaimTypes.BusinessLines, b)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Bearer" : null));
    }

    public static AuthenticatedUser? Build(
        string? subject,
        IEnumerable<string> roles,
        IEnumerable<string> legalEntities,
        IEnumerable<string> businessLines,
        string? acr = null,
        MultiFactorPolicy? mfa = null) =>
        AuthenticatedUserFactory.Create(Principal(subject, roles, legalEntities, businessLines, acr), mfa ?? NoMfa);
}

internal sealed class FixedCurrentUser(AuthenticatedUser? user) : ICurrentUser
{
    public AuthenticatedUser? User { get; } = user;
}
