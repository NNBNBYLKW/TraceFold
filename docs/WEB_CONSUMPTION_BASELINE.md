# Web Consumption Baseline

## Purpose

This document records the current formal Web consumption boundary for the main TraceFold pages already wired to shared backend baselines.

Use it to answer:

- which API surfaces the Web currently consumes as formal paths
- how formal facts, alerts, and AI derivations are separated in UI
- which state semantics are already aligned across the main pages

It is not a design brief and not a page-by-page redesign document.

## Current Status Note

The first Post-Phase-1 optimization round for the current Web main consumption lines has been completed and closed.

That closure applies only to:

- Workbench / Dashboard
- Knowledge detail + `knowledge_summary`
- Health records + rule alerts
- shared state presentation for the pages above

It does not expand the formal Web baseline into a new product center, AI center, alerts center, or Web-owned workflow layer.

A later restrained Phase 2 V1 enhancement pack is now implementation-complete and closure-ready.

That Phase 2 V1 scope adds:

- Pending review workbench
- Capture list/detail plus restrained minimal Web entry
- Expense formal consumption polish
- Workbench Round 2 hierarchy and template-entry restraint
- local operability baseline on Workbench

This adds formal consumption and support surfaces without changing the service-centered architecture or creating a new product center.

## Current Coverage

The current formal Web consumption baseline covers:

1. Workbench / Dashboard
2. Pending review workbench
3. Capture list/detail + restrained minimal capture entry
4. Expense list/detail formal consumption
5. Knowledge detail + `knowledge_summary`
6. Health records + rule alerts
7. shared state presentation for major page states

## Workbench / Dashboard

Workbench and Dashboard currently consume:

- `GET /api/workbench/home`
- `GET /api/dashboard`
- `GET /api/system/status`
- `GET /api/system/local-operability`
- `POST /api/system/backup`
- `POST /api/system/restore`
- `POST /api/system/export/capture-bundle`
- `POST /api/system/import/capture-bundle`

Current UI boundary:

- Workbench remains an entry layer, not a second business center
- Dashboard remains a summary layer, not a replacement for formal domain pages
- runtime status can degrade locally without collapsing the whole page
- local continuity remains support-level and local-first, not an admin or control center

Current state semantics:

- `loading`
- `empty`
- `unavailable`
- `degraded`

## Pending Review Workbench

Pending currently consumes:

- `GET /api/pending`
- `GET /api/pending/{id}`
- `POST /api/pending/{id}/fix`
- `POST /api/pending/{id}/confirm`
- `POST /api/pending/{id}/discard`
- `POST /api/pending/{id}/force_insert`

Current UI boundary:

- Pending list remains a review queue
- Pending detail remains a single-item review workbench
- review actions remain formal review actions, not workflow orchestration
- resolved items remain readable as review context, not active control surfaces

## Capture

Capture currently consumes:

- `GET /api/capture`
- `GET /api/capture/{id}`
- `POST /api/capture`

Current UI boundary:

- Capture remains an upstream visibility layer
- capture detail keeps the input record primary and downstream linkage secondary
- minimal capture entry remains plain-text and restrained
- Capture does not become a broad intake platform or workflow console

## Expense

Expense currently consumes:

- `GET /api/expense`
- `GET /api/expense/{id}`

Current UI boundary:

- expense list and detail remain formal-record-first consumption surfaces
- source and provenance stay contextual support
- Expense does not become a charts, analytics, or AI-finance center

## Knowledge Detail

Knowledge detail currently consumes:

- `GET /api/knowledge/{id}`
- `GET /api/ai-derivations/knowledge/{id}`
- `POST /api/ai-derivations/knowledge/{id}/recompute`

Current UI boundary:

- formal knowledge content remains the primary section
- AI-derived summary remains a separate secondary section
- AI summary does not replace formal content and is not presented as fact authority

Current derivation states surfaced in UI:

- `ready`
- `not generated`
- `failed`
- `invalidated`
- `pending` / `running`
- `unavailable`

## Health

Health pages currently consume:

- `GET /api/health`
- `GET /api/health/{id}`
- `GET /api/alerts`
- `POST /api/alerts/{id}/acknowledge`
- `POST /api/alerts/{id}/resolve`

Current UI boundary:

- formal health records remain the primary content
- rule alerts remain a separate reminder layer
- alert state changes do not imply automatic mutation of health facts

Current alert states surfaced in UI:

- `open`
- `acknowledged`
- `resolved`
- `empty`
- `unavailable`

## Shared State Semantics

The current shared state polish keeps these meanings aligned across the main pages:

- `loading`: request is still in progress
- `empty`: API succeeded but the current page or section has no data to show
- `unavailable`: API failed or returned unusable data
- `degraded`: system status is warning-level, but readable content may still remain available

Derived-state semantics:

- `derivation not generated`: no derivation exists yet and this is not an error
- `derivation failed`: formal content remains readable, but derivation generation failed
- `derivation invalidated`: an older derivation is stale and should be recomputed

Alert semantics:

- `alerts empty`: no relevant alerts currently exist
- `alerts unavailable`: alert reads failed independently from formal record reads

For the Phase 2 V1 additions:

- Pending action success/failure stays inside the review detail context
- Capture linkage absence stays contextual rather than becoming page failure
- Expense source/provenance remains contextual rather than center-stage
- local continuity support stays support-level even when it exposes explicit local backup and transfer actions

## What The Web Is Not Doing

The current Web baseline does not introduce:

- an AI center
- an alerts center
- a rule management console
- a task runtime control center
- an admin console
- a workflow center
- a capture platform
- an analytics center
- a prompt or model configuration UI
- Desktop / Telegram specific display semantics

## Recommended Doc Flow For Web Work

Use this order:

1. `docs/WEB_CONSUMPTION_BASELINE.md`
2. `docs/WEB_SHARED_STATE_POLISH_BASELINE.md`
3. `docs/WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
4. `docs/WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
5. `docs/WEB_PP1_CROSS_PAGE_CONSISTENCY_AND_SMOKE_BASELINE.md`
6. `docs/PHASE2_V1_CLOSURE_BASELINE.md`
7. `docs/API_SEEDED_INTEGRATION_SMOKE.md` for fresh demo DB validation

## Manual Smoke Focus

With the normal fresh demo DB flow already running:

1. Open `http://127.0.0.1:3000/workbench`
2. Confirm workbench home, dashboard summary, runtime status, and local continuity are all readable
3. Open `http://127.0.0.1:3000/capture`
4. Confirm Capture remains upstream-input-first rather than becoming an intake platform
5. Open `http://127.0.0.1:3000/pending`
6. Confirm Pending remains a review queue and single-item review workbench
7. Open `http://127.0.0.1:3000/expense`
8. Confirm Expense remains formal-record-first rather than becoming an analytics center
9. Open a knowledge detail page and confirm `Formal Content` and `AI-derived Summary` remain separate
10. Open `http://127.0.0.1:3000/health`
11. Confirm `Formal Records` and `Rule Alerts` remain separate

For deeper page-specific detail, use the lower-level Web baseline docs listed above.
