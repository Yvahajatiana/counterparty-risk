# Data classification

Status: **v1.0** (issue #4). Implements ASVS 5.0 V14.1 (SEC-175, SEC-176), V11.1 (SEC-141, SEC-142) and
V16.1 (SEC-197).

**Rule**: every new table, schema, file, external source, log sink or key declares its class in this document
in the same pull request. A table without a class is not merged.

## 1. Classes

| Class | Name | Content | Examples |
| --- | --- | --- | --- |
| **C0** | Public | Open data published by third parties, reused under their licence | GLEIF LEI records, ECB FX rates and yield curves, BIS OTC statistics, EBA Pillar 3 / Transparency, FFIEC FR Y-9C, CFTC / CME SDR public dissemination |
| **C1** | Internal | Synthetic portfolio and everything computed from it, handled **as if it were confidential** so that access control, audit and export controls are exercised | Trades, netting sets, CSA, collateral, limits, financing facilities, exposures, EE / PFE, EAD, RWA, CVA, read models, method versions, data quality results |
| **C1-PD** | Internal, personal data | Personal data of the application's users (GDPR) | Keycloak accounts (username, first and last name, e-mail, role, scope attributes), Keycloak events, audit trail (`actor_sub`, roles, HMAC of the IP) |
| **C2** | Secret | Any value granting access or protecting other data | Client secret, database credentials, Keycloak admin credentials, Data Protection key ring, session tickets and OIDC tokens, audit IP HMAC key, TLS private keys |
| **R** | Restricted, outside the repository | Documents that must never be versioned | The requirements document (EDB, `docs/edb.md`), personal documents, any PDF |

**Licence attribute (orthogonal to the class)**: a C0 source can be **NR, non-redistributable** (DTCC,
CCP disclosures). NR data may be used for internal demonstration only: it is excluded from exports, from
versioned fixtures and from any public screenshot.

## 2. Handling rules per class

| Rule | C0 Public | C1 Internal | C1-PD Personal | C2 Secret | R Restricted |
| --- | --- | --- | --- | --- | --- |
| Allowed in the repository | Small extracts as test fixtures, with source and licence (not NR) | Synthetic fixtures only | No (synthetic test users only, e.g. `rm.markets.fr`) | **Never**, placeholders `<...>` or `${VAR}` only | **Never** (`.gitignore`, pre-commit hook, CI guard) |
| Storage | PostgreSQL `ref`, `market`, `bench`, `meta`; quarantine files deleted after load | PostgreSQL, protected by RLS per legal entity | Keycloak database; `audit` schema | User-secrets (local), Aspire secret parameters, vault; Data Protection keys and tickets in PostgreSQL, encrypted | Local workstation only |
| Encryption in transit | TLS | TLS | TLS | TLS | n/a |
| Encryption at rest | Storage encryption of the platform | Storage encryption of the platform | Storage encryption; IP stored as HMAC only | Vault encryption; Data Protection (AES-256-GCM) for keys and tickets | n/a |
| Access | All authenticated users (attribution displayed) | ABAC: capability + legal entity + business line, RLS | Keycloak administrators; audit read by `ccr-direction` / `ccr-auditor` | The owning service identity only | Owner only |
| Application logs | Counters, file hash, source id | Identifiers and counts only; **no individual amount** at level `Information` or above | `sub` only, never name or e-mail | **Never** (not even masked) | Never |
| Audit trail | Ingestion run (hash, counts, status) | Action, effective scope, allow-listed filters, row count, export hash; no amount | `iss` + `sub`, roles, HMAC of the IP | Never | Never |
| Export (CSV / XLSX) | Yes, with source and licence mention (not NR) | Yes, within the user's scope, labelled "synthetic data" | No | Never | Never |
| Browser | Yes | Displayed, never persisted in browser storage, `Cache-Control: no-store` | `/bff/user`: name, roles, scope only | Never (BFF pattern: no token in the browser) | Never |
| Retention | Source history as needed by W12 (13 months daily, 5 years monthly) | 13 months daily, 5 years monthly, purge by partition | Keycloak events 90 days; audit 5 years | Rotated (section 5); revoked on compromise | n/a |

## 3. Inventory: data stores

| Store / schema | Content | Class | Notes |
| --- | --- | --- | --- |
| `ref` | Legal entities, counterparties (GLEIF), groups, business lines, desks, countries, sectors | C0 (GLEIF) / C1 (bank structure) | LEIs and legal names are public; attachment to the synthetic bank is C1 |
| `portfolio` | Netting sets, CSA, trades, collateral | C1 | RLS `FORCE` |
| `financing` | Facilities, drawings, guarantees, collateral received | C1 | RLS `FORCE` + `WITH CHECK`, `lineage = Synthetic` |
| `market` | FX rates, yield curves (ECB) | C0 | "Source: ECB statistics." attribution |
| `bench` | BIS, EBA, FR Y-9C benchmark observations | C0 (some NR flags possible) | Published values kept separate from recomputed values |
| `calc` | Exposure, SA-CCR, profiles, RWA, CVA, allocation, financing results | C1 | RLS, `method_version_id` mandatory |
| `reporting` | Read models per widget | C1 | RLS / views in `security_invoker` |
| `meta` | Data sources and licences, ingestion runs, method versions, DQ results, events | C0 / C1 | No row content in ingestion logs |
| `staging` | Unlogged load tables | Same class as the source | Truncated after each run |
| `audit` | Audit events | C1-PD | INSERT-only, hash chain, monthly partitions, 5-year retention |
| BFF ticket store, Data Protection key ring | Authentication tickets with tokens, keys | C2 | Encrypted, BFF role only |
| Keycloak database | Accounts, credentials (hashed), sessions, events | C1-PD / C2 | Managed by Keycloak only |
| Quarantine directory | Raw downloaded files | C0, untrusted | Outside any served path, deleted after processing |

## 4. Inventory: external sources

Technical requirements per source are in [external-sources.md](external-sources.md).

| Source | Data | Class | Licence / redistribution |
| --- | --- | --- | --- |
| GLEIF Golden Copy | LEI level 1 and level 2 | C0 | CC0, redistributable |
| ECB Data Portal (SDMX) | FX reference rates, yield curves | C0 | Free reuse with "Source: ECB statistics.", series not modified |
| BIS statistics | OTC derivatives statistics | C0 | Cite BIS, no implied endorsement |
| EBA Pillar 3 Data Hub, Transparency exercise | CCR, CVA, RWA tables per institution | C0 | To be confirmed in #41 |
| FFIEC / Federal Reserve FR Y-9C | HC-L, HC-R schedules | C0 | To be confirmed in #41 |
| CFTC / CME SDR public dissemination | Anonymised trade records (input of the synthetic generator) | C0 | Per SDR terms; DTCC data is **NR** and not used as a generator source |
| CCP public disclosures | Clearing statistics (if used) | C0, **NR** | Internal demonstration only |

## 5. Cryptographic inventory and key lifecycle

| Key / secret | Purpose | Algorithm | Storage | Rotation | Owner |
| --- | --- | --- | --- | --- | --- |
| Keycloak realm signing keys | Sign ID / access / logout tokens | RSA-PSS **PS256**, 3072 bits (or ES256) | Keycloak database | Yearly, with overlap (old key kept for validation until tokens expire) | #9 |
| `ccr-bff` client credential | Authenticate the BFF to Keycloak | Client secret (`private_key_jwt` recommended later) | Vault / Aspire secret parameter | 90 days and on suspicion | #9 |
| Data Protection key ring (BFF, API) | Protect cookies, tickets, pagination cursors | AES-256-GCM | PostgreSQL, encrypted at rest | 90 days (framework default), automatic | #9, #31 |
| Audit IP HMAC key | Pseudonymise client IPs | HMAC-SHA-256, 256-bit key | Vault | Yearly; key id stored with each event | #10 |
| Audit hash chain | Tamper evidence | SHA-256 | n/a (no key) | Algorithm id stored for agility | #10 |
| File integrity hashes | Ingestion integrity | SHA-256 | `meta.ingestion_run` | n/a | #15-#17 |
| Database credentials | Service accounts `cr_*` | Password locally; managed identity or vault-rotated credentials elsewhere | User-secrets / vault | 90 days (static), per lease (dynamic) | #10, #11 |
| TLS certificates | BFF, Keycloak, PostgreSQL, internal services | ECDSA P-256 or RSA 3072+ | Platform certificate store | Before expiry, automated | #41 |

Keys are never shared between more than the services that need them, never logged, and replaced on any
suspicion of compromise (runbook in #41).

## 6. Logging inventory

| Layer | Events | Format | Destination | Access | Retention |
| --- | --- | --- | --- | --- | --- |
| BFF | Login / logout, CSRF rejections, 401 / 403, 429, CSP reports, proxy errors | Structured JSON / OTLP with `TraceId` | OpenTelemetry collector (local: Aspire dashboard) | Operations team | 90 days |
| API | Requests (route, status, duration), validation failures, authorization refusals, unhandled errors | Same | Same | Operations team | 90 days |
| API audit | Widget reads, exports, refusals, parameter changes | Rows in `audit.audit_event` | PostgreSQL `audit` schema | `cr_audit_reader` (direction, auditor) | 5 years |
| Workers | Job start / end, counts, file hash, DQ status, source errors | Structured JSON / OTLP | Collector | Operations team | 90 days |
| Keycloak | User events (login, errors, logout, credential updates), admin events | Keycloak event store | Keycloak database | Keycloak administrators | 90 days (admin events 1 year) |
| PostgreSQL | Connection failures, permission denied, slow statements (no parameters) | Server log | Platform log store | DBA | 90 days |

Never logged anywhere: passwords, tokens, cookies, client secrets, connection strings, Data Protection
material, file contents, individual amounts at level `Information` or above. Correlation uses the W3C
`TraceId`. Values are logged through structured templates (no string concatenation), which neutralises
log injection.
