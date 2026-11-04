# Architecture Decision Records

This folder holds the Architecture Decision Records (ADR) of the Counterparty Risk Dashboard.
ADRs follow the [MADR](https://adr.github.io/madr/) format, adapted with a mandatory
"Security and licensing notes" section (Secure by Design).

## Index

| ADR | Title | Status | Issue |
| --- | --- | --- | --- |
| [0001](0001-stack-and-architecture.md) | Stack, Clean/DDD/CQRS modular monolith, repository layout and conventions | Proposed | #3 |

ADR-002 (arbitration of the open points of the EDB) is expected from #6.

## Lifecycle

```
Proposed  -->  Accepted  -->  Superseded by ADR-NNNN
                   \-->  Deprecated
```

- **Proposed**: the ADR is open in a PR. Agents may already build on it (see "parallel:ready" issues),
  accepting that names may still change.
- **Accepted**: the PR is merged and the Lead has recorded the acceptance comment on the PR.
- **Superseded by ADR-NNNN**: a later ADR replaces it. The old ADR is never deleted or rewritten;
  only its status line and a link to the new ADR are updated.
- **Deprecated**: the decision no longer applies and nothing replaces it.

## How to write an ADR

1. Copy [`0000-template.md`](0000-template.md) to `NNNN-short-title.md` (next free number, kebab-case title).
2. Fill in every section; keep it short and factual. Record licences (SPDX id) of every new dependency.
3. Add a line to the index above.
4. Open a PR on a branch `<agent>/<issue>-adr-NNNN-<short-title>`; ADRs are owned by the Lead (CODEOWNERS).
5. Never paste content of the EDB (`docs/edb.md`, git-ignored) or any confidential data into an ADR.

## Assets

`assets/` holds machine-readable artefacts referenced by ADRs, for example
[`assets/main-ruleset.json`](assets/main-ruleset.json) (protection of `main`, applied by
`scripts/setup-branch-protection.ps1`).
