# Web Knowledge AI Presentation Baseline

## Purpose

This document records the current Web presentation baseline for Knowledge detail and the formal `knowledge_summary` AI derivation.

It is intentionally narrow:

- Knowledge detail remains a formal record read page
- AI-derived summary is shown as a secondary interpretation layer
- the page consumes existing formal APIs only

It is not:

- an AI center
- a prompt configuration surface
- a model control panel
- a replacement for the formal knowledge record

## Consumed APIs

The current Knowledge detail page consumes:

- `GET /api/knowledge/{id}`
- `GET /api/ai-derivations/knowledge/{id}`

For the minimal recompute affordance, it also uses:

- `POST /api/ai-derivations/knowledge/{id}/recompute`

No new Web-only aggregation API was added for this page.

## Presentation Boundary

Knowledge detail now keeps two distinct sections:

1. `Formal Content`
2. `AI-derived Summary`

The boundary is explicit:

- formal content remains the record of truth
- AI-derived summary is generated from the formal record
- AI-derived summary does not replace the formal content
- recompute only requests a new derivation; it does not mutate the formal knowledge record

## Derivation State Handling

The page distinguishes these derivation states:

- `ready`: show summary, key points, keywords, and minimal derivation metadata
- `failed`: show a derivation failure message while keeping formal content available
- `invalidated`: show that the current derivation is stale and should be recomputed
- `pending` / `running`: show that recompute has been requested or is in progress
- `not generated`: show a non-error empty state for the derivation layer
- `unavailable`: show a derivation read failure without pretending the formal record failed

This is separate from formal detail failure. If `GET /api/knowledge/{id}` fails, the Knowledge detail page is unavailable even if an older derivation might exist elsewhere.

## Minimal Recompute

Knowledge detail includes one small action:

- `Recompute AI-derived Summary`

Its meaning is intentionally limited:

- request regeneration of the formal `knowledge_summary` derivation
- rely on the existing task-backed derivation runtime
- avoid turning the page into a task center or AI control surface

## Manual Smoke

With a fresh demo DB and the normal API/Web startup flow already running:

1. Open `http://127.0.0.1:3000/knowledge`
2. Open any seeded knowledge record
3. Verify the page shows:
   - `Formal Content`
   - `Source Reference`
   - `AI-derived Summary`
4. Verify the seeded demo path shows a ready `knowledge_summary`
5. Click `Recompute AI-derived Summary`
6. Verify the page refreshes and still keeps formal content separate from the AI section

Optional status checks:

- invalidated:
  - `POST /api/ai-derivations/knowledge/{id}/invalidate`
  - reload the page and confirm the invalidated message appears
- failed:
  - use the backend derivation failure test path rather than trying to trigger this through normal demo usage
- unavailable:
  - this is mainly a regression condition when derivation read fails while the formal detail read still succeeds

## Automated Verification

Recommended Web-focused checks:

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_knowledge_ai_ui_contract.py apps/web/tests/knowledge/test_knowledge_detail_ai_presentation.py
```
