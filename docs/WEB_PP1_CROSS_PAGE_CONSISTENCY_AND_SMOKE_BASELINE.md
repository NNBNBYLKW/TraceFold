# Web PP1 Cross-Page Consistency And Smoke Baseline

## Purpose

This document closes the Post-Phase-1 Web optimization round for the current in-scope main lines.

It records:

- which pages were included in the closure
- which cross-page consistency rules were checked
- the final manual smoke order
- the final automated verification command
- what remains intentionally out of scope

It is a closure document, not a new redesign brief.

## Included Pages In This Closure

This closure applies only to:

1. Workbench / Dashboard
2. Knowledge detail + `knowledge_summary`
3. Health records + rule alerts
4. shared state semantics used by the pages above

It does not expand to Expense, Capture, Pending, Templates, Desktop, or Telegram.

## Cross-Page Consistency Rules Checked

### 1. Page-role consistency

The following roles remain fixed:

- Workbench = entry layer
- Dashboard = summary layer
- Knowledge detail = formal-first record read page with AI-secondary support
- Health = formal-first record read page with alerts-secondary support

### 2. Hierarchy consistency

The following hierarchy remains fixed:

- formal facts / formal records remain primary
- `Source Reference` remains contextual support
- `AI-derived Summary` remains secondary
- `Rule Alerts` remain secondary
- runtime status remains support-level where possible

### 3. Shared-state meaning consistency

The shared meanings remain:

- `loading` = waiting for required data
- `empty` = successful read, but no current data
- `unavailable` = relevant read failed or returned unusable data
- `degraded` = support warning or partial degradation, not automatic page collapse

Local state placement remains fixed:

- Knowledge derivation states stay inside the AI section
- Health alert states stay inside the alerts section
- Workbench degraded/runtime state stays support-level where possible

### 4. Action-weight consistency

The following actions remain intentionally light and local:

- `Recompute AI-derived Summary`
- `Acknowledge Alert`
- `Resolve Alert`

Workbench actions remain entry/context actions rather than control-panel actions.

## Final Manual Smoke Order

Run manual smoke in this order:

1. Open `/workbench`
   - confirm Workbench still reads as entry layer
   - confirm Dashboard summary reads as support summary rather than a competing center
   - confirm runtime/degraded state stays support-level where possible

2. Open one knowledge detail page
   - confirm `Formal Content` is primary
   - confirm `Source Reference` is contextual
   - confirm `AI-derived Summary` is secondary
   - confirm derivation state remains local to the AI section

3. Open `/health`
   - confirm `Formal Records` remain primary
   - confirm `Rule Alerts` remain a separate secondary reminder layer
   - confirm `alerts empty` and `alerts unavailable` are visibly different

4. Open one health detail page
   - confirm `Formal Record` appears before `Source Reference` and `Rule Alerts`
   - confirm alerts lifecycle grouping stays local to the alerts section
   - confirm no AI section, AI wording, or AI placeholder appears on Health pages

## Final Automated Verification Command

Use this as the final Web verification command for the Post-Phase-1 optimization round:

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py
```

After that, run:

```powershell
cd apps/web
npm run build
```

## Final Reading Order

Use this order for the current Web baseline and closure:

1. `docs/WEB_CONSUMPTION_BASELINE.md`
2. `docs/WEB_SHARED_STATE_POLISH_BASELINE.md`
3. `docs/WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
4. `docs/WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
5. `docs/WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
6. `docs/WEB_INFORMATION_HIERARCHY_CONTRACT.md`
7. `docs/WEB_PP1_CROSS_PAGE_CONSISTENCY_AND_SMOKE_BASELINE.md`

This keeps current formal baselines ahead of optimization-chain closure notes.

## Intentionally Out Of Scope

This closure does not introduce:

- new APIs
- new page centers
- AI center / alerts center / rule console / task runtime control center
- model / provider / prompt controls
- health AI or medical assistant UI
- design-system rewrite
- global state rewrite
- broad new page implementation waves

It also does not reopen frozen backend or product-boundary decisions.

## Closure Status

The current closure assumes:

- Workbench / Dashboard hierarchy polish has landed
- Knowledge detail formal-vs-AI polish has landed
- Health records / alerts formal-vs-alerts polish has landed
- Health pages contain no AI section / AI wording drift

If any of those cease to be true later, reopen a new scoped task instead of silently extending this closure.
