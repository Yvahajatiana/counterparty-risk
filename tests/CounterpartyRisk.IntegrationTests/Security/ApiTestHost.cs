using System.Security.Cryptography;
using System.Text;
using CounterpartyRisk.Api.Http;
using CounterpartyRisk.Domain.Contracts.Results;
using CounterpartyRisk.SharedKernel.Application;
using FluentValidation;
using CounterpartyRisk.SharedKernel.Application.Messaging;
using CounterpartyRisk.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CounterpartyRisk.IntegrationTests.Security;

/// <summary>
/// Api in Production mode with a static issuer configuration (no network): tokens are signed by
/// <see cref="Tokens"/> with a 3072-bit RSA key, like the Keycloak realm.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://idp.test/realms/ccr";

    public static RsaSecurityKey SigningKey { get; } = new(RSA.Create(3072)) { KeyId = "realm-key" };

    protected virtual bool WithTestEndpoints => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Production-like behaviour: no developer exception page, MFA required.
        builder.UseEnvironment("Production");
        builder.UseSetting("Authentication:Authority", Issuer);
        builder.UseSetting("Authentication:MultiFactorAcrValues:0", "mfa");
        builder.UseSetting("AllowedHosts", "localhost");

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                options.Configuration = configuration;
                // The framework post-configuration already built a manager from the authority: replace it.
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            // The identity provider readiness check needs a network: not part of these tests.
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                foreach (var registration in options.Registrations.Where(r => r.Name == "keycloak-discovery").ToList())
                {
                    options.Registrations.Remove(registration);
                }
            });

            if (WithTestEndpoints)
            {
                services.AddCqrs(typeof(ApiFactory).Assembly);
                services.AddSingleton<SqlProbe>();
                services.AddSingleton<IEndpointModule, TestEndpointModule>();
            }
        });
    }
}

/// <summary>Api with business-like test endpoints dispatching through the real pipeline.</summary>
public sealed class ApiWithEndpointsFactory : ApiFactory
{
    protected override bool WithTestEndpoints => true;
}

/// <summary>Counts handler executions: the handler is the only place that would query the database.</summary>
public sealed class SqlProbe
{
    private int _queries;

    public int Queries => _queries;

    public void Hit() => Interlocked.Increment(ref _queries);
}

internal sealed class TestEndpointModule : IEndpointModule
{
    public static readonly Guid FrNettingSet = Guid.Parse("6d3f7a52-0000-4000-8000-000000000001");
    public static readonly Guid DeNettingSet = Guid.Parse("6d3f7a52-0000-4000-8000-000000000002");

    public void Map(RouteGroupBuilder api)
    {
        var test = api.MapGroup("/test");

        test.MapGet("/exposure", async (IDispatcher dispatcher, string[]? legalEntities, string[]? businessLines, CancellationToken ct) =>
                (await dispatcher.Query(new TestExposureQuery(legalEntities, businessLines), ct)).ToHttpResult())
            .RequireAuthorization(Policies.ReadExposure);

        test.MapGet("/capital", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new TestCapitalQuery(), ct)).ToHttpResult())
            .RequireAuthorization(Policies.ReadCapital);

        test.MapGet("/export", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new TestExportQuery(), ct)).ToHttpResult())
            .RequireAuthorization(Policies.Export);

        test.MapGet("/netting-sets/{id:guid}", async (IDispatcher dispatcher, Guid id, CancellationToken ct) =>
                (await dispatcher.Query(new TestNettingSetQuery(id), ct)).ToHttpResult())
            .RequireAuthorization(Policies.ReadExposure);

        test.MapPost("/validated", async (IDispatcher dispatcher, TestValidatedCommand command, CancellationToken ct) =>
                (await dispatcher.Send(command, ct)).ToHttpResult())
            .RequireAuthorization(Policies.ReadCapital);

        // Canary for the policy coverage test: no named policy (only the fallback applies).
        test.MapGet("/fallback-only", () => TypedResults.Ok());
    }
}

public sealed record EffectiveScopeDto(IReadOnlyCollection<string> LegalEntities, bool AllLegalEntities, IReadOnlyCollection<string> BusinessLines, bool AllBusinessLines);

[RequiresCapability(Capability.ReadExposure)]
public sealed record TestExposureQuery(IReadOnlyCollection<string>? LegalEntities, IReadOnlyCollection<string>? BusinessLines)
    : IQuery<EffectiveScopeDto>, IScopedQuery;

[RequiresCapability(Capability.ReadCapital)]
public sealed record TestCapitalQuery : IQuery<int>;

[RequiresCapability(Capability.Export, Capability.ReadExposure)]
public sealed record TestExportQuery : IQuery<int>;

[RequiresCapability(Capability.ReadExposure)]
public sealed record TestNettingSetQuery(Guid Id) : IQuery<string>;

[RequiresCapability(Capability.ReadCapital)]
public sealed record TestValidatedCommand(int Value) : ICommand<int>;

internal sealed class TestExposureQueryHandler(IRequestScope scope, SqlProbe probe) : IQueryHandler<TestExposureQuery, EffectiveScopeDto>
{
    public Task<Result<EffectiveScopeDto>> Handle(TestExposureQuery query, CancellationToken cancellationToken)
    {
        probe.Hit();
        var s = scope.Current;
        return Task.FromResult(Result.Success(new EffectiveScopeDto(
            [.. s.LegalEntities.Order(StringComparer.Ordinal)], s.AllLegalEntities, [.. s.BusinessLines.Order(StringComparer.Ordinal)], s.AllBusinessLines)));
    }
}

internal sealed class TestCapitalQueryHandler(SqlProbe probe) : IQueryHandler<TestCapitalQuery, int>
{
    public Task<Result<int>> Handle(TestCapitalQuery query, CancellationToken cancellationToken)
    {
        probe.Hit();
        return Task.FromResult(Result.Success(42));
    }
}

internal sealed class TestExportQueryHandler : IQueryHandler<TestExportQuery, int>
{
    public Task<Result<int>> Handle(TestExportQuery query, CancellationToken cancellationToken) => Task.FromResult(Result.Success(1));
}

/// <summary>Ownership check of a resource addressed by id: unknown and out of scope give the same 404.</summary>
internal sealed class TestNettingSetQueryHandler(IRequestScope scope) : IQueryHandler<TestNettingSetQuery, string>
{
    private static readonly Dictionary<Guid, (string Lei, string BusinessLine)> Owners = new()
    {
        [TestEndpointModule.FrNettingSet] = ("CCRSYNTHPARENTFR0057", "MARKETS"),
        [TestEndpointModule.DeNettingSet] = ("CCRSYNTHSUBSIDDE0022", "MARKETS"),
    };

    public Task<Result<string>> Handle(TestNettingSetQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Owners.TryGetValue(query.Id, out var owner) && scope.Current.Covers(owner.Lei, owner.BusinessLine)
            ? Result.Success(query.Id.ToString())
            : Result.Failure<string>(AuthorizationErrors.NotFound));
}

internal sealed class TestValidatedCommandHandler : ICommandHandler<TestValidatedCommand, int>
{
    public Task<Result<int>> Handle(TestValidatedCommand command, CancellationToken cancellationToken) => Task.FromResult(Result.Success(command.Value));
}

internal sealed class TestValidatedCommandValidator : AbstractValidator<TestValidatedCommand>
{
    public TestValidatedCommandValidator() => RuleFor(c => c.Value).GreaterThanOrEqualTo(0).WithErrorCode("NotNegative");
}

/// <summary>Access tokens shaped like those of the realm (typ Bearer, azp ccr-bff, aud ccr-api).</summary>
public static class Tokens
{
    public const string ParentFr = "CCRSYNTHPARENTFR0057";
    public const string SubsidiaryDe = "CCRSYNTHSUBSIDDE0022";

    public static string Create(
        string[] roles,
        string[] legalEntities,
        string[] businessLines,
        string? acr = "mfa",
        string audience = "ccr-api",
        string issuer = ApiFactory.Issuer,
        string type = "Bearer",
        string azp = "ccr-bff",
        string algorithm = SecurityAlgorithms.RsaSsaPssSha256,
        SecurityKey? key = null,
        DateTime? expires = null,
        string subject = "user-1")
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["typ"] = type,
            ["azp"] = azp,
            ["roles"] = roles,
            ["ccr_legal_entities"] = legalEntities,
            ["ccr_business_lines"] = businessLines,
            ["preferred_username"] = subject,
        };
        if (acr is not null)
        {
            claims["acr"] = acr;
        }

        var now = DateTime.UtcNow;
        var expiry = expires ?? now.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expiry.AddMinutes(-5),
            NotBefore = expiry.AddMinutes(-5),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(key ?? ApiFactory.SigningKey, algorithm),
        });
    }

    public static string RiskManagerFr() => Create(["ccr-risk-manager"], [ParentFr], ["MARKETS"]);

    public static string FrontOfficeFr() => Create(["ccr-front-office"], [ParentFr], ["MARKETS"]);

    public static string Direction() => Create(["ccr-direction"], ["*"], ["*"]);

    public static string RiskManagerWithoutEntity() => Create(["ccr-risk-manager"], [], ["MARKETS"]);

    /// <summary>Unsigned token (<c>alg: none</c>), built by hand.</summary>
    public static string WithoutSignature()
    {
        static string Encode(string json) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
        var payload = $$"""{"iss":"{{ApiFactory.Issuer}}","aud":"ccr-api","sub":"x","typ":"Bearer","azp":"ccr-bff","roles":["ccr-direction"],"ccr_legal_entities":["*"],"ccr_business_lines":["*"],"exp":{{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}}}""";
        return $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{Encode(payload)}.";
    }
}

internal static class HttpClientExtensions
{
    public static HttpRequestMessage Get(string path, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }
}
