# Phase 2 V1 Closure Baseline

## Purpose

This document records the closure-ready state for the restrained Phase 2 V1 work already implemented on top of the Post-Phase-1 baseline.

Use it to answer:

- what Phase 2 V1 actually added
- which page and feature roles remain frozen
- what is explicitly still out of scope
- what closure smoke order should be used for the implemented scope

It is a closure document, not a new system-definition document and not a new phase charter.

## Closure-Ready Included Scope

Phase 2 V1 closure-ready scope includes only these implemented areas:

1. Pending review workbench
2. Capture list/detail plus restrained minimal Web capture entry
3. Expense formal consumption polish
4. Workbench Round 2 hierarchy and entry polish
5. Templates as entry-only work modes
6. local operability baseline:
   - backup / restore baseline
   - bounded import / export baseline
   - runtime / status clarity
   - daily-use readiness guidance

This document does not claim that the whole Web app, the whole product, or later phases are complete.

## Role Freeze Across The Included Scope

The following roles remain fixed:

- Workbench = entry layer
- Dashboard = summary support layer
- Templates = entry-only work modes, not automation
- Pending = review queue plus single-item review workbench
- Capture = upstream input visibility layer plus minimal restrained entry
- Expense = formal record consumption domain
- local operability = support-level continuity and recoverability support, not a control center

## Shared Hierarchy And State Freeze

Across the included Phase 2 V1 pages and sections:

- formal facts remain primary
- source and provenance remain contextual support
- actions remain restrained and subordinate to understanding
- runtime and local-operability surfaces remain support-level where possible
- `loading`, `empty`, `unavailable`, and `degraded` meanings remain aligned

The included closure-ready work must not drift into:

- workflow-center behavior
- AI-center behavior
- analytics-center behavior
- admin-console behavior
- frontend-owned business logic

## What Phase 2 V1 Explicitly Does Not Include

Phase 2 V1 does not introduce:

- AI platformization
- charts or analytics center expansion
- template automation engine
- task platform or admin console
- Desktop product expansion beyond shell-level role
- Telegram management expansion
- cloud sync or remote infrastructure
- import/export platformization beyond the bounded local baseline

## Preferred Closure Smoke Order

Use this order for the implemented Phase 2 V1 surfaces:

1. Open `/workbench`
   - confirm current mode, summary support, runtime status, and local continuity remain readable
   - confirm Workbench still reads as entry layer

2. Verify Templates remain entry-only
   - confirm work modes set entry context only
   - confirm no automation or execution wording appears

3. Create or verify one restrained capture entry
   - use existing Capture list/detail if a browser-click flow is not available

4. Open one capture detail page
   - confirm raw captured content stays primary
   - confirm parse, pending, and formal-result linkage stay secondary/contextual

5. Follow one pending linkage where available
   - confirm Pending still reads as a review workbench
   - confirm review actions remain formal and bounded

6. Open the resulting formal page where applicable
   - Expense is the preferred check because it is directly exercised by current capture/pending flows

7. Open `/expense`
   - confirm formal records remain primary
   - confirm source context stays contextual support

8. Re-check local continuity on Workbench
   - confirm backup / restore / bounded transfer surfaces remain support-level

## Bounded Closure Verification Substitute

If a full browser-click smoke is not available in the current environment, use this bounded substitute:

- route and render inspection
- relevant backend regression tests
- relevant Web regression tests
- frontend build verification when Web code changed
- linked-path reasoning based on actual implemented routes and UI copy

Do not claim a live browser-click smoke if it did not happen.

## Recommended Regression Slice

For the implemented Phase 2 V1 surfaces, the recommended regression slice is:

```powershell
python -m pytest -q apps/api/tests/test_system.py apps/api/tests/system/test_runtime_status_api.py apps/api/tests/system/test_local_operability_api.py apps/api/tests/domains/capture/test_capture_smoke.py apps/api/tests/domains/pending/test_pending_read.py apps/api/tests/domains/pending/test_pending_actions_http.py apps/api/tests/domains/formal_read/test_formal_read_layer.py::test_expense_list_service_supports_default_sort_category_and_note_keyword apps/api/tests/domains/formal_read/test_formal_read_layer.py::test_detail_services_raise_not_found_for_missing_records[get_expense_read-999001-EXPENSE_NOT_FOUND]
```

```powershell
python -m pytest -q apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_semantics_contract.py apps/web/tests/shared/test_shared_state_polish.py apps/web/tests/pending/test_pending_review_workbench.py apps/web/tests/capture/test_capture_workbench.py apps/web/tests/expense/test_expense_formal_consumption.py
```

If Web implementation files changed, also run:

```powershell
cd apps/web
npm run build
```

## Closure Status Meaning

Phase 2 V1 may be treated as formally closed when:

- the implemented scope above is recorded in the current docs entrypoints
- the role and hierarchy boundaries remain aligned
- the bounded regression slice remains green
- the closure smoke path or bounded substitute has been explicitly recorded

This still does not mean later phases, broader product hardening, or whole-product completion have been claimed.
