using System.Net;
using System.Net.Http.Headers;
using CounterpartyRisk.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CounterpartyRisk.SharedKernel.UnitTests.Http;

public class ResilienceDefaultsTests
{
    private static readonly Uri Target = new("http://downstream.test/resource");

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Unsafe_methods_are_never_retried(string method)
    {
        var calls = await CountCallsOnTransientFailure(new HttpMethod(method));

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Safe_methods_are_still_retried()
    {
        var calls = await CountCallsOnTransientFailure(HttpMethod.Get);

        calls.ShouldBeGreaterThan(1);
    }

    private static async Task<int> CountCallsOnTransientFailure(HttpMethod method)
    {
        var downstream = new AlwaysUnavailableHandler();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddServiceDefaults();
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => downstream));
        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
        using var request = new HttpRequestMessage(method, Target);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        return downstream.Calls;
    }

    private sealed class AlwaysUnavailableHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            // Retry-After of zero keeps the retry path fast without changing the production options.
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
