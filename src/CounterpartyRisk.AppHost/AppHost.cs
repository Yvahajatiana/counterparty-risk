var builder = DistributedApplication.CreateBuilder(args);

// Secrets come from AppHost user-secrets (Parameters:*), never from appsettings:
//   dotnet user-secrets set "Parameters:pg-password" "<value>" --project src/CounterpartyRisk.AppHost
//   dotnet user-secrets set "Parameters:keycloak-admin-password" "<value>" --project src/CounterpartyRisk.AppHost
var pgSecret = builder.AddParameter("pg-password", secret: true);
var keycloakAdminSecret = builder.AddParameter("keycloak-admin-password", secret: true);

// PostgreSQL 17 (ADR-001). Tag pinned; digest pinning tracked by Dependabot docker (#3).
var db = builder.AddPostgres("postgres", password: pgSecret)
    .WithImageTag("17")
    .WithDataVolume()
    .AddDatabase("ccr");

// TODO(#9): .WithRealmImport("./Realms") once the realm (without any secret) is versioned.
var keycloak = builder.AddKeycloak("keycloak", 8080, adminPassword: keycloakAdminSecret)
    .WithDataVolume();

var api = builder.AddProject<Projects.CounterpartyRisk_Api>("api")
    .WithReference(db)
    .WithReference(keycloak)
    .WaitFor(db)
    .WaitFor(keycloak);

// Only the BFF is reachable from outside: the Api is never exposed directly.
builder.AddProject<Projects.CounterpartyRisk_Bff>("bff")
    .WithReference(api)
    .WithReference(keycloak)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.CounterpartyRisk_Workers>("workers")
    .WithReference(db)
    .WaitFor(db);

// TODO(#12): Angular app (AddJavaScriptApp) behind the BFF.
await builder.Build().RunAsync().ConfigureAwait(false);
