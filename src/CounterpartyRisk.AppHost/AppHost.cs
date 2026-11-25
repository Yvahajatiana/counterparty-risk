var builder = DistributedApplication.CreateBuilder(args);

// Secrets come from AppHost user-secrets (Parameters:*), never from appsettings:
//   dotnet user-secrets set "Parameters:pg-password" "<value>" --project src/CounterpartyRisk.AppHost
//   dotnet user-secrets set "Parameters:keycloak-admin-password" "<value>" --project src/CounterpartyRisk.AppHost
var pgSecret = builder.AddParameter("pg-password", secret: true);
var keycloakAdminSecret = builder.AddParameter("keycloak-admin-password", secret: true);

// Generated on first run and persisted in the AppHost user-secrets (never committed): the confidential
// client secret shared by Keycloak and the BFF, and the password of the local test users (#9).
var bffClientSecret = builder.AddParameter(
    "ccr-bff-client-secret",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);
var testUserSecret = builder.AddParameter(
    "ccr-test-user-password",
    new GenerateParameterDefault { MinLength = 24, Special = false },
    secret: true,
    persist: true);

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

// Realm "ccr" as code (Realms/ccr-realm.json, no secret) and the local test users (Realms/ccr-users-0.json,
// password from the generated parameter). Keycloak skips the import when the realm already exists in the
// data volume: delete the volume to re-import after changing the realm.
var keycloak = builder.AddKeycloak("keycloak", 8080, adminPassword: keycloakAdminSecret)
    .WithImageSHA256(IdentityProviderDigest)
    .WithDataVolume()
    .WithRealmImport("./Realms")
    .WithBindMount("./Keycloak/password-blacklists", "/opt/keycloak/data/password-blacklists", isReadOnly: true)
    .WithEnvironment("CCR_BFF_CLIENT_SECRET", bffClientSecret)
    .WithEnvironment("CCR_TEST_USER_PASSWORD", testUserSecret);

var realmAuthority = ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/ccr");

var api = builder.AddProject<Projects.CounterpartyRisk_Api>("api")
    .WithReference(db)
    .WithReference(keycloak)
    .WithEnvironment("Authentication__Authority", realmAuthority)
    .WaitFor(db)
    .WaitFor(keycloak);

// Only the BFF is reachable from outside: the Api is never exposed directly.
var bff = builder.AddProject<Projects.CounterpartyRisk_Bff>("bff")
    .WithReference(api)
    .WithReference(keycloak)
    .WithEnvironment("Authentication__Authority", realmAuthority)
    .WithEnvironment("Authentication__ClientSecret", bffClientSecret)
    .WaitFor(api)
    .WaitFor(keycloak)
    .WithExternalHttpEndpoints();

// Exact redirect URIs of the client ccr-bff (no wildcard): the browser-facing HTTPS origin of the BFF.
// The back-channel logout is called by the Keycloak container, through the host network address.
var bffHttps = bff.GetEndpoint("https");
keycloak
    .WithEnvironment("CCR_BFF_BASE_URL", ReferenceExpression.Create($"https://localhost:{bffHttps.Property(EndpointProperty.Port)}"))
    .WithEnvironment("CCR_BFF_BACKCHANNEL_LOGOUT_URL", ReferenceExpression.Create($"{bff.GetEndpoint("http")}/bff/backchannel-logout"));

builder.AddProject<Projects.CounterpartyRisk_Workers>("workers")
    .WithReference(db)
    .WaitFor(db);

// TODO(#12): Angular app (AddJavaScriptApp) behind the BFF.
await builder.Build().RunAsync().ConfigureAwait(false);
