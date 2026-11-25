using CounterpartyRisk.Api.Http;
using CounterpartyRisk.Api.Security;
using CounterpartyRisk.ServiceDefaults;
using CounterpartyRisk.SharedKernel.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.RequireAllowedHosts();
builder.Services.AddDefaultProblemDetails();

// JWT bearer (PS256, audience ccr-api, issuer of realm ccr) and one ABAC policy per capability (#9).
builder.AddApiSecurity();
builder.Services.AddOidcDiscoveryHealthCheck(builder.Configuration["Authentication:Authority"]);

// Every module registers its handlers and validators; requests only enter the Application layer
// through IDispatcher and its pipeline: Logging, Authorization (#9), Validation, Audit (#10).
builder.Services.AddCqrs(
    CounterpartyRisk.ReferenceData.Application.AssemblyReference.Assembly,
    CounterpartyRisk.MarketData.Application.AssemblyReference.Assembly,
    CounterpartyRisk.Portfolio.Application.AssemblyReference.Assembly,
    CounterpartyRisk.RiskEngine.Application.AssemblyReference.Assembly,
    CounterpartyRisk.Capital.Application.AssemblyReference.Assembly,
    CounterpartyRisk.Reporting.Application.AssemblyReference.Assembly);

// TODO(#11): module DbContexts, PostgreSQL readiness check and the RLS interceptor reading ILegalEntityScope.

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapApiEndpoints();

await app.RunAsync().ConfigureAwait(false);
