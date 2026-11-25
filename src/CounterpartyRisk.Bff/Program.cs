using System.Text.Json.Serialization;
using CounterpartyRisk.Bff.Hosting;
using CounterpartyRisk.Bff.Mock;
using CounterpartyRisk.Bff.Proxy;
using CounterpartyRisk.Bff.Security;
using CounterpartyRisk.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDefaultProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// OIDC code flow + PKCE with Keycloak, server-side session behind the __Host-ccr cookie, anti-CSRF (#9).
// Tokens never reach the browser: YARP attaches the access token when forwarding /api/** to the Api.
builder.AddBffSecurity();
builder.Services.AddApiProxy(builder.Configuration, builder.Environment);
builder.Services.AddOidcDiscoveryHealthCheck(builder.Configuration["Authentication:Authority"]);

// Front-end driven development: the dashboard endpoints are served from synthetic data until
// the Api implements them. Disabled by default; they require a session like any /api/** call.
var useMockData = builder.Configuration.GetValue<bool>("Bff:UseMockData");
if (useMockData)
{
    builder.Services.AddMockDashboard();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    // The BFF is the only public entry point: health endpoints are not exposed outside Development.
    app.MapDefaultEndpoints();
}
else
{
    app.UseHsts();
}

// Public SPA assets are served before authentication; the SPA fallback is anonymous too.
app.UseSpaStaticFiles();

app.UseAuthentication();
app.UseBffRequestProtection();
app.UseAuthorization();

app.MapBffEndpoints();
if (useMockData)
{
    app.MapMockDashboard();
}

app.MapApiProxy();
app.MapSpaFallback();

await app.RunAsync().ConfigureAwait(false);
