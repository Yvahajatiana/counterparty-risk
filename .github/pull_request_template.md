## Linked issue
Closes #

## Agent
<!-- agent:lead | agent:back-1 | agent:back-2 | agent:back-3 | agent:front -->

## Summary

## Security checklist (Secure by Design)
- [ ] Local `/security-review` (official Anthropic skill in Claude Code) run, findings fixed or justified below
- [ ] Local gitleaks pre-commit hook enabled (`git config core.hooksPath .githooks`)
- [ ] AuthZ enforced server-side (legal entity / business line) for every new endpoint or query
- [ ] Inputs validated, no sensitive data in logs, no secrets in code or config
- [ ] Exported files protected against CSV/Excel formula injection (if applicable)

## Risk / domain checklist
- [ ] Every displayed amount carries its exposure level (gross, after netting, after collateral, EAD)
- [ ] Economic and regulatory measures are not mixed in the same chart
- [ ] Tests added (unit + integration where relevant)

## Security review notes
<!-- Paste the /security-review summary here -->

---
After opening the PR, comment `@claude review` to trigger the Claude GitHub App review.
