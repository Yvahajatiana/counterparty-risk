# Contributing

## Team
The project is delivered by a fixed team of agents for its whole lifecycle:

| Agent | Label | Scope |
| --- | --- | --- |
| Expert CCR (business owner) | `agent:expert-ccr` | Guardian of the requirements (EDB): challenges needs, validates indicators, formulas, data mappings, widgets; business review of risk computation code |
| Lead | `agent:lead` | Architecture, ADRs, shared contracts, arbitration, final review, merges |
| Back-1 | `agent:back-1` | Data: schema, ingestion (GLEIF, ECB, BIS, EBA, FR Y-9C, SDR), history |
| Back-2 | `agent:back-2` | Risk engine: exposure cascade, SA-CCR, EE/PFE, CVA |
| Back-3 | `agent:back-3` | Platform: solution skeleton, CI, security (OIDC, ABAC, audit), API, RWA |
| Front | `agent:front` | Angular app: shell, AG Grid tables, Highcharts charts, widgets W1-W12 |

## Onboarding (mandatory)
1. Install gitleaks (`winget install Gitleaks.Gitleaks`).
2. Enable the local hooks once per clone: `git config core.hooksPath .githooks`.
   The pre-commit hook (gitleaks + forbidden files) is mandatory and must never be bypassed (`--no-verify` is forbidden).
3. Read [ADR-0001](docs/adr/0001-stack-and-architecture.md) (stack, modules, dependency rules) and
   [docs/conventions.md](docs/conventions.md) (branches, worktrees, commits, naming).

## Workflow
1. Pick an issue whose `Depends on` items are closed (or only need a published contract).
2. One branch per issue: `<agent>/<issue-number>-<short-name>`, in its own git worktree
   so several issues progress in parallel: `git worktree add ../cr-21 -b back-2/21-sa-ccr origin/main`.
3. Conventional Commits with the issue reference (`feat(risk-engine): add SA-CCR add-on (#21)`), no `Co-Authored-By` trailer.
4. Before opening the PR, run the official Anthropic security review locally in Claude Code:
   `/security-review`. Fix findings, paste the summary in the PR.
5. Open the PR with the template. Codex reviews it automatically; comment `@codex review` after new commits.
6. Issues labelled `review:expert-ccr` also need the business validation of the Expert CCR agent (`.claude/agents/expert-ccr.md`), recorded as a PR comment.
7. The Lead runs a local Claude review (`/code-review`), then merges. CI (secret scan, build, tests) must be green and every review thread resolved.

## Rules
- Secure by Design first: `priority:security-by-design` issues are done before feature work they gate.
- Code comments in English.
- No real confidential data: only public open data and synthetic portfolios.
- Never commit the requirements document (EDB) or any personal document.
