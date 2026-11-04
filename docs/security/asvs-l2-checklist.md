# OWASP ASVS 5.0 Level 2 checklist

Status: **v1.0** (issue #4). Reference: **OWASP ASVS 5.0.0** (released May 2025), the current version of the
standard; 4.0.3 is superseded. Requirement identifiers below are ASVS 5.0 identifiers (`chapter.section.n`),
which differ from the 4.0.3 numbering still quoted in some older issue texts.

Related documents: [threat-model.md](threat-model.md) · [data-flow.md](data-flow.md) ·
[data-classification.md](data-classification.md) · [external-sources.md](external-sources.md).

## How to use this checklist

- Every Level 1 and Level 2 requirement of ASVS 5.0 is listed (246 rows). Chapter V17 (WebRTC) is out of
  scope: the application has no WebRTC feature.
- Applicable requirements get a stable project identifier `SEC-nnn`, an evidence (named test, tool or
  review) and an owning ticket. Non-applicable requirements have no `SEC` identifier and carry the
  justification in the Comment column.
- The ticket that owns a requirement implements it **and** adds its evidence. #38 replays the evidence
  before each release.
- A pull request that changes the scope of a requirement (new feature making a "No" applicable, new
  evidence) updates the row in the same pull request.
- Requirement summaries are short paraphrases; the normative text is the OWASP ASVS 5.0.0 standard
  (CC BY-SA 4.0).

## PR mini-checklist

This is the short list copied in [`.github/pull_request_template.md`](../../.github/pull_request_template.md).
Each item points to the full rows below.

| # | Check | Requirements |
| --- | --- | --- |
| 1 | Authorization decided server-side, deny by default, scope only from the token, IDOR returns 404 | SEC-102..108 |
| 2 | Inputs validated server-side against allowlists (axes, sort, enums, ranges) | SEC-018, SEC-019 |
| 3 | Parameterised SQL only, no concatenated SQL, RLS kept on new tables | SEC-006, SEC-108 |
| 4 | Output safely rendered: no `innerHTML` / `bypassSecurityTrust*`, Highcharts `useHTML: false` | SEC-003, SEC-027 |
| 5 | Exports: formula prefixes neutralised, row cap, export policy and audit | SEC-009, SEC-025 |
| 6 | Untrusted files: no DTD, size / zip limits, no path from archive entries | SEC-013, SEC-049..053 |
| 7 | Outbound HTTP: HTTPS, default certificate validation, host allowlist, no unchecked redirect | SEC-010, SEC-159, SEC-166, SEC-191 |
| 8 | Session and tokens: no token in the browser, `__Host-` cookie, `X-CSRF` on `/api/**` | SEC-031, SEC-038, SEC-116 |
| 9 | No secret in code, config, logs or images; options validated at startup | SEC-165, SEC-168 |
| 10 | Logs: no token, cookie, secret or individual amount; security events audited | SEC-202, SEC-204, SEC-205 |
| 11 | Errors: generic `ProblemDetails`, fail closed | SEC-210, SEC-212 |
| 12 | New flow, source, table or dependency: threat model, data classification and SBOM updated; no vulnerable package | SEC-162, SEC-175, SEC-187 |

## Summary

| Chapter | Applicable | Not applicable |
| --- | --- | --- |
| V1 Encoding and Sanitization | 14 | 13 |
| V2 Validation and Business Logic | 11 | 0 |
| V3 Web Frontend Security | 18 | 1 |
| V4 API and Web Service | 4 | 6 |
| V5 File Handling | 8 | 1 |
| V6 Authentication | 28 | 7 |
| V7 Session Management | 18 | 0 |
| V8 Authorization | 7 | 0 |
| V9 Self-contained Tokens | 7 | 0 |
| V10 OAuth and OIDC | 25 | 4 |
| V11 Cryptography | 13 | 1 |
| V12 Secure Communication | 8 | 1 |
| V13 Configuration | 13 | 0 |
| V14 Data Protection | 9 | 0 |
| V15 Secure Coding and Architecture | 13 | 0 |
| V16 Security Logging and Error Handling | 16 | 0 |
| **Total (246)** | **212** | **34** |

## Requirements

### V1 Encoding and Sanitization

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-001 | 1.1.1 | 2 | Decode input to canonical form only once, before validation | Yes | ASP.NET Core model binding decodes once; no manual re-decoding after validation | Review; ZAP (#38) | #31 |
| SEC-002 | 1.1.2 | 2 | Encode / escape as the last step before the interpreter | Yes | Angular templates, System.Text.Json, CSV / XLSX writers encode at output time | Review | #12, #31 |
| SEC-003 | 1.2.1 | 1 | Context-aware output encoding (HTML, attributes, headers) | Yes | Angular interpolation only; header values built server-side | Test `Should_Render_Counterparty_Name_As_Text` | #14, #31 |
| SEC-004 | 1.2.2 | 1 | Encode untrusted data in URLs, safe URL schemes only | Yes | Source URLs built from catalogue keys with `Uri.EscapeDataString`; front links are relative routes | Unit tests of URL builders (#16, #17) | #13, #16, #17 |
| SEC-005 | 1.2.3 | 1 | Encode when building JavaScript / JSON | Yes | System.Text.Json only, no hand-built JSON; Highcharts fed with typed data, `useHTML: false` | Review; lint | #14, #31 |
| SEC-006 | 1.2.4 | 1 | Parameterised queries / ORM against SQL injection | Yes | EF Core LINQ, interpolated `FromSql`, parameterised Dapper, binary COPY; never concatenated SQL | CodeQL (#8); test `Should_Return400_When_Sort_Field_Is_Not_Allowlisted` | #11, #31 |
| - | 1.2.5 | 1 | No OS command injection | No | No OS process is started by the application (no `Process.Start`); an architecture test enforces it (#7) | - | - |
| - | 1.2.6 | 2 | No LDAP injection | No | No LDAP: Keycloak has no LDAP federation in V1 | - | - |
| - | 1.2.7 | 2 | No XPath injection | No | XML is read with a streaming `XmlReader`; no XPath | - | - |
| - | 1.2.8 | 2 | Secure LaTeX processors | No | No LaTeX processing | - | - |
| SEC-007 | 1.2.9 | 2 | Escape regex metacharacters from input | Yes | User input never builds a regex; constant `[GeneratedRegex]` patterns with timeouts (ReDoS) | Review | #7, #31 |
| - | 1.3.1 | 1 | Sanitise WYSIWYG HTML input | No | No rich-text input anywhere | - | - |
| SEC-008 | 1.3.2 | 1 | No `eval()` or dynamic code execution | Yes | No `eval` / `new Function` (CSP without `unsafe-eval`, Trusted Types); no runtime C# compilation from input | ESLint `no-eval`; CSP test (#10) | #10, #12 |
| SEC-009 | 1.3.3 | 2 | Sanitise data sent to dangerous contexts | Yes | Export pipeline neutralises formula prefixes; length limits on all strings | Test `Should_Escape_Formula_In_Csv_Export` | #31 |
| - | 1.3.4 | 2 | Sanitise user-supplied SVG | No | No upload; charts are SVG generated by Highcharts from typed numbers | - | - |
| - | 1.3.5 | 2 | Sanitise user-supplied template / Markdown / CSS | No | No user-authored markup or templates | - | - |
| SEC-010 | 1.3.6 | 2 | SSRF protection with allowlists | Yes | Workers only; every URL (including URLs returned by a source API) checked against the per-source allowlist | Test `Should_Reject_Download_Url_Outside_Allowlist` | #15, #16, #17 |
| - | 1.3.7 | 2 | No template injection | No | No server-side templates built from input; `index.html` nonce injection uses a fixed template | - | - |
| - | 1.3.8 | 2 | No JNDI injection | No | .NET stack, no JNDI | - | - |
| - | 1.3.9 | 2 | Sanitise memcache content | No | No memcache; `HybridCache` keys are hashes computed server-side | - | - |
| SEC-011 | 1.3.10 | 2 | Sanitise format strings | Yes | Logging message templates are constants (analyzer CA2254); no `string.Format` with user-controlled formats | Analyzers in CI (#8) | #7 |
| - | 1.3.11 | 2 | No SMTP / IMAP injection | No | The application sends no e-mail (Keycloak SMTP disabled in V1) | - | - |
| - | 1.4.1 | 2 | Memory-safe string and buffer handling | No | Managed .NET and TypeScript; `AllowUnsafeBlocks=false` enforced by the build (#7) | - | - |
| SEC-012 | 1.4.2 | 2 | Prevent integer overflows (sign, range checks) | Yes | Amounts are `decimal` / `numeric(28,8)`, `Money` rejects out-of-range values; counters validated | Unit tests on `Money` (#5, #43) | #5, #43 |
| - | 1.4.3 | 2 | Release memory, no dangling pointers | No | Managed runtime with garbage collection; no unsafe code | - | - |
| SEC-013 | 1.5.1 | 1 | Restrictive XML parser configuration (no XXE) | Yes | `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`, streaming | Test `Should_Reject_Xml_With_Dtd` | #15, #16, #17 |
| SEC-014 | 1.5.2 | 2 | Safe deserialisation of untrusted data | Yes | System.Text.Json with concrete types, no type names from input, no `BinaryFormatter` | Analyzers; review | #7 |

### V2 Validation and Business Logic

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-015 | 2.1.1 | 1 | Document input validation rules | Yes | OpenAPI v0 schemas (`maxLength`, `pattern`, `enum`, `maxItems`) and per-source file schemas | Review; Spectral / oasdiff (#31) | #5, #31, #15 |
| SEC-016 | 2.1.2 | 2 | Document combined-data consistency rules | Yes | Domain invariants and data quality rules (`DQ-*`) | Review | #19, #43 |
| SEC-017 | 2.1.3 | 2 | Document business limits (per user and global) | Yes | Rate limits, export caps, page sizes, job limits documented in #10, #31 and this checklist | Review | #10, #31 |
| SEC-018 | 2.2.1 | 1 | Positive validation of all input | Yes | Allowlisted axes and values, enums, ranges; unknown values -> 400 | Test `Should_Return400_When_Axis_Value_Is_Unknown` | #31 |
| SEC-019 | 2.2.2 | 1 | Validation enforced at a trusted service layer | Yes | `ValidationFilter` and handlers in the API; front-end validation is UX only | Integration tests (#31) | #31 |
| SEC-020 | 2.2.3 | 2 | Combinations of related data items are reasonable | Yes | E.g. `compareTo` before `asOf`, aggregation guard between CCR and financing natures (400) | Integration tests | #31, #45 |
| SEC-021 | 2.3.1 | 1 | Business flows processed in order, no skipped steps | Yes | OIDC login (state, nonce, PKCE); limit changes follow maker -> checker: a pending change is applied only after approval by another `sub` and never feeds W4 before (rule R4) | Tests `Should_Reject_Callback_With_Invalid_State`, `Should_Return403_When_Maker_Approves_Own_Limit_Change`, `Should_Not_Use_Pending_Limit_Change_In_W4_Utilisation` | #9, #27 |
| SEC-022 | 2.3.2 | 2 | Business limits implemented as documented | Yes | Same limits as 2.1.3, read from validated options | Tests 429 / 413 (#10, #31) | #10, #31 |
| SEC-023 | 2.3.3 | 2 | Business operations are transactional | Yes | Atomic as-of publication (`published_run_id` swap), ingestion in transactions, audit in the same transaction as sensitive actions | Integration tests | #10, #15, #31 |
| SEC-024 | 2.3.4 | 2 | Locking for limited resources | Yes | One concurrent export per user, `[DisallowConcurrentExecution]` jobs, advisory lock for the audit chain | Integration tests | #10, #19, #31 |
| SEC-025 | 2.4.1 | 2 | Anti-automation against excessive calls | Yes | Rate limiting per `sub` (`query`, `export`, `login`), concurrency limiter | Test `Should_Return429_When_Export_Rate_Is_Exceeded` | #10 |

### V3 Web Frontend Security

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-026 | 3.2.1 | 1 | Prevent rendering in an incorrect context | Yes | JSON with `nosniff`, exports with `Content-Disposition: attachment`, CSP | ZAP (#38) | #10, #31 |
| SEC-027 | 3.2.2 | 1 | Safe rendering functions for text | Yes | Angular interpolation, AG Grid text renderers, no `bypassSecurityTrust*` (lint) | Test `Should_Render_Counterparty_Name_As_Text` | #14 |
| SEC-028 | 3.3.1 | 1 | Cookies `Secure` with `__Host-` / `__Secure-` prefix | Yes | Session cookie `__Host-ccr`, `Secure` | Test on `Set-Cookie` (#9) | #9 |
| SEC-029 | 3.3.2 | 2 | `SameSite` set according to purpose | Yes | `Strict` for the session cookie; OIDC correlation / nonce cookies `Lax` (handler-managed, short-lived) | Test on `Set-Cookie` | #9 |
| SEC-030 | 3.3.3 | 2 | `__Host-` prefix unless shared with other hosts | Yes | Session cookie `__Host-ccr`; OIDC handler cookies are documented exceptions | Test on `Set-Cookie` | #9 |
| SEC-031 | 3.3.4 | 2 | `HttpOnly` for session cookies, set only via `Set-Cookie` | Yes | `HttpOnly`; no token in any response body | Test `Should_Not_Expose_Any_Token_To_The_Browser` | #9 |
| SEC-032 | 3.4.1 | 1 | HSTS, max-age >= 1 year, includeSubDomains | Yes | `max-age=31536000; includeSubDomains` outside `Development` | Header test; ZAP | #10 |
| SEC-033 | 3.4.2 | 1 | CORS origin fixed or allowlisted | Yes | CORS disabled: SPA, BFF and API share one origin | Test: no `Access-Control-Allow-Origin` returned | #9, #10 |
| SEC-034 | 3.4.3 | 2 | Content-Security-Policy with object-src / base-uri 'none' and nonces | Yes | Per-request nonce CSP + Trusted Types; report-only first, then enforced | Test `Should_Block_Inline_Script_With_Csp` | #10, #12 |
| SEC-035 | 3.4.4 | 2 | `X-Content-Type-Options: nosniff` on all responses | Yes | Security headers middleware on BFF and API | Header test; ZAP | #10 |
| SEC-036 | 3.4.5 | 2 | Referrer policy | Yes | `Referrer-Policy: no-referrer` | Header test | #10 |
| SEC-037 | 3.4.6 | 2 | CSP `frame-ancestors` on every response | Yes | `frame-ancestors 'none'` (+ `X-Frame-Options: DENY`) | Header test; ZAP | #10 |
| SEC-038 | 3.5.1 | 1 | CSRF: anti-forgery token or non-safelisted header | Yes | `X-CSRF: 1` required on every `/api/**` call; antiforgery token on logout | Test `Should_Return403_When_Api_Call_Lacks_Csrf_Header` | #9 |
| SEC-039 | 3.5.2 | 1 | No sensitive call possible without CORS preflight | Yes | The custom header forces a preflight, which fails because CORS is disabled | Same test as 3.5.1 | #9 |
| SEC-040 | 3.5.3 | 1 | Sensitive functionality not reachable with safe methods | Yes | State changes use POST / PUT (logout, thresholds); GET exports also require `X-CSRF`, which navigations and resource loads cannot send | Integration tests (#9, #31) | #9, #27, #31 |
| SEC-041 | 3.5.4 | 2 | Separate applications on separate hostnames | Yes | Keycloak on its own hostname; the dashboard on a single origin | Review (deployment, #41) | #9, #41 |
| - | 3.5.5 | 2 | Validate `postMessage` origin and syntax | No | No `postMessage` usage (no silent-renew iframe thanks to the BFF) | - | - |
| SEC-042 | 3.7.1 | 2 | Only supported, secure client-side technologies | Yes | Supported Angular version, evergreen browsers, Dependabot | Review | #12 |
| SEC-043 | 3.7.2 | 2 | Automatic redirects only to allowlisted hosts | Yes | Redirects only to the configured issuer and to local return URLs | Test `Should_Reject_External_ReturnUrl` | #9 |

### V4 API and Web Service

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-044 | 4.1.1 | 1 | Correct `Content-Type` with charset | Yes | `application/json; charset=utf-8`, `text/csv; charset=utf-8`, XLSX media type | ZAP; integration tests | #10, #31 |
| SEC-045 | 4.1.2 | 2 | HTTP to HTTPS redirect only for user-facing endpoints | Yes | HTTPS redirection for the SPA only; `/api/**` never redirects (plain HTTP not served outside local) | Review; ZAP | #10 |
| SEC-046 | 4.1.3 | 2 | Intermediary headers cannot be overridden by the user | Yes | YARP drops client `Authorization`, `Cookie`, `X-Forwarded-*`; `KnownProxies` only | Test `Should_Strip_Client_Authorization_Header_Before_Proxying` | #9, #10 |
| SEC-047 | 4.2.1 | 2 | Consistent HTTP message boundaries (no request smuggling) | Yes | Kestrel and YARP (RFC-compliant parsing); reverse proxy configuration reviewed | Review; ZAP (#38) | #38, #41 |
| - | 4.3.1 | 2 | GraphQL query limits | No | No GraphQL (REST only) | - | - |
| - | 4.3.2 | 2 | GraphQL introspection disabled | No | No GraphQL | - | - |
| - | 4.4.1 | 1 | WebSocket over TLS | No | No WebSocket / SignalR in V1; adding one is a new flow (threat model review) | - | - |
| - | 4.4.2 | 2 | WebSocket Origin check | No | No WebSocket in V1 | - | - |
| - | 4.4.3 | 2 | Dedicated WebSocket session tokens | No | No WebSocket in V1 | - | - |
| - | 4.4.4 | 2 | WebSocket tokens bound to the HTTPS session | No | No WebSocket in V1 | - | - |

### V5 File Handling

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-048 | 5.1.1 | 2 | Document file types, sizes, unpacked sizes and handling | Yes | No user upload; applied to ingested files in [external-sources.md](external-sources.md) | Review | #4, #15, #16, #17, #18 |
| SEC-049 | 5.2.1 | 1 | Accept only files of a processable size | Yes | Download size caps per source, streaming parse | Test `Should_Reject_File_When_Size_Differs_From_Announced` | #15, #16, #17, #18 |
| SEC-050 | 5.2.2 | 1 | Check extension and content (magic bytes) | Yes | ZIP signature, CSV header row, OOXML content types checked before parsing | Unit tests per adapter | #15, #17, #18 |
| SEC-051 | 5.2.3 | 2 | Check uncompressed size and file count before decompressing | Yes | Uncompressed size, ratio and entry count caps | Test `Should_Reject_Zip_Exceeding_Uncompressed_Limit` | #15, #17 |
| SEC-052 | 5.3.1 | 1 | Stored untrusted files never executed | Yes | Quarantine directory outside any served path, deleted after processing | Review | #15, #17 |
| SEC-053 | 5.3.2 | 1 | File paths from internal data, not user filenames | Yes | Temporary names generated internally; archive entry names never used as paths | Test `Should_Reject_Zip_Entry_With_Path_Traversal` | #15, #17, #18 |
| SEC-054 | 5.4.1 | 2 | Ignore user filenames, set `Content-Disposition` filename | Yes | Export file names built server-side | Integration test (#31) | #31 |
| SEC-055 | 5.4.2 | 2 | Encode file names in headers (RFC 6266) | Yes | ASCII-only server-built names | Integration test (#31) | #31 |
| - | 5.4.3 | 2 | Antivirus scan of files from untrusted sources | No | External files are parsed as data, never served to users or stored for download, then deleted; exports are generated by the application | - | - |

### V6 Authentication

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-056 | 6.1.1 | 1 | Document anti brute-force / credential stuffing controls | Yes | Keycloak brute force detection (temporary lockout to avoid malicious permanent lockout) + `login` rate limit on the BFF (this document, #9, #10) | Review | #9, #10 |
| SEC-057 | 6.1.2 | 2 | Document a context-specific password deny list | Yes | Deny list with project words (`ccr`, `counterparty`, `risk`, role names...) shipped with the realm | Review | #9 |
| SEC-058 | 6.1.3 | 2 | Document all authentication pathways | Yes | Single pathway: Keycloak OIDC through the BFF; no API key, no direct grant | Review | #9 |
| SEC-059 | 6.2.1 | 1 | Password length >= 8 (15 recommended) | Yes | Keycloak policy `length(15)` | Realm test | #9 |
| SEC-060 | 6.2.2 | 1 | Users can change their password | Yes | Keycloak account console | Review | #9 |
| SEC-061 | 6.2.3 | 1 | Password change requires current password | Yes | Keycloak account console (re-authentication) | Review | #9 |
| SEC-062 | 6.2.4 | 1 | Check against top 3000 passwords | Yes | Keycloak `passwordBlacklist` policy | Realm test | #9 |
| SEC-063 | 6.2.5 | 1 | No composition rules | Yes | No upper / lower / digit / special character policies | Realm test | #9 |
| SEC-064 | 6.2.6 | 1 | Password fields use `type=password` | Yes | Keycloak login theme | Review | #9 |
| SEC-065 | 6.2.7 | 1 | Paste and password managers allowed | Yes | Keycloak default theme, not overridden | Review | #9 |
| SEC-066 | 6.2.8 | 1 | Password verified exactly as received | Yes | Keycloak behaviour (no truncation, no case change) | Review | #9 |
| SEC-067 | 6.2.9 | 2 | Passwords of 64+ characters allowed | Yes | `maxLength(128)` | Realm test | #9 |
| SEC-068 | 6.2.10 | 2 | No periodic rotation | Yes | No `forceExpiredPasswordChange` | Realm test | #9 |
| SEC-069 | 6.2.11 | 2 | Context-specific deny list applied | Yes | Same deny list as 6.1.2 | Realm test | #9 |
| SEC-070 | 6.2.12 | 2 | Check against breached passwords | Yes | Breached-password list loaded in the deny list policy (offline, see RR-10) | Realm test | #9 |
| SEC-071 | 6.3.1 | 1 | Brute force controls implemented | Yes | Brute force detection enabled even in development | Test `Should_Lock_Account_Temporarily_After_Repeated_Failures` | #9 |
| SEC-072 | 6.3.2 | 1 | No default accounts | Yes | No default user in realm `ccr`; bootstrap admin password from a secret parameter | Realm test; startup validation (#10) | #9, #10 |
| SEC-073 | 6.3.3 | 2 | Multi-factor authentication | Yes | TOTP required action for every user outside `Development` (WebAuthn optional) | Realm test; E2E (#40) | #9 |
| SEC-074 | 6.3.4 | 2 | No undocumented authentication pathways | Yes | Implicit, direct access grants and service accounts disabled for `ccr-bff`; every endpoint has a policy | Tests `Realm_Should_Only_Enable_Code_Flow_With_Pkce`, `Every_Endpoint_Should_Have_A_Named_Policy` | #9 |
| SEC-075 | 6.4.1 | 1 | Initial passwords random, short-lived, not long-term | Yes | Test users get generated passwords; real users get a temporary password with `UPDATE_PASSWORD` | Review | #9 |
| SEC-076 | 6.4.2 | 1 | No password hints or secret questions | Yes | Not configured in Keycloak | Review | #9 |
| SEC-077 | 6.4.3 | 2 | Password reset does not bypass MFA | Yes | Self-service reset disabled in V1 (no SMTP); admin reset keeps the OTP requirement | Review | #9 |
| SEC-078 | 6.4.4 | 2 | Identity proofing when an MFA factor is lost | Yes | OTP reset by an administrator after identity verification (runbook) | Review | #9, #41 |
| SEC-079 | 6.5.1 | 2 | OTP usable only once | Yes | Keycloak TOTP policy (no code reuse) | Realm test | #9 |
| - | 6.5.2 | 2 | Hash low-entropy lookup secrets | No | No recovery codes / lookup secrets in V1 | - | - |
| SEC-080 | 6.5.3 | 2 | CSPRNG for OTP seeds | Yes | Keycloak TOTP secret generation | Review | #9 |
| - | 6.5.4 | 2 | Entropy of lookup secrets and OOB codes | No | No lookup secrets or out-of-band codes | - | - |
| SEC-081 | 6.5.5 | 2 | OOB lifetime <= 10 min, TOTP <= 30 s | Yes | TOTP period 30 s | Realm test | #9 |
| - | 6.6.1 | 2 | PSTN / SMS OTP restrictions | No | No SMS or phone factor | - | - |
| - | 6.6.2 | 2 | OOB requests bound to the original request | No | No out-of-band authentication | - | - |
| - | 6.6.3 | 2 | Rate limit code-based OOB | No | No out-of-band authentication | - | - |
| - | 6.8.1 | 2 | No spoofing across multiple IdPs | No | Single IdP (Keycloak), no identity brokering in V1 | - | - |
| SEC-082 | 6.8.2 | 2 | Signatures of authentication assertions validated | Yes | ID token and access token signatures validated (OIDC handler, `JwtBearer`) | Test `Should_Return401_When_Token_Is_Signed_By_Another_Key` | #9 |
| - | 6.8.3 | 2 | SAML assertions processed once | No | No SAML | - | - |
| SEC-083 | 6.8.4 | 2 | Verify authentication strength (acr / amr / auth_time) | Yes | BFF checks `acr` for MFA; documented fallback: missing `acr` = single factor, access denied outside `Development` | Integration test (#9) | #9 |

### V7 Session Management

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-084 | 7.1.1 | 2 | Document idle and absolute session timeouts | Yes | Idle 30 min, absolute 10 h, compatible with NIST SP 800-63B AAL2 | Review | #9 |
| SEC-085 | 7.1.2 | 2 | Document concurrent sessions policy | Yes | At most 3 concurrent sessions per user; the oldest is terminated (Keycloak session limits) | Realm test | #9 |
| SEC-086 | 7.1.3 | 2 | Document federated session coordination | Yes | BFF session never outlives the Keycloak SSO session; back-channel logout; failed refresh ends the session | Review | #9 |
| SEC-087 | 7.2.1 | 1 | Session tokens verified by a trusted backend | Yes | Ticket lookup in the BFF; JWT validation in the API | Integration tests | #9 |
| SEC-088 | 7.2.2 | 1 | Dynamically generated session tokens | Yes | Server-side tickets, short-lived access tokens; no static API keys | Review | #9 |
| SEC-089 | 7.2.3 | 1 | Reference tokens: CSPRNG, >= 128 bits | Yes | Ticket key 256 bits from `RandomNumberGenerator`, protected by Data Protection | Unit test | #9 |
| SEC-090 | 7.2.4 | 1 | New session token on (re)authentication | Yes | New ticket and cookie at every login, previous ticket removed | Test `Should_Issue_New_Session_Cookie_On_Login` | #9 |
| SEC-091 | 7.3.1 | 2 | Inactivity timeout enforced | Yes | 30 min sliding expiration | Integration test | #9 |
| SEC-092 | 7.3.2 | 2 | Absolute maximum lifetime enforced | Yes | 10 h (SSO session max) | Integration test | #9 |
| SEC-093 | 7.4.1 | 1 | Logout / expiry invalidates the session server-side | Yes | Ticket removed from the store | Test `Should_Return401_When_Cookie_Is_Replayed_After_Logout` | #9 |
| SEC-094 | 7.4.2 | 1 | Disabled / deleted account terminates all sessions | Yes | Keycloak back-channel logout; refresh fails within 5 min at most | Integration test | #9 |
| SEC-095 | 7.4.3 | 2 | Option to terminate other sessions after credential change | Yes | Keycloak "sign out other devices" on password / OTP update | Review | #9 |
| SEC-096 | 7.4.4 | 2 | Visible logout on authenticated pages | Yes | Logout in the shell header | E2E (#40) | #13 |
| SEC-097 | 7.4.5 | 2 | Administrators can terminate sessions | Yes | Keycloak admin console, propagated by back-channel logout | Review | #9 |
| SEC-098 | 7.5.1 | 2 | Re-authentication before changing sensitive account attributes | Yes | Keycloak account console with re-authentication (max age) | Review | #9 |
| SEC-099 | 7.5.2 | 2 | Users can view and terminate their sessions | Yes | Keycloak account console (device activity) | Review | #9 |
| SEC-100 | 7.6.1 | 2 | RP / IdP session lifetimes behave as documented | Yes | Same settings as 7.1.3, verified end to end | Integration test | #9 |
| SEC-101 | 7.6.2 | 2 | Session creation requires an explicit user action | Yes | Login triggered by the user opening the app; no hidden `prompt=none` session creation | Review | #9, #13 |

### V8 Authorization

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-102 | 8.1.1 | 1 | Document function- and data-level authorization rules | Yes | Authorization matrix, incompatible role pairs and default data scopes in [threat-model.md](threat-model.md) section 7 (validated by the Expert CCR, PR #48), `Policies` class | Review; business review on any change | #9 |
| SEC-103 | 8.1.2 | 2 | Document field-level authorization rules | Yes | Rules R1-R7 (threat model 7.2): capital tiles of W1 and the W10 large-exposure ratio need `ReadCapital`, W12 series inherit the indicator capability, allocated measures for business-line scopes; no internal fields | Review | #9, #31 |
| SEC-104 | 8.2.1 | 1 | Function-level access restricted | Yes | Capability policies (`ReadExposure`, `ReadLimits`, `ReadCva`, `ReadRiskOversight`, `ReadCapital`, `ReadMarketAndBenchmark`, `Export` + widget read capability, `ManageLimitThresholds`, `ManageLimits`, `ApproveLimitChange`, `ReadAudit`), `FallbackPolicy` | Tests `Should_Return403_When_FrontOffice_Calls_Capital_Endpoint`, `Should_Return403_When_FrontOffice_Calls_ManageLimits`, `Should_Return403_When_Exporting_W6_Without_ReadCapital`, `Every_Endpoint_Should_Have_A_Named_Policy` | #9, #27, #31 |
| SEC-105 | 8.2.2 | 1 | Data-level access restricted (IDOR / BOLA) | Yes | `AuthorizationBehavior` + RLS | Tests `Should_Return403_When_User_Requests_Other_LegalEntity`, `Should_Return404_When_NettingSet_Is_Out_Of_Scope` | #9, #11, #31 |
| SEC-106 | 8.2.3 | 2 | Field-level access restricted (BOPLA) | Yes | Dedicated DTOs per endpoint; fields governed by rules R1-R3 are not returned (not just hidden) without the capability | Tests `Should_Not_Return_Rwa_Tile_In_W1_For_FrontOffice`, `Should_Not_Return_LargeExposure_Ratio_Without_Full_Entity_Scope`; contract test | #31 |
| SEC-107 | 8.3.1 | 1 | Authorization enforced at a trusted service layer | Yes | API handlers and PostgreSQL RLS; never in the SPA | Integration tests | #9, #11 |
| SEC-108 | 8.4.1 | 2 | Cross-tenant isolation | Yes | Legal entities act as tenants: RLS `FORCE` + `WITH CHECK` | Tests `Should_Return_Zero_Rows_When_Scope_Is_Not_Set`, `All_Tables_With_LegalEntityId_Should_Have_Forced_Rls` | #11, #43 |

### V9 Self-contained Tokens

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-109 | 9.1.1 | 1 | Self-contained tokens validated by signature | Yes | `JwtBearer` signature validation | Test `Should_Return401_When_Token_Is_Signed_By_Another_Key` | #9 |
| SEC-110 | 9.1.2 | 1 | Algorithm allowlist, no `none` | Yes | `PS256` only (RSA-PSS); RS256 is disallowed by ASVS 5.0 Appendix C | Test with an RS256 / `none` token | #9 |
| SEC-111 | 9.1.3 | 1 | Keys only from trusted pre-configured sources | Yes | Keys from the configured issuer metadata; `jku`, `x5u`, `jwk` headers ignored | Review | #9 |
| SEC-112 | 9.2.1 | 1 | `nbf` / `exp` checked | Yes | Lifetime validation, clock skew <= 30 s | Test `Should_Return401_When_Token_Is_Expired` | #9 |
| SEC-113 | 9.2.2 | 2 | Token type checked for its purpose | Yes | API accepts access tokens only (`typ` checked); ID tokens rejected | Test `Should_Return401_When_Id_Token_Is_Presented` | #9 |
| SEC-114 | 9.2.3 | 2 | Audience validated | Yes | `aud` must contain `ccr-api` | Test `Should_Return401_When_Audience_Is_Not_CcrApi` | #9 |
| SEC-115 | 9.2.4 | 2 | Issuer restricts audiences when sharing keys | Yes | Keycloak audience mapper adds `ccr-api` only for `ccr-bff` | Realm test | #9 |

### V10 OAuth and OIDC

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-116 | 10.1.1 | 2 | Tokens only sent to components that need them (BFF) | Yes | Tokens stay in the BFF ticket store; only the access token goes to the API | Test `Should_Not_Expose_Any_Token_To_The_Browser` | #9 |
| SEC-117 | 10.1.2 | 2 | Code / ID token accepted only for the initiating session (PKCE, state, nonce) | Yes | ASP.NET Core OIDC handler with PKCE, `state`, `nonce` bound to correlation cookies | Test `Should_Reject_Callback_With_Invalid_State` | #9 |
| SEC-118 | 10.2.1 | 2 | CSRF protection of the code flow (PKCE or state) | Yes | PKCE S256 and `state` | Same test as 10.1.2 | #9 |
| - | 10.2.2 | 2 | Mix-up attack defence with several authorization servers | No | Single authorization server | - | - |
| SEC-119 | 10.3.1 | 2 | Resource server checks the audience | Yes | See 9.2.3 | Test `Should_Return401_When_Audience_Is_Not_CcrApi` | #9 |
| SEC-120 | 10.3.2 | 2 | Authorization decisions based on token claims | Yes | Roles, `ccr_legal_entities`, `ccr_business_lines`, `sub` build `UserScope`; tokens carrying an incompatible role pair (threat model 7.3) are refused; front-office wildcard scope refused | Tests `Should_Return403_When_Token_Has_Incompatible_Roles`, `Should_Return403_When_FrontOffice_Scope_Is_Wildcard`, `Realm_Should_Not_Assign_Incompatible_Roles` | #9 |
| SEC-121 | 10.3.3 | 2 | User identified by non-reassignable claims (iss + sub) | Yes | `iss` + `sub` used by audit and rate limiting | Review | #9, #10 |
| SEC-122 | 10.3.4 | 2 | Resource server checks authentication strength | Yes | API requires the MFA `acr` level for `Export` and `ManageLimitThresholds` (same fallback as 6.8.4) | Integration test | #9 |
| SEC-123 | 10.4.1 | 1 | Redirect URIs: exact match against an allowlist | Yes | Exact `redirectUris` and `post.logout.redirect.uris`, no wildcard | Realm test | #9 |
| SEC-124 | 10.4.2 | 1 | Authorization code single use | Yes | Keycloak behaviour | DAST (#38) | #9, #38 |
| SEC-125 | 10.4.3 | 1 | Authorization code lifetime <= 10 min | Yes | Keycloak access code lifespan 60 s | Realm test | #9 |
| SEC-126 | 10.4.4 | 1 | Only needed grants enabled; no implicit / password grant | Yes | `standardFlowEnabled` only | Test `Realm_Should_Only_Enable_Code_Flow_With_Pkce` | #9 |
| SEC-127 | 10.4.5 | 1 | Refresh token replay mitigation (rotation) | Yes | `revokeRefreshToken=true`, max reuse 0 (client is confidential, rotation kept anyway) | Realm test | #9 |
| SEC-128 | 10.4.6 | 2 | PKCE required, `plain` refused | Yes | `pkce.code.challenge.method=S256` on the client | Realm test | #9 |
| SEC-129 | 10.4.7 | 2 | Unauthenticated dynamic client registration mitigated | Yes | Anonymous client registration disabled (empty trusted hosts policy) | Realm test | #9 |
| SEC-130 | 10.4.8 | 2 | Refresh tokens have an absolute expiry | Yes | Bounded by SSO session max 10 h | Realm test | #9 |
| SEC-131 | 10.4.9 | 2 | Refresh tokens revocable by an authorized user | Yes | Keycloak admin and account consoles | Review | #9 |
| SEC-132 | 10.4.10 | 2 | Confidential client authenticated on back-channel | Yes | Client secret from a secret store (`private_key_jwt` recommended later) | Review | #9 |
| SEC-133 | 10.4.11 | 2 | Only required scopes assigned to the client | Yes | `openid`, `profile`; no `offline_access` | Realm test | #9 |
| SEC-134 | 10.5.1 | 2 | ID token replay mitigated (nonce) | Yes | Nonce validated by the OIDC handler | Review | #9 |
| SEC-135 | 10.5.2 | 2 | User identified by `sub` | Yes | `sub` from the ID token | Review | #9 |
| SEC-136 | 10.5.3 | 2 | Issuer metadata must match the configured issuer | Yes | Handler validates `issuer` against the configured authority | Review | #9 |
| SEC-137 | 10.5.4 | 2 | ID token `aud` equals `client_id` | Yes | Handler validation | Review | #9 |
| SEC-138 | 10.5.5 | 2 | Back-channel logout token validated (type, events, no nonce) | Yes | Logout token validated before revoking tickets | Test `Should_Reject_Backchannel_Logout_Token_With_Nonce` | #9 |
| SEC-139 | 10.6.1 | 2 | OP allows only safe response types | Yes | Implicit and hybrid flows disabled for `ccr-bff` | Realm test | #9 |
| SEC-140 | 10.6.2 | 2 | OP mitigates forced logout (id_token_hint) | Yes | RP-initiated logout sends `id_token_hint`; Keycloak asks for confirmation otherwise | Review | #9 |
| - | 10.7.1 | 2 | User consent to each authorization request | No | First-party, pre-registered confidential client: consent screen disabled by design | - | - |
| - | 10.7.2 | 2 | Clear consent information | No | No consent screen (first-party client) | - | - |
| - | 10.7.3 | 2 | Users can review and revoke consents | No | No consent granted (first-party client); sessions can still be revoked (7.5.2) | - | - |

### V11 Cryptography

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-141 | 11.1.1 | 2 | Documented key management policy and lifecycle | Yes | Cryptographic inventory and rotation rules in [data-classification.md](data-classification.md) | Review | #10, #41 |
| SEC-142 | 11.1.2 | 2 | Cryptographic inventory | Yes | Same inventory, updated by every PR that adds a key or an algorithm | Review | #4, #41 |
| SEC-143 | 11.2.1 | 2 | Industry-validated crypto implementations | Yes | .NET / OS crypto providers, Keycloak, PostgreSQL TLS; no custom crypto | Review | #7 |
| SEC-144 | 11.2.2 | 2 | Crypto agility | Yes | Algorithms configurable (Data Protection, Keycloak keys); algorithm id stored with audit hashes | Review | #9, #10 |
| SEC-145 | 11.2.3 | 2 | Minimum 128-bit security | Yes | Realm signing keys RSA 3072 bits (PS256) or P-256; AES-256 | Realm test | #9 |
| SEC-146 | 11.3.1 | 1 | No ECB or weak padding | Yes | No ECB; RSASSA-PKCS1-v1_5 (RS256) not used | Review | #9 |
| SEC-147 | 11.3.2 | 1 | Approved ciphers and modes (e.g. AES-GCM) | Yes | Data Protection configured with AES-256-GCM; TLS AEAD suites | Review | #9, #10 |
| SEC-148 | 11.3.3 | 2 | Authenticated encryption | Yes | AES-GCM (Data Protection), protected cursors | Review | #9, #31 |
| SEC-149 | 11.4.1 | 1 | Approved hash functions only | Yes | SHA-256 / HMAC-SHA-256; MD5 / SHA-1 forbidden (analyzers CA5350 / CA5351) | Analyzers (#8) | #7, #8 |
| SEC-150 | 11.4.2 | 2 | Passwords stored with an approved KDF | Yes | Keycloak password hashing (Argon2 or PBKDF2-SHA512 with current iterations) | Realm test | #9 |
| SEC-151 | 11.4.3 | 2 | Collision-resistant hashes >= 256 bits for integrity | Yes | SHA-256 for files and the audit chain | Unit tests | #10, #15 |
| - | 11.4.4 | 2 | Approved KDF when deriving keys from passwords | No | The application derives no key from a password | - | - |
| SEC-152 | 11.5.1 | 2 | CSPRNG with >= 128 bits for non-guessable values | Yes | Ticket keys, CSP nonces, Data Protection; `public_id` UUIDs are not secrets (authorization still checked) | Review | #9, #10, #31 |
| SEC-153 | 11.6.1 | 2 | Approved key generation and signature algorithms | Yes | Keycloak key providers, PS256 | Realm test | #9 |

### V12 Secure Communication

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-154 | 12.1.1 | 1 | Only TLS 1.2 / 1.3 | Yes | Kestrel / reverse proxy and outbound `HttpClient` limited to TLS 1.2+ | testssl / ZAP (#38) | #10, #41 |
| SEC-155 | 12.1.2 | 2 | Recommended cipher suites only | Yes | Reverse proxy profile (Mozilla intermediate or stricter) | testssl (#38) | #41 |
| - | 12.1.3 | 2 | mTLS client certificates validated | No | No mTLS in V1 | - | - |
| SEC-156 | 12.2.1 | 1 | TLS for all external-facing HTTP services | Yes | BFF and Keycloak HTTPS only | ZAP (#38) | #10, #41 |
| SEC-157 | 12.2.2 | 1 | Publicly trusted certificates externally | Yes | Deployment certificates from a public CA (local development certificate excepted) | Review | #41 |
| SEC-158 | 12.3.1 | 2 | Encrypted protocols for all connections, including DB | Yes | PostgreSQL `SSL Mode=VerifyFull`, HTTPS to sources and Keycloak | Integration test with TLS (#11) | #11, #15, #16, #17 |
| SEC-159 | 12.3.2 | 2 | TLS clients validate certificates | Yes | No `ServerCertificateCustomValidationCallback`, no `TrustServerCertificate` | Architecture test `Should_Not_Override_Certificate_Validation` | #7, #15 |
| SEC-160 | 12.3.3 | 2 | TLS between internal HTTP services | Yes | BFF to API over HTTPS outside local development (RR-12) | Review | #7, #9 |
| SEC-161 | 12.3.4 | 2 | Internal services use trusted certificates | Yes | Internal CA explicitly trusted, no blanket trust | Review | #41 |

### V13 Configuration

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-162 | 13.1.1 | 2 | Document all communication needs | Yes | [data-flow.md](data-flow.md) and [external-sources.md](external-sources.md) | Review | #4 |
| SEC-163 | 13.2.1 | 2 | Backend components authenticated without static credentials | Yes | API uses the user's access token; outside local, PostgreSQL with managed identity or vault-rotated credentials (RR-08) | Review | #10, #11, #41 |
| SEC-164 | 13.2.2 | 2 | Least privilege for backend communications | Yes | One DB role per component (`cr_reader`, `cr_writer`, `cr_migrator`, `cr_audit_writer`) | Test `CrReader_Should_Not_Insert_Update_Delete_Or_Alter` | #11 |
| SEC-165 | 13.2.3 | 2 | No default credentials for services | Yes | Startup validation rejects empty or demo values | Test `Should_Refuse_To_Start_When_Secret_Is_Missing_Or_Default` | #10 |
| SEC-166 | 13.2.4 | 2 | Allowlist of external resources | Yes | Per-source host allowlist | Test `Should_Reject_Redirect_To_Non_Allowlisted_Host` | #15, #16, #17, #18 |
| SEC-167 | 13.2.5 | 2 | Server-level outbound allowlist | Yes | Named `HttpClient` per source + egress filtering in deployment | Review | #15, #41 |
| SEC-168 | 13.3.1 | 2 | Secrets in a secrets management solution | Yes | User-secrets locally, Aspire secret parameters, vault in deployment; nothing in code or images | gitleaks, TruffleHog; test `Appsettings_Should_Not_Contain_Secrets` | #10 |
| SEC-169 | 13.3.2 | 2 | Least privilege access to secrets | Yes | Each service reads only its own secrets (managed identity) | Review | #10, #41 |
| SEC-170 | 13.4.1 | 1 | No source control metadata deployed | Yes | Images built from publish output, `.dockerignore` | Trivy / review | #8 |
| SEC-171 | 13.4.2 | 2 | Debug modes disabled in production | Yes | `Production` environment, developer exception page only in `Development`, Angular production build | Review | #7, #12 |
| SEC-172 | 13.4.3 | 2 | No directory listing | Yes | No `UseDirectoryBrowser`; static files from `wwwroot` only | ZAP | #9 |
| SEC-173 | 13.4.4 | 2 | HTTP TRACE not supported | Yes | Not mapped by Kestrel / minimal APIs | ZAP | #38 |
| SEC-174 | 13.4.5 | 2 | Documentation and monitoring endpoints not exposed | Yes | OpenAPI UI only in `Development`; health and Aspire dashboard internal | ZAP; review | #7, #31 |

### V14 Data Protection

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-175 | 14.1.1 | 2 | Sensitive data identified and classified | Yes | [data-classification.md](data-classification.md) | Review | #4 |
| SEC-176 | 14.1.2 | 2 | Protection requirements per level documented | Yes | Same document: storage, logging, retention, encryption per class | Review | #4 |
| SEC-177 | 14.2.1 | 1 | No sensitive data in URLs | Yes | No token or secret in URLs; query strings carry only axis filters (C0 / C1) | ZAP; review | #9, #31 |
| SEC-178 | 14.2.2 | 2 | No sensitive data cached by server components | Yes | `HybridCache` partitioned by scope; no caching of `/api/**` by proxies | Review | #31 |
| SEC-179 | 14.2.3 | 2 | No sensitive data sent to untrusted parties | Yes | No analytics, trackers or external CDN; CSP `connect-src 'self'` | CSP test | #10, #12 |
| SEC-180 | 14.2.4 | 2 | Controls implemented per protection level | Yes | Checked in every PR (template) and in #38 | Review | #4, #38 |
| SEC-181 | 14.3.1 | 1 | Authenticated data cleared from the client after the session | Yes | `Clear-Site-Data` on logout and in-memory state reset | E2E (#40) | #9, #13 |
| SEC-182 | 14.3.2 | 2 | Anti-caching headers | Yes | `Cache-Control: no-store` on `/api/**` and `/bff/user` | Header test | #10 |
| SEC-183 | 14.3.3 | 2 | No sensitive data in browser storage | Yes | Only UI preferences in `localStorage` | E2E (#40) | #13 |

### V15 Secure Coding and Architecture

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-184 | 15.1.1 | 1 | Remediation time frames for vulnerable components | Yes | Critical 7 days, High 30 days, Medium 90 days, others at next upgrade | Review | #8 |
| SEC-185 | 15.1.2 | 2 | SBOM of third-party components from trusted repositories | Yes | CycloneDX SBOM, package source mapping, npm registry only | CI artefact (#8) | #8, #12 |
| SEC-186 | 15.1.3 | 2 | Document resource-demanding functionality | Yes | Monte-Carlo (#22), history recomputation (#26), GLEIF ingestion (#15), exports (#31): see threat model TM-TB1-06, TM-TB3-08, TM-INT-01 | Review | #4 |
| SEC-187 | 15.2.1 | 1 | Components within remediation time frames | Yes | CI fails on High / Critical vulnerable packages | CI gate (#8) | #8 |
| SEC-188 | 15.2.2 | 2 | Defences against resource-demanding functionality | Yes | Jobs only, quotas, timeouts, rate limits, row caps | Tests 429 / 413, perf tests (#39) | #10, #22, #31, #39 |
| SEC-189 | 15.2.3 | 2 | No test code or extraneous functionality in production | Yes | No seed or test endpoints; generator runs as a job | Review | #7 |
| SEC-190 | 15.3.1 | 1 | Return only required fields | Yes | DTOs per endpoint | Contract test | #31 |
| SEC-191 | 15.3.2 | 2 | Do not follow redirects unless intended | Yes | `AllowAutoRedirect=false`, manual redirect revalidated | Test `Should_Reject_Redirect_To_Non_Allowlisted_Host` | #15, #16, #17 |
| SEC-192 | 15.3.3 | 2 | Mass assignment protection | Yes | Dedicated command DTOs for write endpoints (thresholds, limit change request, approval); status and checker never bound from the maker request | Integration test (#27) | #27 |
| SEC-193 | 15.3.4 | 2 | Original client IP from trusted fields only | Yes | `ForwardedHeaders` with `KnownProxies` | Integration test (#10) | #10 |
| SEC-194 | 15.3.5 | 2 | Strict typing and comparisons | Yes | C# nullable + strong types; TypeScript `strict`, `===` (lint) | Build / lint | #7, #12 |
| SEC-195 | 15.3.6 | 2 | No prototype pollution | Yes | No deep merge of untrusted JSON into objects; `Map` / `Set`; lint | Review | #12 |
| SEC-196 | 15.3.7 | 2 | No HTTP parameter pollution | Yes | Scalar parameters bound from one source; repeated scalar -> 400 | Test `Should_Return400_When_Scalar_Parameter_Is_Repeated` | #31 |

### V16 Security Logging and Error Handling

| SEC | ASVS | L | Requirement (summary) | Applicable | Comment | Evidence (test, tool, review) | Ticket |
| --- | --- | --- | --- | --- | --- | --- | --- |
| SEC-197 | 16.1.1 | 2 | Logging inventory | Yes | Logging inventory in [data-classification.md](data-classification.md) | Review | #10, #41 |
| SEC-198 | 16.2.1 | 2 | Log metadata: when, where, who, what | Yes | Timestamp, service, `sub`, action, `TraceId`; limit changes carry old value, new value, maker, checker | Unit test of the audit event | #10, #27 |
| SEC-199 | 16.2.2 | 2 | Synchronised clocks, UTC timestamps | Yes | UTC (`timestamptz`, ISO 8601 with offset) | Review | #10 |
| SEC-200 | 16.2.3 | 2 | Logs only to documented destinations | Yes | OpenTelemetry to the documented collector; audit to PostgreSQL | Review | #7, #10 |
| SEC-201 | 16.2.4 | 2 | Logs in a common, correlatable format | Yes | Structured JSON / OTLP with `TraceId` | Review | #7 |
| SEC-202 | 16.2.5 | 2 | Sensitive data logged according to its protection level | Yes | No token, cookie, secret or individual amount; hashes only | Test `Logs_Should_Not_Contain_Tokens_Or_Cookies` | #10 |
| SEC-203 | 16.3.1 | 2 | Authentication operations logged | Yes | Keycloak events (`LOGIN`, `LOGIN_ERROR`, `LOGOUT`) and BFF audit | Integration test | #9, #10 |
| SEC-204 | 16.3.2 | 2 | Failed authorization logged | Yes | 401 / 403 / 404 authorization refusals audited | Integration test | #10 |
| SEC-205 | 16.3.3 | 2 | Security events and bypass attempts logged | Yes | Validation failures, 429, CSRF rejections, CSP reports | Integration test | #10 |
| SEC-206 | 16.3.4 | 2 | Unexpected errors and security control failures logged | Yes | Unhandled exceptions, TLS failures to sources or DB | Review | #7, #10 |
| SEC-207 | 16.4.1 | 2 | Log injection prevented | Yes | Structured logging; CR / LF neutralised in values | Unit test | #7, #10 |
| SEC-208 | 16.4.2 | 2 | Logs protected from unauthorized access and modification | Yes | Audit append-only + hash chain; collector with RBAC | Test `Should_Detect_Broken_Audit_Chain_At_Exact_Sequence` | #10 |
| SEC-209 | 16.4.3 | 2 | Logs shipped to a separate system | Yes | OTLP collector separate from the application (local: Aspire dashboard) | Review | #10, #41 |
| SEC-210 | 16.5.1 | 2 | Generic error messages | Yes | `ProblemDetails` (RFC 9457) without stack trace outside `Development` | Test `Should_Return_Generic_ProblemDetails_Without_StackTrace` | #7, #10 |
| SEC-211 | 16.5.2 | 2 | Secure operation when external resources fail | Yes | Circuit breaker, last valid data kept with DQ status, last published as-of served | Integration test (#16) | #16, #19 |
| SEC-212 | 16.5.3 | 2 | Fail securely (no fail-open) | Yes | Deny by default, RLS fail-closed, audit fail-closed | Test `Should_Return403_And_Issue_No_Sql_When_Scope_Is_Empty` | #9, #10, #11 |
