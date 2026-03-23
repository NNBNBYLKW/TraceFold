# Step 9 Chapter 2 Tech Debt Register

This register contains the items that are acceptable to carry through the end of Phase 1 as long as they remain honestly described and do not expand silently.

## TD-001: Desktop shell runtime is still skeleton-level

- Why acceptable now:
  - Desktop is explicitly a shell in Phase 1, not a second business frontend.
  - Current shell behavior already supports the required Step 7/8 scope.
- Why it does not block Phase 1:
  - Shared Web workbench remains the real business surface.
  - No desktop-only business logic is introduced.
- What must stay true:
  - Docs must keep calling it a skeleton/baseline runtime.
  - No one may describe it as a complete native desktop client.

## TD-002: No formal migration framework yet

- Why acceptable now:
  - The repo has consistently used metadata bootstrap and ensure scripts so far.
  - Introducing a migration framework now would exceed the current cleanup chapter.
- Why it does not block Phase 1:
  - Current schema bootstrapping is still sufficient for the current local-first Phase 1 usage pattern.
- What must stay true:
  - Repo-style ensure scripts must not be described as a hardened migration platform.

## TD-003: Web UI verification is not browser-E2E

- Why acceptable now:
  - Source-contract tests plus build smoke already verify the current workbench contract without adding a new framework.
- Why it does not block Phase 1:
  - The core Step 8 boundary is API/service-driven and already covered by backend tests.
- What must stay true:
  - Documentation must not overstate these checks as full browser automation.

## TD-004: Legacy UTC warnings remain in older services

- Why acceptable now:
  - They do not change the current business semantics or acceptance outcomes.
- Why it does not block Phase 1:
  - The warnings are noisy but not evidence of a second business center or fact-layer corruption.
- What must stay true:
  - They remain listed as technical debt until cleaned.

## TD-005: Multi-app pytest collection conflicts still exist

- Why acceptable now:
  - The acceptance runner already uses explicit per-app commands.
- Why it does not block Phase 1:
  - Repeatable acceptance execution is still available.
- What must stay true:
  - Test execution docs/scripts must keep the explicit split until the test layout is cleaned later.
