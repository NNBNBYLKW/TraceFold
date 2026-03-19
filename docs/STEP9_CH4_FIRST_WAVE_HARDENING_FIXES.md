# Step 9 Chapter 4 First Wave Hardening Fixes

## Purpose

第一轮只修最影响长期真实使用信任、且低成本高收益的问题。

## Real Fixes Completed

### 1. Web failure wording hardening

- Web request handling now distinguishes:
  - service unavailable
  - invalid response
  - normal request failure
- This reduces browser-opaque or parser-opaque failure text.

### 2. AI derivation empty-state hardening

- Knowledge / Health AI derivation empty states now explicitly say `has not been generated yet`
- This reduces confusion between:
  - not generated
  - generation failed
  - unavailable

### 3. Acceptance runner diagnosability hardening

- `step9_ch1_acceptance_smoke.py` now reports:
  - stage start
  - stage pass
  - stage failure with exit code
  - startup failure to launch a stage
- This makes the runner usable as a repeatable acceptance tool instead of a vague shell wrapper.

### 4. Chapter 1 system smoke wording alignment

- The Step 9 Chapter 1 system acceptance test now matches the unified wording introduced in Chapter 3.
- This keeps system-wide acceptance honest instead of letting old wording assertions simulate regressions.

## Accepted Phase 1 Stability Tech Debt

- Desktop runtime is still skeleton-level shell runtime, not a full native runtime
- repo-style schema ensure is still not a formal migration framework
- Web validation still relies on build + source-contract tests rather than browser E2E
- older services still emit `datetime.utcnow()` deprecation warnings
- rule engine failure is not represented as a dedicated lifecycle state separate from fetch failure

These remain acceptable for Phase 1 because they do not currently:

- create a second business center
- create fake write success
- pollute formal facts
- hide the primary recovery path
