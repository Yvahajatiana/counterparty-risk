# Getting started (back end)

Minimal technical guide for the .NET solution. Completed by #41.

## Prerequisites
- .NET SDK 10 (see `global.json`, `rollForward: latestFeature`).
- Docker Desktop, only to run the Aspire AppHost (PostgreSQL, Keycloak). Build and tests do not need it.
- Local hooks: `git config core.hooksPath .githooks` (requires gitleaks).

## Build and test
```bash
dotnet build CounterpartyRisk.slnx -c Release   # warnings as errors, NuGet audit, analyzers
dotnet test --solution CounterpartyRisk.slnx -c Release
```
Tests run on Microsoft.Testing.Platform (xUnit v3, declared in `global.json`).

## Run locally with Aspire
Secrets are AppHost parameters stored in user-secrets, never in `appsettings*.json`:
```bash
dotnet user-secrets set "Parameters:pg-password" "<choose-one>" --project src/CounterpartyRisk.AppHost
dotnet user-secrets set "Parameters:keycloak-admin-password" "<choose-one>" --project src/CounterpartyRisk.AppHost
dotnet run --project src/CounterpartyRisk.AppHost
```
The Aspire dashboard lists `postgres`, `keycloak`, `api`, `bff` and `workers`. Only the BFF has external
endpoints; the API is internal.

## Layout
```
src/
  CounterpartyRisk.SharedKernel/              Entity, AggregateRoot, ValueObject, Result/Error, Money/Currency
  CounterpartyRisk.SharedKernel.Application/  In-house CQRS dispatcher and pipeline behaviors
  CounterpartyRisk.Domain.Contracts/          Shared contracts, BCL only (#5)
  Modules/<Module>/CounterpartyRisk.<Module>.{Domain,Application,Infrastructure}
  CounterpartyRisk.Api / .Bff / .Workers / .ServiceDefaults / .AppHost
tests/
  CounterpartyRisk.Architecture.Tests/        ADR-001 dependency rules (NetArchTest)
  CounterpartyRisk.SharedKernel.UnitTests/
  CounterpartyRisk.IntegrationTests/          In-memory smoke tests (Testcontainers added by #11)
```

## CQRS in a module
- Requests implement `ICommand<TResult>` / `IQuery<TResult>`; handlers are `internal sealed` and named `*Handler`.
- Register the module's Application assembly in `AddCqrs(...)` (Api, Workers).
- Pipeline order: `LoggingBehavior` -> `ValidationBehavior` -> `AuthorizationBehavior` (#9) -> `AuditBehavior` (#10) -> handler.
  Behaviors added after `AddCqrs` run in registration order.

## Dependencies
Versions live in `Directory.Packages.props` only (Central Package Management, transitive pinning).
Each project commits its `packages.lock.json`; CI restores in locked mode. After changing a version,
run `dotnet restore CounterpartyRisk.slnx --force-evaluate` and commit the updated lock files.
The AppHost has no lock file: the Aspire SDK pulls host-specific (Windows/Linux) orchestration packages.
