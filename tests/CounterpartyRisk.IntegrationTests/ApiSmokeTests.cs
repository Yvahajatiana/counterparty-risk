using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using CounterpartyRisk.IntegrationTests.Security;

namespace CounterpartyRisk.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiSmokeTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_return_200_with_status_only(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_without_internals()
    {
        using var client = _factory.CreateClient();
        // Deny by default: an anonymous caller gets 401 on any route; an authenticated one gets the 404.
        client.DefaultRequestHeaders.Authorization = new("Bearer", Tokens.RiskManagerFr());

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldNotContain("exception", Case.Insensitive);
        body.ShouldNotContain("stack", Case.Insensitive);
    }

    [Fact]
    public async Task Anonymous_unknown_route_returns_401_problem_details()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Problem_details_carry_the_w3c_trace_id()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        traceId.ShouldNotBeNull().ShouldMatch("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");
    }

    [Fact]
    public void Kestrel_does_not_disclose_the_server_header()
    {
        // TestServer bypasses Kestrel, so the option itself is asserted.
        var options = _factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        options.AddServerHeader.ShouldBeFalse();
    }
}
