# Security Policy

This project follows a **Secure by Design** approach: security requirements are defined and
tested before features, and every pull request is reviewed for security first.

## Reporting a vulnerability

Please do **not** open a public issue. Use GitHub private vulnerability reporting
(Security tab > "Report a vulnerability").

## Guardrails

- No secrets in the repository: use user-secrets locally and a vault / GitHub secrets in CI.
- Locally: gitleaks pre-commit hook (`.githooks`) and the official Anthropic `/security-review` before every PR.
- CI blocks merges on: gitleaks, TruffleHog, forbidden files guard. PRs are reviewed by the Claude GitHub App (`@claude review`).
- No real confidential portfolio data is ever committed: only public open data and synthetic data.
- Access to figures is filtered by legal entity and business line (least privilege).
