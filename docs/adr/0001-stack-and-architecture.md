# ADR-0001: Stack, Clean/DDD/CQRS modular monolith, repository layout and conventions

- **Status:** Proposed
- **Date:** 2026-11-04
- **Deciders:** agent:lead (with agent:back-3 for the .NET platform and agent:front for the web stack)
- **Issue:** #3
- **Supersedes:** none

## Context and problem statement

The Counterparty Risk Dashboard computes and displays counterparty credit risk measures (exposure
cascade, SA-CCR EAD, EE/PFE profiles, CVA, RWA, limits, stress) on public open data and synthetic
portfolios. It is built by a fixed team of agents (Lead, Back-1, Back-2, Back-3, Front, with the Expert
CCR as business owner) working in parallel in separate git worktrees.

Before any application code is written we must fix, in one place:

- the technical stack with exact versions, so that `global.json`, `Directory.Packages.props` (#7) and
  `web/package.json` (#12) pin the same thing;
- the module boundaries (bounded contexts), their PostgreSQL schemas and the dependency rules that the
  architecture tests of #7 enforce;
- the libraries whose licence changed recently (MediatR, AutoMapper, FluentAssertions, EPPlus, Duende,
  MassTransit) and the Highcharts licence question;
- the team conventions (branches, worktrees, commits, comments) and the protection of `main`.

## Decision drivers

- **Secure by Design first**: every dependency has a verified licence and active maintenance, the
  supply chain is pinned, authorisation cannot be bypassed by construction.
- **Parallel work by several agents**: stable names and boundaries early, enforced by tests, so that
  #5, #7, #11 and #12 can start on this ADR while it is still `Proposed`.
- **Open-source, non-commercial demonstration project**: no paid licence; anything only free for
  non-commercial use is recorded with its exit path.
- **Regulatory computations**: amounts in `decimal`, reproducible results, traceable method versions.
- **Simplicity**: one deployable back end (modular monolith) rather than microservices.

## Decision outcome

### 1. Technical stack (exact versions)

Versions are the latest stable releases on 2026-11-04. They are pinned exactly (no floating ranges,
no `^`/`~`) by the issue that introduces them; Dependabot proposes upgrades afterwards.

#### Back end

| Concern | Choice | Version | Licence (SPDX) |
| --- | --- | --- | --- |
| Runtime / SDK | .NET 10 LTS (`global.json`, `rollForward: latestPatch`) | SDK 10.0.403, runtime 10.0.14 | MIT |
| Solution file | `CounterpartyRisk.slnx` (XML solution format, default in .NET 10) | - | - |
| Web API | ASP.NET Core Minimal APIs (no MVC controllers), `ProblemDetails` (RFC 9457) | 10.0.14 | MIT |
| ORM | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.14 / 10.0.3 | MIT / PostgreSQL |
| Database | PostgreSQL 17 (container image `postgres:17.11`, digest pinned in #7) | 17.11 | PostgreSQL |
| Local orchestration | .NET Aspire (`Aspire.Hosting.*`; `Aspire.Hosting.Keycloak` is preview, pinned) | 13.6.4 | MIT |
| CQRS dispatcher | In-house (`CounterpartyRisk.SharedKernel.Application`), see section 4 | - | project |
| Mapping | Manual mapping; Mapperly (source generator) allowed where it removes boilerplate | Riok.Mapperly 4.3.1 | Apache-2.0 |
| Validation | FluentValidation | 12.2.0 | Apache-2.0 |
| Scheduling | Quartz.NET (`Quartz.Extensions.Hosting`, persistent job store in PostgreSQL) | 4.7.2 | Apache-2.0 |
| Identity provider | Keycloak (OIDC, authorization code + PKCE) | 26.8.1 | Apache-2.0 |
| BFF | In-house BFF: ASP.NET Core OIDC + cookie authentication + YARP | YARP 2.3.0 | MIT |
| Observability | OpenTelemetry .NET (OTLP to the Aspire dashboard) | 1.20.1 | Apache-2.0 |
| Excel / CSV export | ClosedXML (export hardening against formula injection in #31) | 0.105.1 | MIT |
| Unit / integration tests | xUnit v3 (`xunit.v3`), Shouldly, NSubstitute, Testcontainers.PostgreSql | 4.0.2 / 4.3.0 / 6.2.0 / 4.16.0 | Apache-2.0 / BSD-3-Clause / BSD-3-Clause / MIT |
| Architecture tests | NetArchTest.Rules | 1.3.2 | MIT |
| Load tests | Grafana k6 (scripts in `tests/load/`, #39) | v2.3.0 | AGPL-3.0 (tool only, not distributed) |

PostgreSQL 18 was evaluated and not retained for V1: the Aspire, Npgsql and Testcontainers ecosystem
is validated on 17, and 18 brings nothing V1 needs. Moving to 18 is a later ADR.

#### Front end

| Concern | Choice | Version | Licence (SPDX) |
| --- | --- | --- | --- |
| Framework | Angular, standalone components, signals, zoneless change detection | 22.2.x (latest stable when #12 starts) | MIT |
| Components | Angular Material (Material 3 theme), corporate style | 22.2.x | MIT |
| Layout utilities | Tailwind CSS v4 (layout only, preflight disabled or in a lower layer) | 4.3.4 | MIT |
| Data grids | AG Grid **Community** (`ag-grid-community`, `ag-grid-angular`) - never the Enterprise package | 36.3.0 | MIT |
| Charts | Highcharts via `highcharts-angular`, behind a `ChartAdapter` (see section 6) | 13.1.1 / 5.4.1 | Highcharts licence (non-commercial free) / MIT |
| Charts fallback | Apache ECharts (only if the adapter is switched) | 6.1.0 | Apache-2.0 |

#12 records the exact Angular patch version in `web/package.json` at bootstrap time.

### 2. Excluded libraries (licence decisions)

| Library | Reason | Replacement |
| --- | --- | --- |
| MediatR (v13+) | Commercial licence since 2025 | In-house CQRS dispatcher (section 4) |
| AutoMapper (v15+) | Commercial licence since 2025 | Manual mapping or Mapperly (Apache-2.0) |
| MassTransit (v9+) | Commercial licence | Not needed in V1 (in-process integration events) |
| FluentAssertions (v8+) | Commercial licence (Xceed) | Shouldly (BSD-3-Clause) |
| EPPlus (v5+) | Polyform Noncommercial / commercial licence | ClosedXML (MIT) |
| Duende BFF / IdentityServer | Commercial licence | Keycloak + in-house BFF (YARP + OIDC cookie) |
| AG Grid Enterprise | Commercial licence | AG Grid Community only |

The architecture tests (#7) fail if an assembly references `MediatR`, `AutoMapper`, `MassTransit` or
`FluentAssertions`. The SBOM and licence check of #8 (back) and #12 (front) keep this list enforced.

### 3. Architecture: modular monolith, Clean/Onion, one module per bounded context

One deployable API, one BFF and one worker host share a single PostgreSQL database. Each bounded
context is a module with its own layers and its own PostgreSQL schema.

| Bounded context | Module folder | Layers | PostgreSQL schema | Main issues | Owner |
| --- | --- | --- | --- | --- | --- |
| Reference data (GLEIF, LEI, groups, legal entities, business lines) | `src/Modules/ReferenceData` | Domain, Application, Infrastructure | `refdata` | #15 | agent:back-1 |
| Market data (ECB curves, FX reference rates) | `src/Modules/MarketData` | Domain, Application, Infrastructure | `market` | #16 | agent:back-1 |
| Portfolio (trades, netting sets, CSA, collateral, limits, financings) | `src/Modules/Portfolio` | Domain, Application, Infrastructure | `portfolio` | #18, #27, #43 | agent:back-2 |
| Risk engine (exposure cascade, SA-CCR, EE/PFE, CVA, stress, CDS) | `src/Modules/RiskEngine` | Domain, Application, Infrastructure | `risk` | #20-#22, #24, #28, #29 | agent:back-2 |
| Capital (RWA, output floor, large exposures) | `src/Modules/Capital` | Domain, Application, Infrastructure | `capital` | #23, #30 | agent:back-3 |
| Reporting / query (CQRS read models, exports) | `src/Modules/Reporting` | Application, Infrastructure | `reporting` | #31 | agent:back-3 |

Cross-cutting tables (Quartz job store, audit journal) live in their own schemas (`quartz`, `audit`),
owned by #19 and #10.

#### Repository layout

```
CounterpartyRisk.slnx
global.json  nuget.config  Directory.Build.props  Directory.Packages.props  .editorconfig
src/
  CounterpartyRisk.SharedKernel/               # Entity, AggregateRoot, ValueObject, Result, IDomainEvent, Money
  CounterpartyRisk.SharedKernel.Application/   # in-house CQRS dispatcher + pipeline behaviours
  CounterpartyRisk.Domain.Contracts/           # shared immutable contracts, BCL only (#5)
  Modules/
    ReferenceData/
      CounterpartyRisk.ReferenceData.Domain/
      CounterpartyRisk.ReferenceData.Application/
      CounterpartyRisk.ReferenceData.Infrastructure/
    MarketData/      (same three projects: CounterpartyRisk.MarketData.*)
    Portfolio/       (CounterpartyRisk.Portfolio.*)
    RiskEngine/      (CounterpartyRisk.RiskEngine.*)
    Capital/         (CounterpartyRisk.Capital.*)
    Reporting/
      CounterpartyRisk.Reporting.Application/
      CounterpartyRisk.Reporting.Infrastructure/
  CounterpartyRisk.Api/              # Minimal APIs, JWT bearer from the BFF, ABAC, ProblemDetails
  CounterpartyRisk.Bff/              # OIDC cookie + YARP to Api (#9)
  CounterpartyRisk.Workers/          # Quartz.NET ingestion and calculation jobs (#19)
  CounterpartyRisk.ServiceDefaults/  # OpenTelemetry, health checks, resilience
  CounterpartyRisk.AppHost/          # .NET Aspire orchestration
tests/
  CounterpartyRisk.Architecture.Tests/
  CounterpartyRisk.<Module>.UnitTests/       # e.g. CounterpartyRisk.RiskEngine.UnitTests
  CounterpartyRisk.IntegrationTests/         # Testcontainers PostgreSQL (PostgresFixture from #11)
  load/                                      # k6 scripts (#39)
contracts/
  openapi/counterparty-risk.v0.yaml          # OpenAPI 3.1 contract (#5)
  mocks/w01..w12/                            # synthetic mock payloads (#5)
web/                                         # Angular workspace (#12)
deploy/                                      # Dockerfiles, compose, Keycloak realm exports without secrets
scripts/                                     # repository and operations scripts (PowerShell)
docs/
  adr/                                       # this folder
  security/                                  # threat model, ASVS mapping (#4)
  conventions.md
```

Notes for the dependent issues:

- **#12**: the Angular workspace lives in `web/` (not `frontend/`). The front CI triggers on `web/**`
  and Dependabot `npm` watches `/web`.
- **#5**: `Domain.Contracts` is `src/CounterpartyRisk.Domain.Contracts/`; the OpenAPI contract and the
  mocks live in `contracts/`; the generated TypeScript client goes to `web/src/app/api/`.
- **#7**: the solution uses the `.slnx` format; project names follow
  `CounterpartyRisk.<Module>.<Layer>`, namespaces match project names.

#### Dependency rules (enforced by the architecture tests of #7)

1. `*.Domain` depends only on `CounterpartyRisk.SharedKernel` and `CounterpartyRisk.Domain.Contracts`
   (no EF Core, no ASP.NET Core, no `System.Net.Http`).
2. `*.Application` depends on its own `*.Domain`, `SharedKernel.Application` and `Domain.Contracts`;
   never on `*.Infrastructure`, `Api`, `Bff` or `Microsoft.EntityFrameworkCore`.
3. `*.Infrastructure` depends on its own `*.Application` (and therefore `*.Domain`).
4. A module calls another module **only** through `Domain.Contracts` interfaces or an integration
   event. `Capital.*` never references `RiskEngine.Domain`, and so on.
5. `CounterpartyRisk.Domain.Contracts` references the BCL only.
6. `Api` references no `*.Domain`: it exposes DTOs only (no serialised entity, protection against
   API3:2023 BOPLA). It dispatches commands and queries through `IDispatcher` only, so authorisation
   (`AuthorizationBehavior`, #9) and audit (`AuditBehavior`, #10) cannot be bypassed.
7. Each module's `DbContext` maps its own schema only. Cross-schema reads go through the
   `reporting` read models, never through another module's `DbContext`.
8. Row-Level Security (#11) is a second line of defence on top of rule 6, not a replacement.

### 4. CQRS

- **In-house dispatcher** in `CounterpartyRisk.SharedKernel.Application`: `ICommand<TResult>`,
  `IQuery<TResult>`, `ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResult>`,
  `IDispatcher`, `IPipelineBehavior<TRequest, TResult>`. Handlers are registered by assembly scanning at
  start-up (a source generator may replace scanning later without changing the interfaces).
- Pipeline order: `LoggingBehavior` -> `ValidationBehavior` (FluentValidation) ->
  `AuthorizationBehavior` (#9) -> `AuditBehavior` (#10) -> handler.
- No notifications / pub-sub in the dispatcher for V1: integration events between modules are
  in-process and explicit (outbox can be added by a later ADR).
- **Write side**: aggregates loaded and saved with EF Core change tracking, one aggregate per
  transaction where possible. Commands return an identifier or a `Result`, never a read model.
- **Read side**: dedicated read models in the `reporting` schema (projection tables or materialised
  views), queried with `AsNoTracking`. Widget queries (#31) read only from there.
- Considered alternative: the `Mediator` source-generator package (MIT). Rejected for V1 to keep full
  control of the pipeline and zero third-party surface on the most central abstraction; it remains the
  fallback if the in-house dispatcher grows beyond its minimal scope.

### 5. Identity and BFF

- Keycloak is the OIDC provider (authorization code + PKCE, confidential client held by the BFF).
- The browser never holds an access token: the BFF keeps tokens server side and issues an
  `HttpOnly; Secure; SameSite=Strict` session cookie, plus an anti-CSRF header requirement.
- The BFF forwards API calls with YARP and attaches the access token. Only the BFF is exposed
  (`WithExternalHttpEndpoints()`); the API is internal.
- Duende BFF is not used (commercial licence); the BFF is a thin in-house component owned by #9.

### 6. Charts: Highcharts licence and `ChartAdapter`

- Highcharts is **free only for non-commercial use** (personal, educational, demonstration). This
  public demonstration repository falls in that scope. **Any commercial use requires a paid Highcharts
  licence.** The Highcharts credits stay enabled.
- To keep an exit path, #14 must put every chart behind a `ChartAdapter` abstraction (one
  Highcharts implementation in V1). Widgets (#32-#37) depend on the adapter only, never on the
  Highcharts API, so that switching to Apache ECharts (Apache-2.0) is a change of adapter, not of widgets.
- The adapter also enforces the domain rule "never mix Economic (EE/PFE) and Regulatory (EAD/RWA)
  measures in one chart" from the `MeasureFamily` carried by each series (#5).
- `web/THIRD_PARTY_LICENSES.md` (#12) and the licence documentation (#41) reference this section.

### 7. Engineering conventions

Detailed in [`docs/conventions.md`](../conventions.md). Summary:

- Branches `<agent>/<issue>-<short-name>` (e.g. `back-2/21-sa-ccr`), one git worktree per issue.
- Conventional Commits with a scope and the issue reference (`feat(risk-engine): add SA-CCR add-on #21`).
  No `Co-Authored-By` trailer.
- C#: `.editorconfig`, `Nullable` and `TreatWarningsAsErrors` enabled, .NET analyzers with security
  rules (CA2100, CA3001-CA3012, CA5350-CA5403) as errors, SecurityCodeScan as a complement.
- Amounts in `decimal`; `double` is allowed only inside the Monte-Carlo core (#22) and converted back.
- Naming: `XxxCommand` / `XxxQuery` / `XxxHandler`, `IXxxRepository`, tables and columns in `snake_case`.
- Code comments in English; issues, PRs and documentation may be in French.

### 8. Protection of `main`

A repository ruleset, versioned in [`assets/main-ruleset.json`](assets/main-ruleset.json) and applied by
[`scripts/setup-branch-protection.ps1`](../../scripts/setup-branch-protection.ps1) after this ADR is
merged:

- pull request required, 0 approval required (all agents share one GitHub account, so self-approval
  is impossible), all review threads resolved;
- required status checks: `Gitleaks (full history)`, `TruffleHog (verified secrets)`,
  `Forbidden files guard`; build, tests and CodeQL are added when #8 lands;
- linear history (squash or rebase merges only), force-push and branch deletion forbidden;
- repository administrators may bypass **only through a pull request** (`bypass_mode: pull_request`):
  direct pushes to `main` stay rejected for everyone, but the single admin account is never locked out
  of merging.

The Lead's validation is a procedural step: `@claude review` comment, then an acceptance comment by
the Lead on the PR before merging.

## Consequences

- Good: one deployable back end, clear module boundaries tested in CI, no commercial licence in the
  dependency tree, early pinned versions shared by every agent.
- Good: authorisation and audit are structural (dispatcher pipeline) and cannot be skipped by an endpoint.
- Bad: the in-house dispatcher and BFF are code we maintain; they must stay minimal.
- Bad: the Highcharts licence must be revisited if the project ever becomes commercial (mitigated by
  the `ChartAdapter`).
- Bad: no human approval is technically enforced on PRs (single account); compensated by required
  checks, `@claude review` and the Lead's acceptance comment.
- Follow-up: #7 (skeleton and architecture tests), #8 (CI checks added to the ruleset, SBOM, licence
  check), #9 (BFF), #11 (schemas, RLS), #12 (`web/` workspace), #14 (`ChartAdapter`), #41 (licence docs).

### Confirmation

- Architecture tests of #7 (dependency rules, banned libraries, handler naming, no controllers).
- Licence check and SBOM in CI (#8 back, #12 front).
- Every `uses:` of every workflow is pinned by commit SHA with the version in a comment; Dependabot
  (`github-actions`, `nuget`, `npm`, `docker`) proposes upgrades.
- Ruleset active on `main` (checked with `gh api repos/Yvahajatiana/counterparty-risk/rulesets`).

## Security and licensing notes

- **Supply chain**: GitHub Actions pinned by SHA, `permissions: contents: read` by default in each
  workflow, `persist-credentials: false` on checkout, no `pull_request_target` on untrusted code;
  NuGet Central Package Management with lock files, `NuGetAudit` blocking and `packageSourceMapping`
  (nuget.org only) against dependency confusion (#7); npm exact versions and `npm ci` (#12); container
  images pinned by tag and digest. Image tags declared in the Aspire AppHost C# code are invisible to
  Dependabot: they are reviewed monthly by agent:back-3 until a Dockerfile-based reference exists in
  `deploy/`.
- **Secrets**: user-secrets locally (Aspire parameters with `secret: true`), GitHub secrets in CI, a vault
  (HashiCorp Vault / Azure Key Vault) as target. No connection string with a password in
  `appsettings*.json` (gitleaks rule `connection-string-password`).
- **No Anthropic API key in CI**: PR reviews go through the Claude GitHub App (`@claude review`) and the
  local `/security-review`; no workflow ever reads an Anthropic key (gitleaks rule `anthropic-api-key`).
- **Local guardrails**: the `.githooks/pre-commit` hook (gitleaks + forbidden files) is mandatory for
  every contributor (`git config core.hooksPath .githooks`), see `CONTRIBUTING.md`.
- **Data**: only public open data and synthetic portfolios; the EDB (`docs/edb.md`) is never committed.
- **Licence watch**: a licence change of a retained dependency is a risk tracked through Dependabot PRs
  and the SBOM; any change triggers a new ADR.

## More information

- Issue #3 (this decision), #5 (contracts), #7 (skeleton), #8 (CI), #9 (OIDC/BFF), #11 (schemas, RLS),
  #12 (Angular workspace), #14 (`ChartAdapter`), #41 (licence documentation).
- MADR: https://adr.github.io/madr/
- Highcharts licensing: https://www.highcharts.com/license
- GitHub rulesets: https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets
