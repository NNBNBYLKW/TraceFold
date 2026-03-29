# Post-Phase-1 Baseline

## Purpose

This document is the current project-level baseline for TraceFold after Phase 1 closeout.

Read this first when you need to answer:

- what TraceFold is right now
- which boundaries are still frozen
- which backend and Web baselines are already formal
- which documents are the current hot entrypoints

This document is not an execution diary. Historical process evidence stays in `docs/archive/`.

## Current Project Posture

TraceFold is currently in a restrained Post-Phase-1 stage.

The project remains:

- a local-first personal data workbench / personal data platform
- centered on one shared business chain
- centered on the API service layer as the only business-logic authority
- backed by SQLite as the single source of truth

The main chain remains:

`Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation`

## Frozen Product And Architecture Boundaries

The current baseline still freezes these boundaries:

- Web is the main business interface
- Desktop is still a shell-level entry, not a desktop business client
- Telegram is still a lightweight adapter, not a management surface
- formal facts, rule alerts, AI derivations, and task runtime remain separate layers
- AI stays in the derivation / explanation layer and must not mutate formal facts
- task runtime stays an execution layer and must not become a second business center
- Workbench / Template / Shortcut / Recent / Dashboard role boundaries remain locked

Current non-goals still include:

- multi-tenant or distributed expansion
- cloud sync recentering
- plugin-platform expansion
- Telegram management workflows
- Desktop-heavy business workflows
- AI-led formal fact mutation
- notification-platform or workflow-engine expansion

## Current Formal Backend Baselines

The backend side is currently considered formally established in these areas:

- schema migration baseline
- unified error / logging / runtime status baseline
- minimal background task runtime baseline
- rule evaluation and alert lifecycle baseline
- AI derivation runtime baseline
- demo seed baseline for fresh local databases
- seeded integration smoke baseline for fresh demo DB recovery

These are the operational baseline documents:

- `docs/API_MIGRATION_BASELINE.md`
- `docs/API_ERROR_LOGGING_STATUS_BASELINE.md`
- `docs/API_TASK_RUNTIME_BASELINE.md`
- `docs/API_RULE_ALERT_BASELINE.md`
- `docs/API_AI_DERIVATION_BASELINE.md`
- `docs/API_DEMO_SEED_BASELINE.md`
- `docs/API_SEEDED_INTEGRATION_SMOKE.md`

## Current Formal Web Consumption Baselines

The Web side is currently considered formally established in these areas:

- Workbench / Dashboard consumption of shared API summary and runtime status
- Pending review workbench
- Capture visibility plus restrained minimal Web capture entry
- Expense formal consumption polish
- Knowledge detail consumption of formal content plus `knowledge_summary`
- Health page consumption of formal records plus rule alerts
- bounded zh/en Web UI support across shared shell, shared state, and current main surfaces
- shared state presentation for loading / empty / unavailable / degraded and derived-state feedback
- current Phase 2 V1 closure-ready additions recorded in `docs/PHASE2_V1_CLOSURE_BASELINE.md`

These are the Web baseline documents:

- `docs/WEB_CONSUMPTION_BASELINE.md`
- `docs/WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
- `docs/WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
- `docs/WEB_SHARED_STATE_POLISH_BASELINE.md`
- `docs/PHASE2_V1_CLOSURE_BASELINE.md`
- `docs/POST_PHASE2_BILINGUAL_UI_CLOSURE_BASELINE.md`
- `docs/PHASE3_V1_INPUT_PACK_CLOSURE_BASELINE.md`
- `docs/PHASE3_READBACK_DETAIL_DENSITY_CLOSURE_BASELINE.md`

## Current Web Closure Status

The first Post-Phase-1 Web main consumption lines optimization round is completed and closed.

That closed scope includes:

- Workbench / Dashboard
- Knowledge detail + `knowledge_summary`
- Health records + rule alerts
- shared shell / shared state alignment
- cross-page consistency and smoke closure

Within that closed round:

- Workbench remains the entry layer
- Dashboard remains the summary layer
- Knowledge detail remains formal-first, with AI-derived summary secondary
- Health remains formal-record-first, with rule alerts as a secondary reminder layer
- shared state meanings remain aligned across the included pages

This restrained enhancement round did not introduce an AI center, alerts center, rule console, task runtime control center, or Web-owned business center.
It records a completed polish pass on top of the already-established Web consumption baseline; it does not redefine the product center, the service-centered architecture, or the current Post-Phase-1 boundary.

A later restrained Phase 2 V1 enhancement pack is now also implementation-complete and closure-ready.

That Phase 2 V1 scope includes:

- Pending review workbench
- Capture list/detail plus restrained minimal Web entry
- Expense formal consumption polish
- Workbench Round 2 hierarchy and entry polish
- Templates as entry-only work modes
- local operability baseline for backup / restore, bounded import / export, runtime clarity, and daily-use readiness guidance

That closure-ready pack is recorded in:

- `docs/PHASE2_V1_CLOSURE_BASELINE.md`

This does not mean the whole product or the whole frontend is complete.
It records a bounded Phase 2 V1 closure-ready state only.

A later bounded bilingual Web UI support layer is also now implementation-complete and closure-ready.

That bounded closure includes:

- zh/en Web UI switching
- locale persistence
- shared shell and shared state copy switching
- page-level zh/en coverage for Workbench / Dashboard, Pending, Capture, Expense, Knowledge, and Health
- built-in template and mode localization where safe
- preserved raw, formal, user-authored, and AI-derived content boundaries

That closure-ready state is recorded in:

- `docs/POST_PHASE2_BILINGUAL_UI_CLOSURE_BASELINE.md`

This still does not mean backend i18n, API localization, Desktop / Telegram switching, or a generalized multilingual platform have been introduced.

A later bounded Phase 3 V1 input-side pack is also now implementation-complete and closure-ready.

That bounded closure includes:

- Quick Capture
- Bulk Intake with Preview
- Intake Triage / Capture Inbox

That closure-ready state is recorded in:

- `docs/PHASE3_V1_INPUT_PACK_CLOSURE_BASELINE.md`

This still does not mean broader Phase 3 work, desktop or Telegram quick entry, AI parsing/suggestions, or generic import platformization have been introduced.

A later bounded Phase 3 readback/detail-density pack is now also implementation-complete and closure-ready.

That bounded closure includes:

- detail hierarchy tightening across Pending, Capture, Expense, Knowledge, and Health detail pages
- readback/density polish around compact facts first, longer readback content second, and provenance/support later
- bounded Workbench / Dashboard readback-support polish without reopening entry-layer or center roles

That closure-ready state is recorded in:

- `docs/PHASE3_READBACK_DETAIL_DENSITY_CLOSURE_BASELINE.md`

This still does not mean broader Phase 3 work, charts/analytics expansion, AI expansion, dashboard-center growth, design-system work, or whole-frontend completion have been introduced.

## Desktop And Telegram Reality Check

Desktop and Telegram remain intentionally narrow:

- Desktop credibility has been improved at the shell level, but it is still not a desktop business client
- Telegram can still capture and read lightweight status, but it is still not a management console

The current hot supporting docs are:

- `docs/POST_PHASE1_DESKTOP_R2_ACCEPTANCE.md`
- `docs/TELEGRAM_BOT_SETUP_GUIDE.md`

## Recommended Reading Order

For current work, use this order:

1. `docs/POST_PHASE1_BASELINE.md`
2. `docs/DEVELOPMENT_ENTRYPOINTS.md`
3. `docs/WEB_CONSUMPTION_BASELINE.md`
4. `docs/PHASE2_V1_CLOSURE_BASELINE.md` when the task touches current Phase 2 V1 Web scope
5. `docs/POST_PHASE2_BILINGUAL_UI_CLOSURE_BASELINE.md` when the task touches current bounded Web bilingual UI support
6. `docs/PHASE3_V1_INPUT_PACK_CLOSURE_BASELINE.md` when the task touches current Phase 3 input-side Web scope
7. `docs/PHASE3_READBACK_DETAIL_DENSITY_CLOSURE_BASELINE.md` when the task touches current bounded detail/readback Web polish
8. `docs/PHASE1_SUMMARY.md`
9. `docs/ARCHITECTURE_RULES.md`
10. `docs/ENV_CONVENTIONS.md`
11. The specific `docs/API_*_BASELINE.md` or `docs/WEB_*_BASELINE.md` relevant to the task
12. `docs/archive/README.md` and then `docs/archive/phase1/**` only when historical traceability is needed

## Hot Docs Vs Archive

Keep using root-level baseline docs for:

- current project posture
- development and local integration entrypoints
- backend capability baselines
- Web consumption baselines
- Desktop / Telegram operating boundaries

Use archive docs for:

- Step / Chapter execution evidence
- one-off round or task acceptance materials
- closeout-era checklists, inventories, recovery notes, and process scaffolding

## Maintenance Guidance

When a new task starts, prefer updating the smallest current baseline document that already owns that topic.

Do not reopen archived execution materials as the default source of truth unless:

- a current baseline doc is missing a needed fact
- you need to trace why a boundary was frozen
- you need historical acceptance evidence
