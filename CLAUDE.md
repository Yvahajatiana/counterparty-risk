# Counterparty Risk Dashboard - agent instructions

- Stack: .NET 10 back (Clean Architecture, DDD, CQRS), PostgreSQL + EF Core, Angular front with
  AG Grid Community (tables), Highcharts (charts), Angular Material + Tailwind, corporate style.
- Team is fixed: Expert CCR (business owner, `.claude/agents/expert-ccr.md`), Lead, Back-1, Back-2, Back-3, Front.
- Any doubt on an indicator, formula, data mapping or widget goes to the `expert-ccr` agent. The local EDB lives in `docs/edb.md` (git-ignored, never commit it). See CONTRIBUTING.md for scopes and workflow.
- Secure by Design is the top priority. Run `/security-review` before every PR.
- PR review: Codex reviews automatically when a PR opens (`@codex review` after new commits); the Lead runs a local Claude review (`/code-review`) before each merge. No Anthropic API key in CI.
- Never commit the EDB (`*Expression des besoins*.md`) or any PDF / secret. Hooks: `git config core.hooksPath .githooks`.
- Code comments in English. Issues and PR discussions may be in French.
- Domain rules: always label the exposure level; never mix economic (EE/PFE) and regulatory (EAD/RWA)
  measures in one chart; PFE/EAD/EEPE are computed per netting set and need the documented allocation rule.
