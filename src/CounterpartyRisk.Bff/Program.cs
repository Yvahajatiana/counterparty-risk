using CounterpartyRisk.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDefaultProblemDetails();

// TODO(#9): OIDC code flow + PKCE with Keycloak, HttpOnly/Secure/SameSite cookie,
// anti-forgery, and YARP reverse proxy to the Api (tokens never reach the browser).

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // The BFF is the only public entry point: health endpoints are not exposed outside Development.
    app.MapDefaultEndpoints();
}
else
{
    app.UseHsts();
}

await app.RunAsync().ConfigureAwait(false);
