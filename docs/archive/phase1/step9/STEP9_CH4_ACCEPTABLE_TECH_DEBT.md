# Step 9 Chapter 4 Acceptable Tech Debt

| id | item | why acceptable now | why not blocking Phase 1 |
| --- | --- | --- | --- |
| TD-CH4-001 | Desktop shell runtime is still skeleton-level | Desktop is explicitly shell-only in Phase 1 | It does not own business logic or facts |
| TD-CH4-002 | repo-style schema ensure instead of migration framework | Current repo bootstrap pattern is still explicit and documented | It does not create a hidden write path or false fact mutation |
| TD-CH4-003 | Web lacks browser E2E stack | Existing API, build, and source-contract tests still protect core semantics | It reduces polish confidence, not fact-layer safety |
| TD-CH4-004 | `datetime.utcnow()` warnings remain in older services | Known technical debt with stable behavior today | Warning noise does not currently break semantics |
| TD-CH4-005 | Rule alert lifecycle does not yet expose `not run` as a dedicated UI state | Current UI still separates empty vs fetch error | This is a clarity gap, not a fact-layer corruption risk |
