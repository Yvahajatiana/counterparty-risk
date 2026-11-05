var builder = DistributedApplication.CreateBuilder(args);

// Secrets come from AppHost user-secrets (Parameters:*), never from appsettings:
//   dotnet user-secrets set "Parameters:pg-password" "<value>" --project src/CounterpartyRisk.AppHost
//   dotnet user-secrets set "Parameters:keycloak-admin-password" "<value>" --project src/CounterpartyRisk.AppHost
var pgSecret = builder.AddParameter("pg-password", secret: true);
var keycloakAdminSecret = builder.AddParameter("keycloak-admin-password", secret: true);

// Images are pinned by digest (multi-arch index), resolved from the registries. Aspire keeps the digest only,
// so the version stays in these comments. Not seen by Dependabot: reviewed monthly (ADR-001).
// docker.io/library/postgres:17.11
const string PostgresDigest = "748a18a54d3842d43bc156f0840d78029b8d349e0feca98c12b9c4ce3404a6b3";

// quay.io/keycloak/keycloak:26.8.1
const string IdentityProviderDigest = "3f3c5f8e03c614774b4bd9173b88391fefa4153bfc010d8012d59ad825377332";

var db = builder.AddPostgres("postgres", password: pgSecret)
    .WithImageSHA256(PostgresDigest)
    .WithDataVolume()
    .AddDatabase("ccr");

// TODO(#9): .WithRealmImport("./Realms") once the realm (without any secret) is versioned.
var keycloak = builder.AddKeycloak("keycloak", 8080, adminPassword: keycloakAdminSecret)
    .WithImageSHA256(IdentityProviderDigest)
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
