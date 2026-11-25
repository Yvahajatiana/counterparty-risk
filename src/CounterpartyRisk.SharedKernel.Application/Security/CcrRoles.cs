namespace CounterpartyRisk.SharedKernel.Application.Security;

/// <summary>Realm roles of the Keycloak realm <c>ccr</c> (src/CounterpartyRisk.AppHost/Realms).</summary>
public static class CcrRoles
{
    public const string RiskManager = "ccr-risk-manager";
    public const string FrontOffice = "ccr-front-office";
    public const string Direction = "ccr-direction";
    public const string Finance = "ccr-finance";
    public const string LimitAdmin = "ccr-limit-admin";
    public const string Auditor = "ccr-auditor";

    /// <summary>Every role known to the application; any other role in a token is ignored.</summary>
    public static IReadOnlyList<string> All { get; } = [RiskManager, FrontOffice, Direction, Finance, LimitAdmin, Auditor];

    /// <summary>Business roles (every role except the auditor).</summary>
    public static IReadOnlyList<string> Business { get; } = [RiskManager, FrontOffice, Direction, Finance, LimitAdmin];
}

/// <summary>Claim types read from the access token issued by Keycloak for the audience <c>ccr-api</c>.</summary>
public static class CcrClaimTypes
{
    public const string Subject = "sub";
    public const string Issuer = "iss";
    public const string Roles = "roles";
    public const string LegalEntities = "ccr_legal_entities";
    public const string BusinessLines = "ccr_business_lines";
    public const string AuthenticationContext = "acr";
    public const string Name = "preferred_username";

    /// <summary>Value of a scope claim granting every legal entity or every business line.</summary>
    public const string Wildcard = "*";
}
