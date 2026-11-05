# Security Policy

This project follows a **Secure by Design** approach: security requirements are defined and
tested before features, and every pull request is reviewed for security first.

## Reporting a vulnerability

Please do **not** open a public issue. Use GitHub private vulnerability reporting
(Security tab > "Report a vulnerability").

## Guardrails

- No secrets in the repository: use user-secrets locally and a vault / GitHub secrets in CI.
- Locally: gitleaks pre-commit hook (`.githooks`) and the official Anthropic `/security-review` before every PR.
- CI blocks merges on: gitleaks, TruffleHog, forbidden files guard. PRs are reviewed automatically by Codex, then locally by the Lead with Claude (`/code-review`).
- No real confidential portfolio data is ever committed: only public open data and synthetic data.
- Access to figures is filtered by legal entity and business line (least privilege).

## Security documentation

| Document | Content |
| --- | --- |
| [Threat model](docs/security/threat-model.md) | STRIDE analysis per trust boundary (TB1-TB7), threats `TM-*` with mitigation, owning issue and abuse-case test, authorization matrix, residual risks |
| [Data flow diagram](docs/security/data-flow.md) | DFD level 1 (Mermaid), trust boundaries and flows |
| [ASVS 5.0 L2 checklist](docs/security/asvs-l2-checklist.md) | All OWASP ASVS 5.0 Level 1-2 requirements, `SEC-*` identifiers, evidence and owning issue; PR mini-checklist |
| [Data classification](docs/security/data-classification.md) | Classes C0 / C1 / C1-PD / C2 / R, storage, logging and export rules, data and crypto inventories, logging inventory |
| [External sources](docs/security/external-sources.md) | TLS, allowlist, integrity, size limits and safe parsing rules for open data ingestion |

Any pull request that adds a trust boundary, flow, external source, table or key updates these documents.
