using System.Text.Json.Serialization;
using CounterpartyRisk.Bff.Hosting;
using CounterpartyRisk.Bff.Mock;
using CounterpartyRisk.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDefaultProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Front-end driven development: the dashboard endpoints are served from synthetic data until
// the Api implements them. Disabled by default; the demo image turns it on explicitly.
var useMockData = builder.Configuration.GetValue<bool>("Bff:UseMockData");
if (useMockData)
{
    builder.Services.AddMockDashboard();
}

// TODO(#9): OIDC code flow + PKCE with Keycloak, HttpOnly/Secure/SameSite cookie,
// anti-forgery, and YARP reverse proxy to the Api (tokens never reach the browser).

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

if (useMockData)
{
    app.MapMockDashboard();
}

app.MapSpa();

await app.RunAsync().ConfigureAwait(false);
