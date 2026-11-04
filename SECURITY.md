# Security Policy

This project follows a **Secure by Design** approach: security requirements are defined and
tested before features, and every pull request is reviewed for security first.

## Reporting a vulnerability

Please do **not** open a public issue. Use GitHub private vulnerability reporting
(Security tab > "Report a vulnerability").

## Guardrails

- No secrets in the repository: use user-secrets locally and a vault / GitHub secrets in CI.
- CI blocks merges on: gitleaks, TruffleHog, forbidden files guard, Claude security review.
- No real confidential portfolio data is ever committed: only public open data and synthetic data.
- Access to figures is filtered by legal entity and business line (least privilege).
