using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace CounterpartyRisk.Bff.IntegrationTests;

/// <summary>Session, CSRF, proxy and logout abuse cases of the BFF (TM-TB1-01, -02, -09, TM-TB2-01, -06, TM-TB3-10).</summary>
public sealed class BffSessionTests : IDisposable
{
    private readonly BffFactory _factory = new();
    private readonly HttpClient _browser;

    public BffSessionTests()
    {
        _browser = _factory.CreateBrowser();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _browser.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Session_cookie_is_host_prefixed_httponly_secure_and_strict()
    {
        var jar = await SignIn();

        var cookie = jar.LastSetCookies.Where(c => c.StartsWith("__Host-ccr=", StringComparison.Ordinal)).ShouldHaveSingleItem();
        cookie.ShouldContain("path=/", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldNotContain("domain=", Case.Insensitive);
    }

    [Fact]
    public async Task Should_Not_Expose_Any_Token_To_The_Browser()
    {
        var jar = await SignIn();
        using var response = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // No JWT anywhere: neither in the profile nor in any cookie (the session cookie holds a key only).
        body.ShouldNotContain("eyJ");
        body.ShouldNotContain("token", Case.Insensitive);
        jar.Header().ShouldNotContain("eyJ");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();

        using var profile = JsonDocument.Parse(body);
        profile.RootElement.EnumerateObject().Select(p => p.Name)
            .ShouldBe(["name", "roles", "legalEntities", "businessLines", "sessionExpiresAt"], ignoreOrder: true);
        profile.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["ccr-risk-manager"]);
        profile.RootElement.GetProperty("legalEntities")[0].GetString().ShouldBe("CCRSYNTHPARENTFR0057");
    }

    [Fact]
    public async Task Unauthenticated_api_call_gets_401_without_redirect()
    {
        using var response = await Send(HttpMethod.Get, "/api/v1/widgets/w2", new CookieJar(), csrf: true);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        _factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Return403_When_Api_Call_Lacks_Csrf_Header()
    {
        var jar = await SignIn();

        using var response = await Send(HttpMethod.Get, "/api/v1/widgets/w2", jar, csrf: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _factory.Api.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    public async Task Csrf_header_must_be_exactly_1(string value)
    {
        var jar = await SignIn();
        using var request = Request(HttpMethod.Get, "/api/v1/widgets/w2", jar, csrf: false);
        request.Headers.Add("X-CSRF", value);

        using var response = await _browser.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Profile_requires_the_csrf_header()
    {
        var jar = await SignIn();

        using var response = await Send(HttpMethod.Get, "/bff/user", jar, csrf: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_Strip_Client_Authorization_Header_Before_Proxying()
    {
        var jar = await SignIn("rm-1");
        using var request = Request(HttpMethod.Get, "/api/v1/widgets/w2?asOf=2026-11-24", jar, csrf: true);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer forged-by-the-client");
        request.Headers.Add("X-User-Id", "admin");
        request.Headers.Add("X-Real-IP", "10.0.0.1");

        using var response = await _browser.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var forwarded = _factory.Api.Requests.ShouldHaveSingleItem();
        forwarded.Path.ShouldBe("/api/v1/widgets/w2?asOf=2026-11-24");
        forwarded.Headers["Authorization"].ShouldStartWith("Bearer eyJ");
        forwarded.Headers["Authorization"].ShouldNotContain("forged");
        forwarded.Headers.ShouldNotContainKey("Cookie");
        forwarded.Headers.ShouldNotContainKey("X-User-Id");
        forwarded.Headers.ShouldNotContainKey("X-Real-IP");
        forwarded.Headers.ShouldNotContainKey("X-CSRF");
        // The Api never sets a cookie on the browser.
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Health_endpoints_are_not_proxied_nor_exposed()
    {
        var jar = await SignIn();

        using var response = await Send(HttpMethod.Get, "/health/ready", jar, csrf: true);

        response.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        _factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Should_Return401_When_Cookie_Is_Replayed_After_Logout()
    {
        var jar = await SignIn();
        var stolen = jar["__Host-ccr"]!;
        await LoadAntiforgery(jar);

        using var logout = await Logout(jar);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logout.Headers.GetValues("Clear-Site-Data").ShouldHaveSingleItem().ShouldContain("\"cookies\"");

        var replay = new CookieJar();
        replay.Set("__Host-ccr", stolen);
        using var response = await Send(HttpMethod.Get, "/bff/user", replay, csrf: true);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The Keycloak SSO session is ended through the back channel with the refresh token.
        _factory.IdentityProvider.Requests.ShouldContain(r => r.Uri == BffFactory.EndSessionEndpoint && r.Form["refresh_token"] == "refresh-user-1");
    }

    [Fact]
    public async Task Logout_without_antiforgery_token_is_refused_and_keeps_the_session()
    {
        var jar = await SignIn();

        using var logout = await Send(HttpMethod.Post, "/bff/logout", jar, csrf: true);
        using var user = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);

        logout.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        user.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_with_get_is_not_allowed()
    {
        var jar = await SignIn();

        using var response = await Send(HttpMethod.Get, "/bff/logout", jar, csrf: true);

        response.StatusCode.ShouldNotBe(HttpStatusCode.NoContent);
        using var user = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);
        user.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Issue_New_Session_Cookie_On_Login()
    {
        var first = await SignIn();
        var second = await SignIn();

        first["__Host-ccr"].ShouldNotBe(second["__Host-ccr"]);
    }

    [Fact]
    public async Task Oldest_session_is_revoked_beyond_three_concurrent_sessions()
    {
        var sessions = new List<CookieJar>();
        for (var i = 0; i < 4; i++)
        {
            sessions.Add(await SignIn("same-user", sid: $"sid-{i}"));
        }

        using var oldest = await Send(HttpMethod.Get, "/bff/user", sessions[0], csrf: true);
        using var newest = await Send(HttpMethod.Get, "/bff/user", sessions[3], csrf: true);

        oldest.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        newest.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("javascript:alert(1)")]
    public async Task Should_Reject_External_ReturnUrl(string returnUrl)
    {
        using var response = await Send(HttpMethod.Get, $"/bff/login?returnUrl={Uri.EscapeDataString(returnUrl)}", new CookieJar(), csrf: false);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_starts_the_code_flow_with_pkce_s256()
    {
        using var response = await Send(HttpMethod.Get, "/bff/login?returnUrl=%2Fexposures", new CookieJar(), csrf: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith(BffFactory.Issuer + "/protocol/openid-connect/auth");
        location.ShouldContain("response_type=code");
        location.ShouldContain("code_challenge_method=S256");
        location.ShouldContain("client_id=ccr-bff");
        location.ShouldNotContain("client_secret");
        location.ShouldNotContain("offline_access");
    }

    [Fact]
    public async Task Should_Reject_Callback_With_Invalid_State()
    {
        var jar = new CookieJar();

        using var response = await Send(HttpMethod.Get, "/signin-oidc?code=stolen-code&state=forged", jar, csrf: false);
        jar.Capture(response);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        jar["__Host-ccr"].ShouldBeNull();
        _factory.IdentityProvider.Requests.ShouldNotContain(r => r.Uri == BffFactory.TokenEndpoint);
    }

    [Fact]
    public async Task Access_token_is_refreshed_server_side_before_expiry()
    {
        var jar = await SignIn(expiresInSeconds: 30);

        using var response = await Send(HttpMethod.Get, "/api/v1/widgets/w1", jar, csrf: true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refresh = _factory.IdentityProvider.Requests.ShouldHaveSingleItem();
        refresh.Form["grant_type"].ShouldBe("refresh_token");
        refresh.Form["refresh_token"].ShouldBe("refresh-user-1");
        _factory.Api.Requests.ShouldHaveSingleItem().Headers["Authorization"].ShouldBe("Bearer " + _factory.IdentityProvider.RefreshedAccessToken);
    }

    [Fact]
    public async Task Failed_refresh_ends_the_session()
    {
        _factory.IdentityProvider.TokenStatus = HttpStatusCode.BadRequest;
        var jar = await SignIn(expiresInSeconds: 30);

        using var first = await Send(HttpMethod.Get, "/api/v1/widgets/w1", jar, csrf: true);
        _factory.IdentityProvider.TokenStatus = HttpStatusCode.OK;
        using var second = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);

        first.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        second.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Idle_session_expires_after_30_minutes()
    {
        var jar = await SignIn(expiresInSeconds: 86400);

        _factory.Time.Advance(TimeSpan.FromMinutes(31));
        using var response = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Session_ends_after_the_absolute_lifetime_even_when_active()
    {
        var jar = await SignIn(expiresInSeconds: 86400);
        HttpStatusCode status;
        var elapsed = TimeSpan.Zero;
        do
        {
            _factory.Time.Advance(TimeSpan.FromMinutes(20));
            elapsed += TimeSpan.FromMinutes(20);
            using var response = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);
            jar.Capture(response);
            status = response.StatusCode;
        }
        while (status == HttpStatusCode.OK && elapsed < TimeSpan.FromHours(12));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        elapsed.ShouldBeGreaterThan(TimeSpan.FromHours(10) - TimeSpan.FromMinutes(20));
        elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(20));
    }

    [Fact]
    public async Task Backchannel_logout_revokes_the_keycloak_session()
    {
        var jar = await SignIn(sid: "kc-session-1");

        using var response = await BackchannelLogout(BffFactory.LogoutToken("kc-session-1"));
        using var user = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        user.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Backchannel_logout_by_subject_revokes_every_session()
    {
        var first = await SignIn("user-9", sid: "a");
        var second = await SignIn("user-9", sid: "b");

        using var response = await BackchannelLogout(BffFactory.LogoutToken(sid: null, subject: "user-9"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Get, "/bff/user", first, csrf: true)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Send(HttpMethod.Get, "/bff/user", second, csrf: true)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Reject_Backchannel_Logout_Token_With_Nonce()
    {
        var jar = await SignIn(sid: "kc-session-2");

        using var response = await BackchannelLogout(BffFactory.LogoutToken("kc-session-2", withNonce: true));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Get, "/bff/user", jar, csrf: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Reject_Forged_Backchannel_Logout_Token()
    {
        var jar = await SignIn(sid: "kc-session-3");
        using var otherKey = RSA.Create(3072);

        using var forged = await BackchannelLogout(BffFactory.LogoutToken("kc-session-3", key: new RsaSecurityKey(otherKey) { KeyId = "realm-key" }));
        using var withoutEvent = await BackchannelLogout(BffFactory.LogoutToken("kc-session-3", withEvent: false));
        using var garbage = await BackchannelLogout("not-a-jwt");

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        withoutEvent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        garbage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Send(HttpMethod.Get, "/bff/user", jar, csrf: true)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Spa_routes_stay_anonymous()
    {
        using var response = await Send(HttpMethod.Get, "/dashboard", new CookieJar(), csrf: false);

        // No front end build in tests: 503 from the SPA host, never a 401 or a redirect.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task No_cors_header_is_ever_returned()
    {
        var jar = await SignIn();
        using var request = Request(HttpMethod.Get, "/bff/user", jar, csrf: true);
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await _browser.SendAsync(request, Ct);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private async Task<CookieJar> SignIn(string subject = "user-1", string sid = "sid-1", int expiresInSeconds = 300)
    {
        var jar = new CookieJar();
        using var response = await _browser.GetAsync(new Uri($"/test/signin?sub={subject}&sid={sid}&expiresIn={expiresInSeconds}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        jar.Capture(response);
        return jar;
    }

    private async Task LoadAntiforgery(CookieJar jar)
    {
        using var response = await Send(HttpMethod.Get, "/bff/user", jar, csrf: true);
        jar.Capture(response);
        jar["__Host-ccr-xsrf"].ShouldNotBeNull();
    }

    private async Task<HttpResponseMessage> Logout(CookieJar jar)
    {
        using var request = Request(HttpMethod.Post, "/bff/logout", jar, csrf: true);
        request.Headers.Add("X-XSRF-TOKEN", jar["__Host-ccr-xsrf"]);
        var response = await _browser.SendAsync(request, Ct);
        jar.Capture(response);
        return response;
    }

    private async Task<HttpResponseMessage> BackchannelLogout(string logoutToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["logout_token"] = logoutToken });
        return await _browser.PostAsync(new Uri("/bff/backchannel-logout", UriKind.Relative), content, Ct);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, CookieJar jar, bool csrf)
    {
        using var request = Request(method, path, jar, csrf);
        return await _browser.SendAsync(request, Ct);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, CookieJar jar, bool csrf)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        var cookies = jar.Header();
        if (cookies.Length > 0)
        {
            request.Headers.Add("Cookie", cookies);
        }

        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        return request;
    }
}

public sealed class BffStartupTests
{
    [Theory]
    [InlineData("")]
    [InlineData("changeme")]
    [InlineData("${CCR_BFF_CLIENT_SECRET}")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Should_Refuse_To_Start_When_Secret_Is_Missing_Or_Default(string secret)
    {
        using var factory = new BffFactory().WithWebHostBuilder(b => b.UseSetting("Authentication:ClientSecret", secret));

        Should.Throw<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Should_Refuse_To_Start_With_Http_Issuer_Outside_Development()
    {
        using var factory = new BffFactory().WithWebHostBuilder(b => b.UseSetting("Authentication:Authority", "http://idp.test/realms/ccr"));

        Should.Throw<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Should_Refuse_To_Start_Without_Persisted_Key_Ring_Outside_Development()
    {
        using var factory = new BffFactory().WithWebHostBuilder(b => b.UseSetting("Bff:DataProtection:KeysPath", ""));

        Should.Throw<Exception>(() => factory.CreateClient());
    }
}
