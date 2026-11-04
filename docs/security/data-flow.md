# Data flow diagram (DFD level 1)

Status: **v1.0** (issue #4). Companion of [threat-model.md](threat-model.md). Any new component, flow or
external source must be added here **and** analysed in the threat model before it is merged.

All identifiers in this document are synthetic. No hostname, URL or account below is a real deployment
value: `<...>` marks a placeholder.

## Diagram

The dashed blocks are trust zones. A flow that leaves a zone crosses a **trust boundary** `TB1` to `TB7`;
its label carries the boundary identifier.

```mermaid
flowchart LR
  subgraph Z0["Z0 - User workstation (untrusted)"]
    USR(["Risk user"])
    BRW["Browser - Angular SPA<br/>no token, session cookie only"]
    XF[["Exported CSV / XLSX file"]]
  end

  subgraph Z1["Z1 - Edge (only public entry point)"]
    BFF["CounterpartyRisk.Bff<br/>cookie session, OIDC client, YARP, CSP, rate limiting"]
  end

  subgraph Z2["Z2 - Identity provider"]
    KC["Keycloak - realm ccr"]
  end

  subgraph Z3["Z3 - Internal application network"]
    API["CounterpartyRisk.Api<br/>JWT bearer, ABAC, CQRS, audit"]
    WRK["Workers - Quartz jobs<br/>ingestion, risk engine, projections"]
    MIG["Migration bundle job"]
    QUA[("Quarantine - temp files")]
  end

  subgraph Z4["Z4 - Data"]
    PG[("PostgreSQL<br/>RLS, roles, audit schema")]
  end

  subgraph Z5["Z5 - External open data (untrusted)"]
    EXT["GLEIF, ECB, BIS, EBA,<br/>FFIEC FR Y-9C, CFTC / CME SDR"]
  end

  subgraph Z6["Z6 - Supply chain"]
    GH["GitHub repository + Actions"]
    REG["Package and image registries<br/>NuGet, npm, container"]
    ART[["Build artefacts, images, SBOM"]]
  end

  USR --> BRW
  BRW -- "F1 TB1 HTTPS: SPA, /bff/*, /api/* + session cookie + X-CSRF" --> BFF
  BFF -- "F2 TB1 HTTPS: JSON, export stream" --> BRW
  BRW -- "F3 TB2 HTTPS: login page, front channel" --> KC
  BFF -- "F4 TB2 HTTPS: code + PKCE exchange, refresh, logout" --> KC
  KC -- "F5 TB2 HTTPS: back-channel logout token" --> BFF
  BFF -- "F6 TB3 HTTPS: /api/v1/* + Bearer access token" --> API
  API -- "F7 TB2 HTTPS: OIDC metadata + JWKS" --> KC
  API -- "F8 TB4 SQL over TLS: role cr_reader + app.legal_entity_ids" --> PG
  API -- "F9 TB4 SQL over TLS: role cr_audit_writer, INSERT only" --> PG
  BFF -- "F10 TB4 SQL over TLS: ticket store, Data Protection keys" --> PG
  WRK -- "F11 TB5 HTTPS GET: files, SDMX, APIs" --> EXT
  WRK -- "F12 local write: streamed download" --> QUA
  WRK -- "F13 TB4 SQL over TLS: role cr_writer, COPY + MERGE" --> PG
  MIG -- "F14 TB4 SQL over TLS: role cr_migrator, DDL" --> PG
  BRW -- "F15 TB7 save file" --> XF
  GH -- "F16 TB6 build, test, scan, publish" --> ART
  REG -- "F17 TB6 dependencies, base images" --> GH
  ART -- "F18 TB6 deployment" --> Z3
```

## Trust boundaries

| Id | Boundary | Crossing flows | Authentication across the boundary | Main controls |
| --- | --- | --- | --- | --- |
| TB1 | Browser to BFF (`CounterpartyRisk.Bff`) | F1, F2 | Session cookie `__Host-ccr` (opaque key, server-side ticket) | TLS, cookie flags, `X-CSRF` header, CSP, rate limiting, audit |
| TB2 | BFF / browser / API to Keycloak | F3, F4, F5, F7 | OIDC Authorization Code + PKCE S256, confidential client `ccr-bff` | Exact redirect URIs, `state` / `nonce`, issuer and signature validation, MFA |
| TB3 | BFF to API (`CounterpartyRisk.Api`) | F6 | JWT access token, audience `ccr-api`, added server-side by YARP | API not exposed outside the internal network, JWT revalidated, ABAC per request |
| TB4 | API / BFF / workers / migrations to PostgreSQL | F8, F9, F10, F13, F14 | One database role per component | Least privilege roles, `FORCE` RLS, TLS `VerifyFull`, parameterised SQL |
| TB5 | Workers to external open data sources | F11, F12 | Server certificate only (sources are anonymous) | HTTPS, host allowlist, no cross-domain redirect, size limits, integrity checks, safe parsing, quarantine |
| TB6 | CI/CD (GitHub Actions) to repository, registries and artefacts | F16, F17, F18 | `GITHUB_TOKEN` with least permissions | Actions pinned by SHA, secret scanning, dependency and image scanning, SBOM, branch protection |
| TB7 | Export file to the user workstation | F15 | None (the file leaves the system) | Formula neutralisation, row cap, export policy, audit with file hash, licence filtering |

Database role names follow #11 and #10: `cr_owner` (NOLOGIN owner), `cr_migrator`, `cr_writer`,
`cr_reader`, `cr_audit_writer`, `cr_audit_reader`. They implement the `app_api` (read) / `app_worker` (write)
split described in the issue.

## Flows

| Flow | From | To | Data (class, see [data-classification.md](data-classification.md)) | Protocol | Boundary |
| --- | --- | --- | --- | --- | --- |
| F1 | Browser | BFF | Requests, axis filters (C1), session cookie (C2 reference) | HTTPS, TLS 1.2+ | TB1 |
| F2 | BFF | Browser | SPA assets (C0), widget figures and exports (C1), `/bff/user` profile (C1-PD) | HTTPS | TB1 |
| F3 | Browser | Keycloak | Username, password, OTP (C2 / C1-PD) | HTTPS | TB2 |
| F4 | BFF | Keycloak | Authorization code, PKCE verifier, client secret, tokens (C2) | HTTPS, back channel | TB2 |
| F5 | Keycloak | BFF | Logout token (C2) | HTTPS, back channel | TB2 |
| F6 | BFF | API | Access token (C2), query parameters (C1) | HTTPS on the internal network | TB3 |
| F7 | API | Keycloak | Public signing keys, metadata (C0) | HTTPS | TB2 |
| F8 | API | PostgreSQL | Read models, portfolio and results (C1), RLS scope | PostgreSQL protocol over TLS | TB4 |
| F9 | API | PostgreSQL | Audit events (C1-PD) | PostgreSQL over TLS | TB4 |
| F10 | BFF | PostgreSQL | Authentication tickets with tokens, Data Protection key ring (C2, encrypted) | PostgreSQL over TLS | TB4 |
| F11 | Workers | External sources | Open data files and SDMX responses (C0, some non-redistributable) | HTTPS GET | TB5 |
| F12 | Workers | Quarantine | Raw downloaded files (C0, untrusted) | Local file system | inside Z3 |
| F13 | Workers | PostgreSQL | Reference data, market data, synthetic portfolio, results (C0, C1) | PostgreSQL over TLS | TB4 |
| F14 | Migration job | PostgreSQL | DDL, grants, RLS policies | PostgreSQL over TLS | TB4 |
| F15 | Browser | Workstation | Exported file (C1, synthetic, labelled) | File download | TB7 |
| F16 | GitHub Actions | Artefacts | Binaries, images, SBOM, scan reports | HTTPS | TB6 |
| F17 | Registries | GitHub Actions | Third-party packages, actions, base images (untrusted until scanned) | HTTPS | TB6 |
| F18 | Artefacts | Runtime | Signed images and bundles | HTTPS | TB6 |

## Rules for evolving this diagram

- A new external endpoint, a new public route on the BFF, a new database role, a WebSocket / SignalR channel,
  an external ORE or pricing engine, or a new file upload feature is a **new flow**: update this diagram,
  add the STRIDE rows in [threat-model.md](threat-model.md) and the ASVS rows in
  [asvs-l2-checklist.md](asvs-l2-checklist.md) in the same pull request.
- The API must never get a public endpoint: if a deployment needs one, TB3 becomes an internet-facing
  boundary and the threat model must be revised first.
