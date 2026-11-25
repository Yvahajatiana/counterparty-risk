# BFF contract (session, anti-CSRF, API proxy)

Contract between the Angular shell (#13) and the BFF (`CounterpartyRisk.Bff`, #9). The browser never holds a
token: the BFF keeps the tokens in a server-side session and forwards `/api/**` to the internal Api with the
access token attached.

## Session cookie

| Cookie | Set by | Flags | Content |
| --- | --- | --- | --- |
| `__Host-ccr` | OIDC callback `/signin-oidc` | `HttpOnly; Secure; SameSite=Strict; Path=/`, no `Domain` | Opaque key (256 bits) of a server-side ticket. Never a JWT. |
| `__Host-ccr-af` | `GET /bff/user` | `HttpOnly; Secure; SameSite=Strict; Path=/` | Antiforgery cookie token (logout only). |
| `__Host-ccr-xsrf` | `GET /bff/user` | `Secure; SameSite=Strict; Path=/` (readable by JS) | Antiforgery request token, to echo in `X-XSRF-TOKEN` on `POST /bff/logout`. |
| `.AspNetCore.Correlation.*`, `.AspNetCore.OpenIdConnect.Nonce.*` | `/bff/login` | `HttpOnly; Secure; SameSite=Lax`, short-lived | OIDC handler cookies (documented exception to the `__Host-` prefix, SEC-030). |

Idle timeout 30 min (sliding), absolute lifetime 10 h, at most 3 concurrent sessions per user (the oldest is
revoked). The access token (5 min) is refreshed server side; a failed refresh ends the session.

## Endpoints

| Method and path | Auth | Required headers | Response |
| --- | --- | --- | --- |
| `GET /bff/login?returnUrl=/path` | anonymous | none | `302` to Keycloak (code flow + PKCE S256). `returnUrl` must be a local path (`/x`, never `//host`, `https://...`): otherwise `400`. Already signed in: `302` to `returnUrl`. Use a top-level navigation (`window.location.href`), never XHR. |
| `GET /bff/user` | session | `X-CSRF: 1` | `200` `{ "name", "roles": [], "legalEntities": [], "businessLines": [], "sessionExpiresAt" }` (`*` = all), `Cache-Control: no-store`; sets `__Host-ccr-xsrf`. `401` without session. |
| `POST /bff/logout` | session | `X-CSRF: 1` and `X-XSRF-TOKEN: <value of __Host-ccr-xsrf>` | `204` + `Clear-Site-Data: "cache", "cookies", "storage"`. The server-side ticket is removed and the Keycloak SSO session ended through the back channel. `403` without a valid antiforgery token. Then navigate to `/`. |
| `* /api/**` | session | `X-CSRF: 1` on **every** method, GET included | Proxied to the Api (`/api/v1/...`). `403` (ProblemDetails) without the header, before any proxying; `401` without session (never a redirect). Responses carry `Cache-Control: no-store`. |
| `POST /bff/backchannel-logout` | Keycloak only (signed logout token) | form `logout_token` | `200` / `400`. Not for the SPA. |

Errors are RFC 9457 `application/problem+json` with a stable `code` (for example `authorization.out_of_scope`,
`authorization.forbidden`, `authorization.empty_scope`, `resource.not_found`) and no internal detail.

## Rules for the SPA (#13)

- Send `X-CSRF: 1` on every call to `/api/**` and `/bff/*` (an HTTP interceptor). No CORS: the SPA, the BFF and
  the API share one origin.
- On `401` from `/api/**` or `/bff/user`: navigate to `/bff/login?returnUrl=<current path>`. Do not retry in a loop.
- On `403`: show "access denied" (the scope or the capability is missing); never hide it as "no data".
- Angular `withXsrfConfiguration({ cookieName: '__Host-ccr-xsrf', headerName: 'X-XSRF-TOKEN' })` covers the logout.
- Never store figures, profile or anything else in `localStorage` / `sessionStorage`.
- The profile is for display only: every authorization decision is taken by the API on the token.

## Api side (for #31)

- The Api accepts only access tokens of realm `ccr`: signature `PS256`, audience `ccr-api`, `azp = ccr-bff`,
  `typ = Bearer`, clock skew 30 s. Anything else is `401`.
- Every endpoint names a policy of `Policies` (`RequireAuthorization(Policies.ReadExposure)`) and dispatches a
  request carrying `[RequiresCapability(...)]`; scoped queries implement `IScopedQuery`. A filter outside the user
  scope is `403`; a resource addressed by id outside the scope is `404` (`AuthorizationErrors.NotFound`, checked in
  the handler with `IRequestScope.Current.Covers(lei, businessLine)`).
- Local test users (Development only, password in the Aspire parameter `ccr-test-user-password`): `rm.markets.fr`,
  `fo.rates.desk`, `dir.risk`, `fin.reg.de`, `rm.nolegal`, `lim.admin.fr`, `aud.internal`.
