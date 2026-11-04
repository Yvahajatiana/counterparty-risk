# Contributing

## Team
The project is delivered by a fixed team of agents for its whole lifecycle:

| Agent | Label | Scope |
| --- | --- | --- |
| Lead | `agent:lead` | Architecture, ADRs, shared contracts, arbitration, final review, merges |
| Back-1 | `agent:back-1` | Data: schema, ingestion (GLEIF, ECB, BIS, EBA, FR Y-9C, SDR), history |
| Back-2 | `agent:back-2` | Risk engine: exposure cascade, SA-CCR, EE/PFE, CVA |
| Back-3 | `agent:back-3` | Platform: solution skeleton, CI, security (OIDC, ABAC, audit), API, RWA |
| Front | `agent:front` | Angular app: shell, AG Grid tables, Highcharts charts, widgets W1-W12 |

## Workflow
1. Pick an issue whose `Depends on` items are closed (or only need a published contract).
2. One branch per issue: `<agent>/<issue-number>-<short-name>`, ideally in its own git worktree
   so several issues progress in parallel: `git worktree add ../cr-<issue> -b back-2/12-sa-ccr`.
3. Enable the local hooks once: `git config core.hooksPath .githooks` (requires gitleaks).
4. Before opening the PR, run the official Anthropic security review locally in Claude Code:
   `/security-review`. Fix findings, paste the summary in the PR.
5. Open the PR with the template, then comment `@claude review` (Claude GitHub App).
6. The Lead reviews and merges. CI (secret scan, build, tests) must be green.

## Rules
- Secure by Design first: `priority:security-by-design` issues are done before feature work they gate.
- Code comments in English.
- No real confidential data: only public open data and synthetic portfolios.
- Never commit the requirements document (EDB) or any personal document.
