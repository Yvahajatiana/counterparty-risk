# Engineering conventions

These conventions implement section 7 of [ADR-0001](adr/0001-stack-and-architecture.md). They apply to
every agent (Lead, Back-1, Back-2, Back-3, Front) and to every human contributor.

## 1. Onboarding (mandatory)

```powershell
git clone https://github.com/Yvahajatiana/counterparty-risk.git
cd counterparty-risk
winget install Gitleaks.Gitleaks          # required by the pre-commit hook
git config core.hooksPath .githooks       # mandatory: gitleaks + forbidden files on every commit
```

- The pre-commit hook must never be bypassed (`--no-verify` is forbidden). A false positive is fixed in
  `.gitleaks.toml` through a PR reviewed by the Lead, never by skipping the hook.
- The EDB (`docs/edb.md`) is local only and git-ignored. No PDF, key, `.env` file, dump or real portfolio
  data is ever committed (enforced by the hook and by the "Forbidden files guard" CI check).

## 2. Branches and worktrees

- One issue = one branch = one PR.
- Branch name: `<agent>/<issue>-<short-name>` in kebab-case, where `<agent>` is one of `lead`, `back-1`,
  `back-2`, `back-3`, `front`, `expert-ccr`.
  Examples: `lead/3-adr-001-architecture`, `back-2/21-sa-ccr`, `front/12-angular-workspace`.
- Each issue is worked in its own git worktree so that several issues progress in parallel:

  ```powershell
  git fetch origin
  git worktree add ../cr-21 -b back-2/21-sa-ccr origin/main
  # ... work, commit, push, open the PR ...
  git worktree remove ../cr-21            # once the PR is merged
  ```

- Branches are short-lived: rebase on `origin/main` rather than merging `main` into the branch.
  `main` only accepts squash or rebase merges (linear history).

## 3. Commits

- [Conventional Commits](https://www.conventionalcommits.org/): `<type>(<scope>): <summary> (#<issue>)`.
  - Types: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `build`, `ci`, `chore`, `security`.
  - Scopes: module or area in kebab-case: `reference-data`, `market-data`, `portfolio`, `risk-engine`,
    `capital`, `reporting`, `api`, `bff`, `workers`, `shared-kernel`, `contracts`, `web`, `ci`, `adr`.
  - Example: `feat(risk-engine): add SA-CCR interest rate add-on (#21)`.
- Summary in English, imperative mood, at most 72 characters. The body explains the why.
- **No `Co-Authored-By` trailer**, and the git identity is never changed.
- Never commit secrets, confidential data or generated build output.

## 4. Pull requests

1. Fill in `.github/pull_request_template.md`, with `Closes #N` and the `agent:<name>` label.
2. Run the official Anthropic `/security-review` locally and paste its summary in the PR.
3. Codex reviews the PR automatically; comment `@codex review` after new commits (no Anthropic API key in CI).
4. Issues labelled `review:expert-ccr` also need the Expert CCR validation as a PR comment.
5. The Lead runs a local Claude review (`/code-review`), then merges once every required check is green and every review thread is resolved.
   Agents never merge their own PR.

## 5. Code

### All languages

- **Code comments are in English.** Issues, PR discussions and documentation may be in French.
- Formatting follows `.editorconfig` (UTF-8, LF, final newline; 4 spaces for C#, 2 for web files).
- No secret, connection string with password, token or real data in code, configuration or tests.

### C# / .NET

- `Nullable` and `TreatWarningsAsErrors` enabled; .NET analyzers (`latest-recommended`) with security
  rules as errors (see `.editorconfig`); SecurityCodeScan as a complement (#7).
- Amounts are `decimal`. `double` is allowed only inside the Monte-Carlo core (#22).
- Naming:
  - projects and namespaces `CounterpartyRisk.<Module>.<Layer>` (e.g. `CounterpartyRisk.RiskEngine.Domain`);
  - commands / queries / handlers: `XxxCommand`, `XxxQuery`, `XxxHandler` (handlers `internal sealed`);
  - repositories `IXxxRepository`; validators `XxxValidator`;
  - database: one schema per module (`refdata`, `market`, `portfolio`, `risk`, `capital`, `reporting`),
    tables and columns in `snake_case`.
- Minimal APIs only (no MVC controllers); the API exposes DTOs, never domain entities.
- Banned libraries (licence): MediatR, AutoMapper, MassTransit, FluentAssertions, EPPlus, Duende.
- Tests: xUnit v3 + Shouldly + NSubstitute; integration tests on Testcontainers PostgreSQL.

### Angular (`web/`)

- Standalone components, signals, `OnPush`, zoneless; strict TypeScript and strict templates.
- Charts only through the `ChartAdapter` (#14); grids only through the AG Grid Community wrapper.
- Exact dependency versions (no `^` or `~`), installed with `npm ci`.

## 6. Protection of `main`

`main` is protected by the repository ruleset `main-protection`, defined in
[`adr/assets/main-ruleset.json`](adr/assets/main-ruleset.json):

| Rule | Setting |
| --- | --- |
| Pull request | Required; 0 approval (single shared GitHub account); all review threads resolved |
| Merge methods | Squash or rebase only (linear history) |
| Required checks | `Gitleaks (full history)`, `TruffleHog (verified secrets)`, `Forbidden files guard`, `Build & test (.NET)`, `Vulnerable NuGet packages`, `Workflow pinning`, `SBOM CycloneDX`, `Trivy container scan` (from GitHub Actions) |
| Force-push / deletion | Forbidden |
| Bypass | Repository admins, through a pull request only: direct pushes to `main` are rejected for everyone |

### Applying or updating the ruleset

The ruleset is applied by the Lead after the ADR-0001 PR is merged, with the GitHub CLI authenticated as
a repository admin (PowerShell 7):

```powershell
./scripts/setup-branch-protection.ps1 -WhatIf   # dry run: shows whether the ruleset is created or updated
./scripts/setup-branch-protection.ps1           # creates or updates the "main-protection" ruleset
gh api repos/Yvahajatiana/counterparty-risk/rulesets   # check
```

The script is idempotent: it looks up the ruleset by name and updates it, or creates it. To add the
build, test and CodeQL checks delivered by #8, add them to `required_status_checks` in the JSON file
(using the exact job `name:` of the workflow), merge the PR, then re-run the script.

Checks after applying it:

- `git push origin main` directly is rejected.
- A PR with a failing required check cannot be merged with the normal merge button.

### Related repository settings (Lead, once)

These settings are not changed by the script. Check or enable them in Settings > Code security, or with
`gh`:

```powershell
# Secret scanning and push protection
gh api --method PATCH repos/Yvahajatiana/counterparty-risk `
  -f "security_and_analysis[secret_scanning][status]=enabled" `
  -f "security_and_analysis[secret_scanning_push_protection][status]=enabled"
# Dependabot alerts and security updates
gh api --method PUT repos/Yvahajatiana/counterparty-risk/vulnerability-alerts
gh api --method PUT repos/Yvahajatiana/counterparty-risk/automated-security-fixes
# Private vulnerability reporting (referenced by SECURITY.md)
gh api --method PUT repos/Yvahajatiana/counterparty-risk/private-vulnerability-reporting
```
