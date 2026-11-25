using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CounterpartyRisk.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using static CounterpartyRisk.IntegrationTests.Security.HttpClientExtensions;

namespace CounterpartyRisk.IntegrationTests.Security;

/// <summary>JWT validation abuse cases (TM-TB2-03) and ABAC through the real pipeline (TM-TB3-02, TM-TB3-03).</summary>
public sealed class ApiAuthorizationTests(ApiWithEndpointsFactory factory) : IClassFixture<ApiWithEndpointsFactory>
{
    private const string Exposure = "/api/v1/test/exposure";

    private async Task<HttpResponseMessage> Send(string path, string? token)
    {
        using var client = factory.CreateClient();
        using var request = Get(path, token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_Return401_When_No_Token_Is_Presented()
    {
        using var response = await Send(Exposure, null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("CCRSYNTH");
    }

    [Fact]
    public async Task Valid_access_token_is_accepted()
    {
        using var response = await Send(Exposure, Tokens.RiskManagerFr());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Return401_When_Token_Is_Signed_By_Another_Key()
    {
        using var otherKey = RSA.Create(3072);
        var token = Tokens.Create(["ccr-direction"], ["*"], ["*"], key: new RsaSecurityKey(otherKey) { KeyId = "realm-key" });

        using var response = await Send(Exposure, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldNotContain("signature", Case.Insensitive);
    }

    [Fact]
    public async Task Should_Return401_When_Token_Uses_RS256()
    {
        var token = Tokens.Create(["ccr-direction"], ["*"], ["*"], algorithm: SecurityAlgorithms.RsaSha256);

        using var response = await Send(Exposure, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return401_When_Token_Is_Unsigned()
    {
        using var response = await Send(Exposure, Tokens.WithoutSignature());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return401_When_Token_Is_Expired()
    {
        // Beyond the 30 s clock skew.
        var token = Tokens.Create(["ccr-direction"], ["*"], ["*"], expires: DateTime.UtcNow.AddMinutes(-2));

        using var response = await Send(Exposure, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return401_When_Audience_Is_Not_CcrApi()
    {
        using var response = await Send(Exposure, Tokens.Create(["ccr-direction"], ["*"], ["*"], audience: "ccr-bff"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return401_When_Issuer_Is_Another_Realm()
    {
        using var response = await Send(Exposure, Tokens.Create(["ccr-direction"], ["*"], ["*"], issuer: "https://idp.test/realms/master"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return401_When_Id_Token_Is_Presented()
    {
        using var response = await Send(Exposure, Tokens.Create(["ccr-direction"], ["*"], ["*"], type: "ID"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Should_Return401_When_Token_Was_Not_Issued_To_The_Bff()
    {
        using var response = await Send(Exposure, Tokens.Create(["ccr-direction"], ["*"], ["*"], azp: "another-client"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Missing_filter_returns_the_user_scope_only()
    {
        var scope = await (await Send(Exposure, Tokens.RiskManagerFr())).Content
            .ReadFromJsonAsync<EffectiveScopeDto>(TestContext.Current.CancellationToken);

        scope!.LegalEntities.ShouldBe([Tokens.ParentFr]);
        scope.BusinessLines.ShouldBe(["MARKETS"]);
        scope.AllLegalEntities.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Return403_When_User_Requests_Other_LegalEntity()
    {
        using var response = await Send($"{Exposure}?legalEntities={Tokens.SubsidiaryDe}", Tokens.RiskManagerFr());
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldContain("authorization.out_of_scope");
        body.ShouldNotContain(Tokens.ParentFr);
    }

    [Fact]
    public async Task Requested_filter_inside_the_scope_is_applied()
    {
        var scope = await (await Send($"{Exposure}?legalEntities={Tokens.SubsidiaryDe}&businessLines=MARKETS", Tokens.Direction())).Content
            .ReadFromJsonAsync<EffectiveScopeDto>(TestContext.Current.CancellationToken);

        scope!.LegalEntities.ShouldBe([Tokens.SubsidiaryDe]);
        scope.AllBusinessLines.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Return404_When_NettingSet_Is_Out_Of_Scope()
    {
        using var foreign = await Send($"/api/v1/test/netting-sets/{TestEndpointModule.DeNettingSet}", Tokens.RiskManagerFr());
        using var unknown = await Send($"/api/v1/test/netting-sets/{Guid.NewGuid()}", Tokens.RiskManagerFr());
        using var own = await Send($"/api/v1/test/netting-sets/{TestEndpointModule.FrNettingSet}", Tokens.RiskManagerFr());

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Same answer for "exists elsewhere" and "does not exist": no existence oracle.
        (await Code(foreign)).ShouldBe(await Code(unknown));
    }

    [Fact]
    public async Task Should_Return403_And_Issue_No_Sql_When_Scope_Is_Empty()
    {
        var probe = factory.Services.GetRequiredService<SqlProbe>();
        var before = probe.Queries;

        using var response = await Send(Exposure, Tokens.RiskManagerWithoutEntity());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        probe.Queries.ShouldBe(before);
    }

    [Fact]
    public async Task Should_Return403_When_FrontOffice_Calls_Capital_Endpoint()
    {
        using var response = await Send("/api/v1/test/capital", Tokens.FrontOfficeFr());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_Return403_When_FrontOffice_Exports()
    {
        using var response = await Send("/api/v1/test/export", Tokens.FrontOfficeFr());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Export_requires_the_multi_factor_level()
    {
        using var singleFactor = await Send("/api/v1/test/export", Tokens.Create(["ccr-risk-manager"], [Tokens.ParentFr], ["MARKETS"], acr: "pwd"));
        using var multiFactor = await Send("/api/v1/test/export", Tokens.RiskManagerFr());

        singleFactor.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        multiFactor.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Should_Return403_When_Token_Has_Incompatible_Roles()
    {
        using var response = await Send(Exposure, Tokens.Create(["ccr-risk-manager", "ccr-limit-admin"], [Tokens.ParentFr], ["MARKETS"]));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Should_Return403_When_FrontOffice_Scope_Is_Wildcard()
    {
        using var response = await Send(Exposure, Tokens.Create(["ccr-front-office"], ["*"], ["MARKETS"]));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Endpoint_with_fallback_policy_still_rejects_invalid_principals()
    {
        using var anonymous = await Send("/api/v1/test/fallback-only", null);
        using var incompatible = await Send("/api/v1/test/fallback-only", Tokens.Create(["ccr-auditor", "ccr-direction"], ["*"], ["*"]));

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        incompatible.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unauthorized_caller_gets_403_never_a_field_error()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/test/validated", UriKind.Relative))
        {
            Content = JsonContent.Create(new { value = -1 }),
        };
        request.Headers.Authorization = new("Bearer", Tokens.FrontOfficeFr());

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldNotContain("NotNegative");
        body.ShouldNotContain("errors");
    }

    [Fact]
    public async Task Policy_coverage_check_detects_an_endpoint_without_a_named_policy()
    {
        EndpointPolicyCoverage.Offenders(factory.Services).ShouldBe(["/api/v1/test/fallback-only"]);
        await Task.CompletedTask;
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("code").GetString();
    }
}

public sealed class ApiEndpointCoverageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Every_Endpoint_Should_Have_A_Named_Policy()
    {
        EndpointPolicyCoverage.Offenders(factory.Services).ShouldBeEmpty();
    }

    [Fact]
    public void Anonymous_endpoints_are_the_internal_health_checks_only()
    {
        var anonymous = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        anonymous.ShouldBe(["/health/live", "/health/ready"], ignoreOrder: true);
    }

    [Fact]
    public void Startup_fails_without_an_issuer()
    {
        using var broken = new ApiFactory().WithWebHostBuilder(b => b.UseSetting("Authentication:Authority", ""));

        Should.Throw<Exception>(() => broken.CreateClient());
    }

    [Fact]
    public void Startup_fails_with_an_http_issuer_outside_development()
    {
        using var broken = new ApiFactory().WithWebHostBuilder(b => b.UseSetting("Authentication:Authority", "http://idp.test/realms/ccr"));

        Should.Throw<Exception>(() => broken.CreateClient());
    }
}

/// <summary>"Every endpoint has a named policy" (API5:2023 BFLA): anonymous only for /health, otherwise a Policies name.</summary>
internal static class EndpointPolicyCoverage
{
    public static IReadOnlyList<string?> Offenders(IServiceProvider services)
    {
        var known = Policies.AllCapabilities.Select(Policies.PolicyName).ToHashSet(StringComparer.Ordinal);
        return [.. services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e =>
            {
                var path = e.RoutePattern.RawText ?? string.Empty;
                if (e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                {
                    return !path.StartsWith("/health/", StringComparison.Ordinal);
                }

                var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
                return policies.Count == 0 || !policies.All(p => p is not null && known.Contains(p));
            })
            .Select(e => e.RoutePattern.RawText)];
    }
}
