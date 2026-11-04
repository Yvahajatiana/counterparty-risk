# External sources: security requirements

Status: **v1.0** (issue #4). Applies to every worker that downloads data across **TB5**
(see [data-flow.md](data-flow.md)) and mitigates TM-TB5-01 to TM-TB5-11 of [threat-model.md](threat-model.md).
Owning tickets: #15 (GLEIF), #16 (ECB), #17 (BIS, EBA, FR Y-9C), #18 (SDR), #19 (scheduling and data quality).

## 1. Mandatory controls (all sources)

| # | Control | Implementation | Requirements | Abuse-case test |
| --- | --- | --- | --- | --- |
| X1 | HTTPS only, certificate validated | TLS 1.2+, default validation, no `ServerCertificateCustomValidationCallback`, no HTTP fallback | SEC-158, SEC-159 | Architecture test `Should_Not_Override_Certificate_Validation` |
| X2 | Host allowlist | One named / typed `HttpClient` per source, `BaseAddress` fixed, allowed hosts from validated options | SEC-166, SEC-167 | `Should_Reject_Redirect_To_Non_Allowlisted_Host` |
| X3 | No uncontrolled redirect | `AllowAutoRedirect = false`; a redirect is followed only if its target is HTTPS and in the same source allowlist, at most 3 hops | SEC-191 | Same test as X2 |
| X4 | URLs from responses revalidated (SSRF) | Any URL read from a source response (e.g. GLEIF publishes endpoint) is checked for scheme, host and port before use | SEC-010 | `Should_Reject_Download_Url_Outside_Allowlist` |
| X5 | Size limits | Per-source maximum download size, enforced while streaming; announced size (when published) must equal received size | SEC-049 | `Should_Reject_File_When_Size_Differs_From_Announced` |
| X6 | Archive limits | Maximum entry count (1 for GLEIF), maximum uncompressed size, maximum ratio, checked before and while decompressing; streaming only | SEC-051 | `Should_Reject_Zip_Exceeding_Uncompressed_Limit` |
| X7 | No path from archives | Entries are never extracted by name; temporary file names generated internally | SEC-053 | `Should_Reject_Zip_Entry_With_Path_Traversal` |
| X8 | Timeouts and resilience | Global timeout per run, per-request timeout, Polly retry with exponential backoff and jitter, circuit breaker, `CancellationToken` propagated | SEC-211 | - |
| X9 | Integrity | SHA-256 computed while streaming, stored in `meta.ingestion_run` with size, record count and publication date; same hash = run `Skipped`; record count reconciled with the previous run or the published count | SEC-151 | Idempotence test per adapter |
| X10 | Content type and schema | Magic bytes / content type checked; expected columns, types, encodings and code lists (ISO 3166, ISO 4217, LEI mod 97) validated **before** loading; reject rather than silently correct | SEC-050, SEC-018 | Schema tests per adapter |
| X11 | Safe XML | `XmlReader` only, `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`, depth / size limits | SEC-013 | `Should_Reject_Xml_With_Dtd` |
| X12 | Safe spreadsheets | XLSX read as values only: no formula evaluation, no macros (`.xlsx` only, `.xlsm` refused), no external link resolution | SEC-050 | Adapter test with a formula and an external link |
| X13 | Safe CSV | Maximum line and field length, forced encoding, strict delimiter; text starting with `=` `+` `-` `@` stored as-is but flagged for export neutralisation (#31) | SEC-009 | `Should_Escape_Formula_In_Csv_Export` (#31) |
| X14 | Quarantine | Download to a quarantine directory outside any served path; files deleted after processing or on failure; invalid files never reach `ref.*`, `market.*` or `bench.*` | SEC-052 | Failure-path test per adapter |
| X15 | No content execution | Nothing downloaded is executed, evaluated or loaded as code | SEC-008 | Review |
| X16 | Least privilege load | Load through `COPY` binary + parameterised `MERGE` with role `cr_writer`; no DDL from workers | SEC-006, SEC-164 | `CrReader_Should_Not_Insert_Update_Delete_Or_Alter` (#11) |
| X17 | Logging | Counters, hashes, status and source id only, never row content | SEC-202 | `Logs_Should_Not_Contain_Tokens_Or_Cookies` (#10) |
| X18 | Licence | Every source registered in `meta.data_source` with licence, attribution and redistribution flag; NR data excluded from exports | - | `Export_Should_Exclude_NonRedistributable_Sources` (#31) |

## 2. Per-source parameters

Hosts are the documented public endpoints of each publisher; final values live in validated configuration.
Limits are starting values, tuned after the first runs (#39) without being removed.

| Source | Allowed hosts | Format | Max download | Max uncompressed / entries | Integrity reference | Ticket |
| --- | --- | --- | --- | --- | --- | --- |
| GLEIF Golden Copy | `goldencopy.gleif.org` + storage host announced by the publishes API (allowlisted explicitly) | ZIP of CSV (XML fallback) | 1 GB | 10 GB, 1 entry, ratio <= 50 | Announced size, record count vs previous full file | #15 |
| ECB Data Portal | `data-api.ecb.europa.eu` | SDMX-CSV (SDMX-ML fallback) | 100 MB per request | n/a | Observation count per series, no modification of published values | #16 |
| BIS statistics | `stats.bis.org`, `data.bis.org` | SDMX-CSV, bulk ZIP | 500 MB | 2 GB, <= 10 entries | Keys validated against the DSD, record counts | #17 |
| EBA Pillar 3 Data Hub, Transparency | EBA domains, set by the #17 spike | CSV, XLSX | 200 MB | 1 GB, <= 50 entries | File hash per publication (republication detection) | #17 |
| FFIEC / Federal Reserve FR Y-9C | FFIEC / Federal Reserve domains, set by #17 | ZIP of delimited text | 500 MB | 2 GB, <= 20 entries | Record counts per schedule | #17 |
| CFTC / CME SDR | CFTC / CME public dissemination domains, set by #18 | CSV (ZIP) | 500 MB | 2 GB, <= 50 entries | Row counts, strict schema | #18 |

## 3. Adding a source

1. Register the source here (hosts, format, limits, integrity reference) and in
   [data-classification.md](data-classification.md) (class, licence, redistribution).
2. Add the STRIDE rows for the new flow in [threat-model.md](threat-model.md) if it differs from the
   patterns above (new protocol, authentication, upload, push instead of pull).
3. Reuse the shared ingestion building blocks (named client, quarantine, archive reader, safe parsers) and
   their tests; never write a new download or decompression path.
