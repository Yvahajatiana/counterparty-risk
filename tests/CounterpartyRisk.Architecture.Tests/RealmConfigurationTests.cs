using System.Text.Json;
using CounterpartyRisk.Domain.Contracts.ReferenceData;
using CounterpartyRisk.SharedKernel.Application.Security;

namespace CounterpartyRisk.Architecture.Tests;

/// <summary>
/// The Keycloak realm is code (TM-TB2-04): its security settings are asserted here, so that a change in
/// the admin console exported back into the file cannot silently weaken them. Values verified against
/// Keycloak 26.8.1 when #9 was written.
/// </summary>
public sealed class RealmConfigurationTests : IDisposable
{
    private readonly JsonDocument _realm = Load("ccr-realm.json");
    private readonly JsonDocument _users = Load("ccr-users-0.json");

    private JsonElement Realm => _realm.RootElement;

    private JsonElement Client => Realm.GetProperty("clients").EnumerateArray()
        .Single(c => c.GetProperty("clientId").GetString() == "ccr-bff");

    public void Dispose()
    {
        _realm.Dispose();
        _users.Dispose();
    }

    [Fact]
    public void Realm_Should_Only_Enable_Code_Flow_With_Pkce()
    {
        var client = Client;
        client.GetProperty("publicClient").GetBoolean().ShouldBeFalse();
        client.GetProperty("standardFlowEnabled").GetBoolean().ShouldBeTrue();
        client.GetProperty("implicitFlowEnabled").GetBoolean().ShouldBeFalse();
        client.GetProperty("directAccessGrantsEnabled").GetBoolean().ShouldBeFalse();
        client.GetProperty("serviceAccountsEnabled").GetBoolean().ShouldBeFalse();

        var attributes = client.GetProperty("attributes");
        attributes.GetProperty("pkce.code.challenge.method").GetString().ShouldBe("S256");
        attributes.GetProperty("oauth2.device.authorization.grant.enabled").GetString().ShouldBe("false");
        attributes.GetProperty("oidc.ciba.grant.enabled").GetString().ShouldBe("false");
    }

    [Fact]
    public void Redirect_uris_are_exact()
    {
        var client = Client;
        var redirects = client.GetProperty("redirectUris").EnumerateArray().Select(e => e.GetString()!).ToList();

        redirects.ShouldBe(["${CCR_BFF_BASE_URL}/signin-oidc"]);
        redirects.ShouldAllBe(uri => !uri.Contains('*'));
        client.GetProperty("attributes").GetProperty("post.logout.redirect.uris").GetString().ShouldBe("${CCR_BFF_BASE_URL}/signout-callback-oidc");
        client.GetProperty("webOrigins").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void Realm_files_contain_no_secret()
    {
        Client.GetProperty("secret").GetString().ShouldBe("${CCR_BFF_CLIENT_SECRET}");
        Realm.TryGetProperty("users", out _).ShouldBeFalse("Users live in ccr-users-0.json (local only)");

        var credentials = _users.RootElement.GetProperty("users").EnumerateArray()
            .SelectMany(u => u.GetProperty("credentials").EnumerateArray())
            .Select(c => c.GetProperty("value").GetString())
            .ToList();
        credentials.ShouldNotBeEmpty();
        credentials.ShouldAllBe(value => value == "${CCR_TEST_USER_PASSWORD}");
    }

    [Fact]
    public void Sessions_and_tokens_have_the_documented_lifetimes()
    {
        Realm.GetProperty("accessTokenLifespan").GetInt32().ShouldBe(300);
        Realm.GetProperty("accessCodeLifespan").GetInt32().ShouldBeLessThanOrEqualTo(60);
        Realm.GetProperty("ssoSessionIdleTimeout").GetInt32().ShouldBe(1800);
        Realm.GetProperty("ssoSessionMaxLifespan").GetInt32().ShouldBe(36000);
        Realm.GetProperty("revokeRefreshToken").GetBoolean().ShouldBeTrue();
        Realm.GetProperty("refreshTokenMaxReuse").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void Tokens_are_signed_with_PS256_and_3072_bit_keys_only()
    {
        Realm.GetProperty("defaultSignatureAlgorithm").GetString().ShouldBe("PS256");

        var rsa = Realm.GetProperty("components").GetProperty("org.keycloak.keys.KeyProvider").EnumerateArray()
            .Where(k => k.GetProperty("providerId").GetString()!.StartsWith("rsa", StringComparison.Ordinal))
            .ToList();
        var key = rsa.ShouldHaveSingleItem().GetProperty("config");
        key.GetProperty("algorithm")[0].GetString().ShouldBe("PS256");
        key.GetProperty("keySize")[0].GetString().ShouldBe("3072");

        var attributes = Client.GetProperty("attributes");
        attributes.GetProperty("access.token.signed.response.alg").GetString().ShouldBe("PS256");
        attributes.GetProperty("id.token.signed.response.alg").GetString().ShouldBe("PS256");
        RawText("ccr-realm.json").ShouldNotContain("RS256");
    }

    [Fact]
    public void Brute_force_detection_and_password_policy_are_enabled()
    {
        Realm.GetProperty("bruteForceProtected").GetBoolean().ShouldBeTrue();
        Realm.GetProperty("permanentLockout").GetBoolean().ShouldBeFalse();

        var policy = Realm.GetProperty("passwordPolicy").GetString()!;
        policy.ShouldContain("length(15)");
        policy.ShouldContain("maxLength(128)");
        policy.ShouldContain("passwordBlacklist(");
        // ASVS 6.2.5 / 6.2.10: no composition rules, no periodic rotation.
        foreach (var forbidden in new[] { "upperCase", "lowerCase", "digits", "specialChars", "forceExpiredPasswordChange" })
        {
            policy.ShouldNotContain(forbidden);
        }
    }

    [Fact]
    public void Totp_is_a_default_required_action()
    {
        var totp = Realm.GetProperty("requiredActions").EnumerateArray()
            .Single(a => a.GetProperty("alias").GetString() == "CONFIGURE_TOTP");
        totp.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        totp.GetProperty("defaultAction").GetBoolean().ShouldBeTrue();
        Realm.GetProperty("otpPolicyPeriod").GetInt32().ShouldBe(30);
        Realm.GetProperty("otpPolicyCodeReusable").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Client_gets_only_the_required_scopes()
    {
        var client = Client;
        client.GetProperty("defaultClientScopes").EnumerateArray().Select(e => e.GetString())
            .ShouldBe(["basic", "acr", "profile", "ccr-api"], ignoreOrder: true);
        client.GetProperty("optionalClientScopes").GetArrayLength().ShouldBe(0);
        client.GetProperty("fullScopeAllowed").GetBoolean().ShouldBeFalse();
        RawText("ccr-realm.json").ShouldNotContain("offline_access");
    }

    [Fact]
    public void Audience_roles_and_scope_are_in_the_access_token_only()
    {
        var mappers = Realm.GetProperty("clientScopes").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "ccr-api")
            .GetProperty("protocolMappers").EnumerateArray().ToList();

        mappers.Single(m => m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper")
            .GetProperty("config").GetProperty("included.custom.audience").GetString().ShouldBe("ccr-api");

        foreach (var mapper in mappers)
        {
            var config = mapper.GetProperty("config");
            config.GetProperty("access.token.claim").GetString().ShouldBe("true");
            config.GetProperty("id.token.claim").GetString().ShouldBe("false");
        }

        var claims = mappers.Select(m => m.GetProperty("config")).Where(c => c.TryGetProperty("claim.name", out _))
            .Select(c => c.GetProperty("claim.name").GetString()).ToList();
        claims.ShouldBe([CcrClaimTypes.Roles, CcrClaimTypes.LegalEntities, CcrClaimTypes.BusinessLines], ignoreOrder: true);
    }

    [Fact]
    public void Anonymous_client_registration_is_disabled()
    {
        var policies = Realm.GetProperty("components")
            .GetProperty("org.keycloak.services.clientregistration.policy.ClientRegistrationPolicy").EnumerateArray().ToList();

        policies.ShouldContain(p => p.GetProperty("providerId").GetString() == "trusted-hosts" && p.GetProperty("subType").GetString() == "anonymous");
        policies.Single(p => p.GetProperty("providerId").GetString() == "max-clients")
            .GetProperty("config").GetProperty("max-clients")[0].GetString().ShouldBe("0");
    }

    [Fact]
    public void Realm_roles_are_the_application_roles()
    {
        Realm.GetProperty("roles").GetProperty("realm").EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .ShouldBe(CcrRoles.All, ignoreOrder: true);
    }

    [Fact]
    public void Authentication_events_are_recorded()
    {
        Realm.GetProperty("eventsEnabled").GetBoolean().ShouldBeTrue();
        Realm.GetProperty("adminEventsEnabled").GetBoolean().ShouldBeTrue();
        var types = Realm.GetProperty("enabledEventTypes").EnumerateArray().Select(e => e.GetString()).ToList();
        types.ShouldContain("LOGIN");
        types.ShouldContain("LOGIN_ERROR");
        types.ShouldContain("LOGOUT");
    }

    [Fact]
    public void Realm_Should_Not_Assign_Incompatible_Roles()
    {
        foreach (var user in TestUsers())
        {
            Policies.FindIncompatiblePair(user.Roles).ShouldBeNull($"{user.Name} cumulates incompatible roles");
        }
    }

    [Fact]
    public void Test_users_have_known_roles_and_valid_scopes()
    {
        foreach (var user in TestUsers())
        {
            user.Roles.ShouldNotBeEmpty();
            user.Roles.ShouldAllBe(role => CcrRoles.All.Contains(role));
            user.LegalEntities.ShouldAllBe(lei => lei == "*" || Lei.Create(lei).IsSuccess);
            if (user.Roles.Contains(CcrRoles.FrontOffice))
            {
                user.LegalEntities.ShouldNotContain("*");
                user.BusinessLines.ShouldNotContain("*");
                user.LegalEntities.ShouldNotBeEmpty();
            }

            user.RequiredActions.ShouldBeEmpty("Local test users skip MFA, in Development only");
        }
    }

    [Fact]
    public void Test_users_cover_the_profiles_of_the_ticket()
    {
        TestUsers().Select(u => u.Name).ShouldBe(
            ["rm.markets.fr", "fo.rates.desk", "dir.risk", "fin.reg.de", "rm.nolegal", "lim.admin.fr", "aud.internal"],
            ignoreOrder: true);
    }

    private IEnumerable<(string Name, List<string> Roles, List<string> LegalEntities, List<string> BusinessLines, List<string> RequiredActions)> TestUsers() =>
        _users.RootElement.GetProperty("users").EnumerateArray().Select(u =>
        (
            u.GetProperty("username").GetString()!,
            Strings(u, "realmRoles"),
            u.GetProperty("attributes").TryGetProperty("ccr_legal_entities", out var le) ? le.EnumerateArray().Select(e => e.GetString()!).ToList() : [],
            u.GetProperty("attributes").TryGetProperty("ccr_business_lines", out var bl) ? bl.EnumerateArray().Select(e => e.GetString()!).ToList() : [],
            Strings(u, "requiredActions")));

    private static List<string> Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) ? [.. array.EnumerateArray().Select(e => e.GetString()!)] : [];

    private static string RealmPath(string file) =>
        Path.Combine(SolutionModel.RepositoryRoot(), "src", "CounterpartyRisk.AppHost", "Realms", file);

    private static string RawText(string file) => File.ReadAllText(RealmPath(file));

    private static JsonDocument Load(string file) => JsonDocument.Parse(RawText(file));
}
