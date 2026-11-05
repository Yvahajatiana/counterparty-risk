## Linked issue
Closes #

## Agent
<!-- agent:lead | agent:back-1 | agent:back-2 | agent:back-3 | agent:front -->

## Summary

## Security checklist (Secure by Design)
- [ ] Local `/security-review` (official Anthropic skill in Claude Code) run, findings fixed or justified below
- [ ] Local gitleaks pre-commit hook enabled (`git config core.hooksPath .githooks`)

ASVS 5.0 L2 mini-checklist (tick what applies, write `n/a` otherwise; full rows in
[docs/security/asvs-l2-checklist.md](https://github.com/Yvahajatiana/counterparty-risk/blob/main/docs/security/asvs-l2-checklist.md#pr-mini-checklist)):
- [ ] 1. AuthZ server-side, deny by default, scope (legal entity / business line) only from the token, out-of-scope id -> 404 (SEC-102..108)
- [ ] 2. Inputs validated server-side against allowlists (axes, sort, enums, ranges) (SEC-018, SEC-019)
- [ ] 3. Parameterised SQL only, RLS on any new table (SEC-006, SEC-108)
- [ ] 4. Output rendered as text: no `innerHTML` / `bypassSecurityTrust*`, Highcharts `useHTML: false` (SEC-003, SEC-027)
- [ ] 5. Exports: formula neutralisation, row cap, export policy, audited (SEC-009, SEC-025)
- [ ] 6. Untrusted files: no DTD, size / zip limits, no path from archive entries (SEC-013, SEC-049..053)
- [ ] 7. Outbound HTTP: HTTPS, default certificate validation, host allowlist, no unchecked redirect (SEC-010, SEC-159, SEC-166, SEC-191)
- [ ] 8. Session: no token in the browser, `__Host-` cookie, `X-CSRF` on `/api/**` (SEC-031, SEC-038, SEC-116)
- [ ] 9. No secret in code, config, logs or images; options validated at startup (SEC-165, SEC-168)
- [ ] 10. Logs: no token, cookie, secret or individual amount; security events audited (SEC-202, SEC-204, SEC-205)
- [ ] 11. Errors as generic `ProblemDetails`; fail closed (SEC-210, SEC-212)
- [ ] 12. New flow, source, table or dependency: [threat model](https://github.com/Yvahajatiana/counterparty-risk/blob/main/docs/security/threat-model.md) and [data classification](https://github.com/Yvahajatiana/counterparty-risk/blob/main/docs/security/data-classification.md) updated, no vulnerable package (SEC-162, SEC-175, SEC-187)

## Risk / domain checklist
- [ ] Every displayed amount carries its exposure level (gross, after netting, after collateral, EAD)
- [ ] Economic and regulatory measures are not mixed in the same chart
- [ ] Tests added (unit + integration where relevant), including the abuse-case tests of the threats owned by this issue

## Security review notes
<!-- Paste the /security-review summary here -->

---
After opening the PR, comment `@claude review` to trigger the Claude GitHub App review.
