# Phase 3 Readback / Detail Density Closure Baseline

## Purpose

This document records the closure-ready state for the bounded readback/detail-density polish already implemented on top of the current Post-Phase-1, Phase 2 V1, bilingual Web, and Phase 3 V1 input-pack baselines.

Use it to answer:

- what the current readback/detail-density pack actually includes
- which detail/readback Web surfaces are now closure-ready
- which hierarchy and product boundaries remain frozen
- what closure-smoke order should be used for this bounded pack

It is a closure document, not a new phase charter and not a broader Phase 3 completion claim.

## Closure-Ready Included Scope

This bounded readback/detail-density closure-ready pack includes only:

1. detail hierarchy tightening across:
   - Pending detail
   - Capture detail
   - Expense detail
   - Knowledge detail
   - Health detail
2. density/readback polish focused on:
   - compact facts first
   - longer record/readback content second
   - provenance/support later
   - calmer section rhythm and readability
3. bounded cross-domain detail consistency improvements
4. bounded Workbench / Dashboard readback-support polish:
   - clearer summary-support framing
   - clearer recent-context usefulness for reopening detail work
   - no entry-layer or center-role redefinition

This document does not claim broader Phase 3 completion, whole-frontend completion, or whole-product completion.

## Hierarchy And Role Freeze Across The Included Scope

The following hierarchy and role boundaries remain fixed:

- Pending detail = single-item review workbench
- Capture detail = upstream record detail with secondary triage context
- Expense detail = formal-record-first read surface
- Knowledge detail = formal-content-first read surface with AI-derived summary secondary
- Health detail = formal-record-first read surface with rule alerts secondary
- Workbench = entry layer
- Dashboard summary on Workbench = support layer, not a replacement for formal domain pages

Across the included pages:

- compact facts / truth-bearing content remain primary
- longer readback content remains readable but subordinate to primary facts
- provenance, source, and support remain contextual
- actions, results, and states do not overpower the primary reading flow

## What This Bounded Pack Explicitly Does Not Include

This bounded readback/detail-density pack does not introduce:

- charts or analytics expansion
- AI summary or derivation scope expansion
- dashboard-center growth or platformization
- design-system abstraction work
- broader list-page redesign beyond tiny coherence support
- workflow or control-center behavior
- reopened input-pack work
- backend or business-logic changes

## Cross-Page Consistency Freeze

Across Pending, Capture, Expense, Knowledge, Health, and bounded Workbench support:

- detail pages should read like one system rather than isolated page styles
- compact factual framing should appear before long-form record/readback text
- source/provenance sections should remain contextual support
- Workbench summary/recent language should support reopening or resuming detail work rather than becoming a new center
- current bounded zh/en UI copy should remain coherent when touched

This closure-ready pack must not drift into:

- analytics-center behavior
- AI-center behavior
- dashboard-center behavior
- workflow/control-surface behavior
- frontend-owned business-logic creep

## Preferred Closure Smoke Order

Use this order for the implemented readback/detail-density surfaces:

1. Open `/workbench`
   - confirm `Summary Support` remains support-level
   - confirm `Recent Context` helps reopen detail work rather than becoming a history center
2. Open one Pending detail page
   - confirm compact review facts appear before payload, actions, and history
3. Open one Capture detail page
   - confirm current capture facts stay compact and raw content stays primary
4. Open one Expense detail page
   - confirm formal expense facts stay primary and note/provenance remain secondary/contextual
5. Open one Knowledge detail page
   - confirm formal content stays primary, source stays contextual, and AI-derived summary stays secondary
6. Open one Health detail page
   - confirm formal health facts stay primary, source stays contextual, and rule alerts stay secondary
7. Re-check the common pattern
   - compact facts first
   - longer record/readback content next
   - provenance/support later
   - no new control-center behavior

## Bounded Closure Verification Substitute

If a live browser-click smoke is not available in the current environment, use this bounded substitute:

- route and render inspection
- focused Web regression tests for the affected detail/readback surfaces
- locale coverage checks when user-facing copy was touched
- Web build verification when Web implementation files changed
- actual implementation inspection of section ordering and support-layer placement

Do not claim a live browser-click smoke if it did not happen.

## Recommended Regression Slice

For the implemented readback/detail-density surfaces, the recommended regression slice is:

```powershell
python -m pytest -q apps/web/tests/pending/test_pending_review_workbench.py apps/web/tests/capture/test_capture_workbench.py apps/web/tests/expense/test_expense_formal_consumption.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/shared/test_locale_page_coverage.py apps/web/tests/shared/test_locale_foundation.py apps/web/tests/shared/test_shared_state_polish.py
```

If Web implementation files changed, also run:

```powershell
cd apps/web
npm run build
```

## Closure Status Meaning

This bounded readback/detail-density pack may be treated as formally closed when:

- the included scope above is recorded in the current docs entrypoints
- hierarchy, role, and support-layer boundaries remain aligned
- the focused regression slice remains green
- the closure smoke path or bounded substitute has been explicitly recorded

This still does not mean:

- broader Phase 3 work is complete
- charts, analytics, or AI expansion were added
- the whole frontend is complete
- the whole product is complete
