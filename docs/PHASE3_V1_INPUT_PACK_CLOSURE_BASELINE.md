# Phase 3 V1 Input Pack Closure Baseline

## Purpose

This document records the closure-ready state for the bounded Phase 3 V1 input-side pack already implemented on top of the current Post-Phase-1, Phase 2 V1, and bilingual Web baselines.

Use it to answer:

- what the current Phase 3 V1 input pack actually includes
- which input-side Web surfaces are now closure-ready
- which chain, role, and product boundaries remain frozen
- what closure-smoke order should be used for this bounded pack

It is a closure document, not a new phase charter and not a broader Phase 3 completion claim.

## Closure-Ready Included Scope

Phase 3 V1 Input Pack closure-ready scope includes only:

1. Quick Capture
   - Web-only
   - pure-text
   - stay-on-page repeated entry
   - capture-first semantics
2. Bulk Intake with Preview
   - text-file import only
   - preview-before-import
   - import into Capture only
   - bounded result feedback
3. Intake Triage / Capture Inbox
   - Capture list strengthened as an inbox/triage surface
   - bounded next-step cues
   - downstream linkage visibility
   - triage context on Capture detail

This document does not claim that broader Phase 3 work, the whole frontend, or the whole product is complete.

## Role Freeze Across The Included Scope

The following roles remain fixed:

- Quick Capture = the light single-item capture surface
- Bulk Intake = the bounded file-based capture intake surface
- Capture Inbox = the upstream inbox/triage/navigation surface
- Capture detail = upstream record detail with secondary triage context
- Pending = the downstream formal review workbench

These roles must not be blurred into:

- a workflow board
- a control center
- a second Pending surface
- an automation layer
- a chat or bot interaction model

## Chain And Semantics Freeze

Across all three included input-pack surfaces:

- input remains capture-first
- none of the surfaces bypass Capture
- none of the surfaces bypass Pending/review semantics
- none of the surfaces write directly to formal records
- structure still happens later
- review still happens later

The main chain remains:

`Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation`

## What Phase 3 V1 Input Pack Explicitly Does Not Include

Phase 3 V1 Input Pack does not introduce:

- direct review actions from Capture
- workflow board or control-center behavior
- AI parsing, suggestions, classification, or prioritization
- desktop quick entry
- Telegram quick-entry expansion
- multimodal input
- generic import or ETL platform behavior
- CSV / Excel / mapping-style import formats
- direct-to-formal import
- readback / detail-density unification

## Cross-Package Consistency Freeze

Across Quick Capture, Bulk Intake, and Capture Inbox:

- input friction reduction remains the common theme
- explanatory copy remains cue-oriented rather than control-oriented
- navigation remains bounded and explicit
- Workbench entry remains entry-oriented rather than command-center oriented
- shared shell and shared state semantics remain intact where reused
- current bounded zh/en UI support remains preserved for user-facing copy

This closure-ready pack must not drift into:

- AI behavior
- automation behavior
- workflow behavior
- Desktop / Telegram scope expansion
- frontend-owned business logic

## Preferred Closure Smoke Order

Use this order for the implemented Phase 3 V1 input-pack surfaces:

1. Open `/workbench`
   - confirm input-side entry points remain visible as entry affordances, not command-center controls
2. Enter `/quick-capture`
   - submit 2-3 quick text entries
   - confirm the page stays in place and remains ready for the next item
3. Open `/capture`
   - confirm the list reads as an intake inbox / triage surface
   - confirm linkage and next-step cues remain navigational only
4. Open one capture detail page
   - confirm raw captured content remains primary
   - confirm triage context remains secondary and points toward Pending or formal pages only through navigation
5. Enter `/bulk-intake`
   - preview a supported text file
   - confirm import creates capture records first
   - confirm result feedback remains bounded
6. Re-check `/capture`
   - confirm imported items are visible as capture records
   - confirm downstream cues remain visibility/navigation only

## Bounded Closure Verification Substitute

If a live browser-click smoke is not available in the current environment, use this bounded substitute:

- route and render inspection
- targeted backend regression tests
- targeted frontend regression tests
- frontend build verification when Web files changed or when a stronger closure check is useful
- linked-path reasoning based on actual implemented surfaces

Do not claim a live browser-click smoke if it did not happen.

## Recommended Regression Slice

For the implemented Phase 3 V1 input-pack surfaces, the recommended regression slice is:

```powershell
python -m pytest -q apps/api/tests/domains/capture/test_capture_smoke.py apps/api/tests/domains/capture/test_bulk_capture_intake_http.py
```

```powershell
python -m pytest -q apps/web/tests/capture/test_capture_workbench.py apps/web/tests/capture/test_quick_capture_surface.py apps/web/tests/capture/test_bulk_intake_surface.py apps/web/tests/shared/test_locale_page_coverage.py
```

If a stronger closure verification is desired or if Web implementation files changed, also run:

```powershell
cd apps/web
npm run build
```

## Closure Status Meaning

Phase 3 V1 Input Pack may be treated as formally closed when:

- the included scope above is recorded in the current docs entrypoints
- the role and chain boundaries remain aligned
- the bounded regression slice remains green
- the closure smoke path or bounded substitute has been explicitly recorded

This still does not mean:

- broader Phase 3 work is complete
- readback or detail-density work is complete
- desktop or Telegram quick-entry work exists
- the whole frontend or whole product is complete
