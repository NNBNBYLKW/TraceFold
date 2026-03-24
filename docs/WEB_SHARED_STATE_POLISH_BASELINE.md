# Web Shared State Polish Baseline

## Purpose

This document records the current shared Web state presentation baseline for the main post-Phase-1 pages already wired to formal API reads.

It is intentionally narrow:

- keep loading / empty / unavailable / degraded easier to distinguish
- keep derivation and alert state language more consistent
- reduce page-to-page drift without turning the Web app into a new framework project

It is not:

- a design-system rewrite
- a new global state platform
- a full-site component overhaul

## Current Shared State Semantics

The current baseline keeps these meanings aligned:

- `loading`: the page is still waiting for its shared API inputs
- `empty`: the API succeeded, but the current page or section has no usable data yet
- `unavailable`: the relevant API route could not be reached or returned an unusable response
- `degraded`: the shared system status reports warnings, but readable content may still remain available

For derivations:

- `failed`: formal content remains readable, but the derivation failed
- `invalidated`: the previous derivation is stale and should be recomputed
- `not generated`: the derivation does not exist yet and this is not treated as an error

For alerts:

- `empty`: there are currently no relevant alerts
- `unavailable`: the alerts API failed independently from the formal record read

## Pages Included In This Round

This polish round covers:

1. Workbench / Dashboard
2. Knowledge detail + AI-derived summary
3. Health records + rule-based alerts

It does not attempt to make every page fully identical. The goal is consistency of meaning, not full UI sameness.

## Current Presentation Rules

### Workbench / Dashboard

- use the shared runtime status panel for `ready`, `degraded`, and `unavailable`
- allow partial content to remain readable when supporting inputs degrade
- keep workbench summary failure as a local degraded block rather than collapsing the whole page

### Knowledge Detail

- formal content remains the primary section
- AI-derived summary keeps its own secondary section
- not generated, failed, invalidated, pending, and unavailable stay visibly distinct

### Health

- formal health records remain the primary section
- alerts remain a separate reminder layer
- alerts use distinct empty and unavailable states
- alert lifecycle states stay grouped rather than flattened into one undifferentiated list

## Manual Smoke

With a fresh demo DB and the normal API/Web startup flow already running:

1. Open `http://127.0.0.1:3000/workbench`
2. Confirm system status can show:
   - ready
   - degraded
   - unavailable
3. Open a knowledge detail page and confirm:
   - `Formal Content`
   - `AI-derived Summary`
   - distinct not-generated / failed / invalidated wording when applicable
4. Open `http://127.0.0.1:3000/health`
5. Confirm:
   - `Formal Records`
   - `Rule Alerts`
   - alerts empty and alerts unavailable are not presented as the same thing

## Automated Verification

Recommended Web-focused checks:

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py apps/web/tests/health/test_health_alerts_consumption.py apps/web/tests/shared/test_shared_state_polish.py
```
