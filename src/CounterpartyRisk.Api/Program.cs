using CounterpartyRisk.ServiceDefaults;
using CounterpartyRisk.SharedKernel.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDefaultProblemDetails();

// Every module registers its handlers and validators; requests only enter the
// Application layer through IDispatcher and its pipeline (authorization #9, audit #10).
builder.Services.AddCqrs(
    CounterpartyRisk.ReferenceData.Application.AssemblyReference.Assembly,
    CounterpartyRisk.MarketData.Application.AssemblyReference.Assembly,
    CounterpartyRisk.Portfolio.Application.AssemblyReference.Assembly,
    CounterpartyRisk.RiskEngine.Application.AssemblyReference.Assembly,
    CounterpartyRisk.Capital.Application.AssemblyReference.Assembly,
    CounterpartyRisk.Reporting.Application.AssemblyReference.Assembly);

// TODO(#9): JWT bearer authentication and ABAC authorization policies.
// TODO(#11): module DbContexts and PostgreSQL readiness check.

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.MapDefaultEndpoints();

await app.RunAsync().ConfigureAwait(false);
