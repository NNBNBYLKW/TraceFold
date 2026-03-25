# Web Post-Phase-1 Optimization Plan

## Purpose

This document freezes the current Post-Phase-1 Web optimization direction for TraceFold.

It exists to answer:

- what the current Web optimization is trying to improve
- what the current Web optimization is explicitly not trying to become
- which pages are in scope for this round
- what sequence should be used to execute the work
- how to judge whether a proposed Web task is aligned or drifting

It is not:

- a backend restructuring brief
- a new product-definition document
- a design-system rewrite plan
- an AI expansion plan
- a Desktop or Telegram product plan

For Sprint A, this document is a freeze-and-alignment charter first.
It does not authorize implementation-heavy work by itself.

---

## Current Project Position

TraceFold is already past Phase 1.
The current stage is Post-Phase-1 restrained enhancement.

For the Web layer, this means the main formal consumption lines are already established:

1. Workbench / Dashboard
2. Knowledge detail + formal `knowledge_summary`
3. Health records + rule alerts
4. shared state semantics across the main pages

The current Web work is therefore not “build the main skeleton from scratch”.
It is “improve clarity, hierarchy, readability, and consistency on top of the existing formal consumption baseline”.

For the current freeze round, this means:

- preserve the already-established Web consumption lines
- freeze page roles before touching broader page polish
- prevent Task 3+ work from silently redefining product direction

---

## Product Alignment

The Web must continue to serve the final software shape rather than redefine it.

TraceFold remains:

- a local-first personal data workbench / personal data platform
- a system with one shared main chain:
  `Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation`
- a system whose application service layer is the only business-logic center
- a system where Web is the main business interface
- a system where Desktop is a shell-level entry
- a system where Telegram is a lightweight adapter
- a system where AI stays in derivation / explanation rather than fact authority

Therefore, the role of Post-Phase-1 Web work is:

- strengthen the workbench feeling
- reduce page-to-page drift
- improve information hierarchy
- keep formal facts primary and AI / alerts secondary
- keep runtime support as supporting state rather than a page center
- improve usability without creating a new product center

---

## Core Optimization Goal

The current Web optimization goal is:

**to turn the already-wired formal Web consumption lines into a clearer, more unified, lower-friction personal workbench experience without changing the system center, business authority, or product boundary.**

This goal decomposes into four concrete targets:

### 1. Stronger information hierarchy

The Web should make it easier to see:

- what is the primary content
- what is derived or secondary
- what is actionable
- what is only contextual
- what is unavailable vs merely empty vs locally degraded

### 2. Stronger cross-page consistency

The Web should reduce semantic and visual drift across:

- Workbench / Dashboard
- Knowledge detail
- Health

The goal is consistency of meaning, not full-page sameness.

### 3. Stronger workbench feeling

The user should more easily feel that the system is:

- one workbench
- one continuous context
- one set of state semantics
- one shared interaction language

rather than three isolated pages that merely happen to exist in the same app.

### 4. Better readability and action priority

The Web should improve:

- scanability
- visual priority
- local-state clarity
- section-level readability
- action de-emphasis where actions are intentionally small

This is especially important for:
- runtime status on Workbench
- derivation states on Knowledge detail
- alert lifecycle states on Health

---

## Non-Goals For This Round

This round does not introduce:

- an AI center
- an alerts center
- a rule management console
- a task runtime control center
- a model / prompt configuration UI
- a Web-owned business workflow layer
- Desktop-specific product semantics
- Telegram-specific product semantics
- a design-system rewrite
- a new global state platform
- a full-site component overhaul

This round also does not redefine:

- the system center
- the main chain
- the service-centered architecture
- the role of Desktop
- the role of Telegram
- the current AI scope

---

## In-Scope Pages

This round includes only the main Web lines that already have formal baselines:

1. Workbench / Dashboard
2. Knowledge detail + `knowledge_summary`
3. Health records + rule alerts
4. shared state semantics used by the pages above

This round may improve shared shells or shared presentation blocks only when that directly helps the pages above.
It is not a justification for broad whole-app redesign.

Explicitly out of scope for this charter round:

- Expense / Capture / Pending page redesign
- Templates product semantics
- new API-driven page centers
- implementation-heavy shared shell work beyond what later tasks may choose to adopt

---

## Page Role Freezes

### Workbench / Dashboard

Workbench remains:

- an entry layer
- a context-setting layer
- a place to understand the current system situation and where to go next

Dashboard remains:

- a summary layer
- a high-level read surface
- not a replacement for formal domain pages

### Knowledge Detail

Knowledge detail remains:

- a formal record read page
- a page where formal content is primary
- a page where AI-derived summary is secondary

`Recompute AI-derived Summary` remains:

- a small local action
- a request for derivation regeneration
- not an entry to a task center or AI control surface

### Health

Health remains:

- a formal record read surface
- a page where rule alerts are secondary reminder layers
- not an alert management console

`Acknowledge Alert` / `Resolve Alert` remain:

- small lifecycle actions
- local state transitions
- not workflow-center entry points

---

## Shared UI Semantics Freeze

The following meanings must remain aligned:

- `loading`: the page or section is still waiting for required inputs
- `empty`: the read succeeded, but there is no current data to show
- `unavailable`: the relevant read failed or returned unusable data
- `degraded`: a supporting system warning exists, but readable content may still remain

For derivations:

- `not generated`: no derivation exists yet; this is not an error
- `failed`: formal content remains readable, but derivation generation failed
- `invalidated`: an older derivation is stale and should be recomputed
- `pending` / `running`: regeneration has been requested or is still in progress

For alerts:

- `empty`: there are no current relevant alerts
- `unavailable`: alerts failed independently from formal record reads
- `open` / `acknowledged` / `resolved`: lifecycle states, not separate product modes

The purpose of this freeze is semantic consistency, not rigid visual uniformity.

Across all included pages, the fixed ordering principle is:

- formal facts or entry-defining content stay primary
- AI / alerts / runtime remain secondary or support layers
- actions remain subordinate to understanding

---

## Optimization Principles

### 1. Formal-first, derived-second

Formal records remain the primary truth surfaces.
AI summaries and rule alerts remain secondary interpretation or reminder layers.

### 2. Improve hierarchy before adding capability

If a page is already formally wired, prefer improving:
- layout
- emphasis
- section order
- empty / unavailable clarity
- action weight
before adding new capability.

### 3. Local degradation over page collapse

Where possible, supporting failures should degrade locally rather than collapse the whole page.
This is especially important on Workbench and on secondary sections like derivations or alerts.

### 4. Shared meaning over forced sameness

The target is not to make every page look identical.
The target is to make the meaning of states, sections, and actions legible in the same language.

### 5. Keep Web as interface layer

The Web may organize and present.
It must not become a second business-logic center.

### 6. Workbench feeling over feature accumulation

This round optimizes the sense that the user is operating inside one coherent workbench.
It does not optimize for adding more centers, more panels, or more system surfaces.

---

## Execution Sequence

The recommended execution order is:

### Phase A — Charter and hierarchy freeze

1. freeze the current Web optimization charter
2. freeze page-role and section-role hierarchy
3. freeze anti-drift checks for new Web tasks

### Phase B — Shared shell and shared state polish

1. unify page-level structure where it improves clarity
2. align section hierarchy language
3. align state block meanings
4. keep the scope narrow and local to the included pages

### Phase C — Workbench / Dashboard polish

Focus on:
- first-screen hierarchy
- clearer entry-vs-summary roles
- local degraded presentation
- better “what should I look at next?” readability

### Phase D — Knowledge detail polish

Focus on:
- Formal Content remaining visually primary
- AI-derived Summary remaining clearly secondary
- derivation states becoming easier to distinguish
- recompute becoming clearer but still intentionally small

### Phase E — Health polish

Focus on:
- Formal Records remaining primary
- Rule Alerts remaining secondary
- clearer lifecycle grouping
- clearer distinction between alerts empty and alerts unavailable
- action weight staying intentionally light

### Phase F — Cross-page consistency and smoke

Focus on:
- same semantic language across the three lines
- same section priority logic
- same understanding of empty / unavailable / degraded / derived-state meanings
- existing Web tests and manual smoke remaining green

---

## Deliverable Shape

The current round should produce:

- one frozen Web Post-Phase-1 optimization plan
- one frozen information hierarchy contract
- one execution sequence for later tasks
- one acceptance and drift-check baseline for future implementation work

For Sprint A specifically, the immediate deliverables are only:

- `docs/WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
- `docs/WEB_INFORMATION_HIERARCHY_CONTRACT.md`

Task 3+ remain future execution items, not outputs completed by this freeze.

---

## Acceptance Criteria

This round is successful when:

1. the user can more easily understand page priority at a glance
2. Workbench feels more like an entry workbench and less like an undifferentiated page
3. Knowledge detail makes formal-vs-AI distinction clearer without removing AI usefulness
4. Health makes record-vs-alert distinction clearer without turning into an alert console
5. shared state semantics are easier to read and less likely to drift across pages
6. no new Web-owned product center is introduced
7. no architecture boundary is weakened
8. existing tests and smoke paths for the included pages still pass
9. later Web tasks can be judged against a stable scope boundary before implementation begins

---

## Drift Checks

Any new Web task proposed under this plan should be rejected or rewritten if it trends toward any of the following:

- making AI look like fact authority
- turning alerts into a product center
- turning Workbench into a second business center
- creating a task control surface in Web
- adding Web-only aggregation APIs without strong need
- embedding business logic in page code
- introducing Desktop-only or Telegram-only semantic branches
- expanding the round into a full-site redesign
- using “consistency” as a reason to start a framework rewrite

A quick decision rule:

If a task improves hierarchy, readability, consistency, or workbench feeling on an already-wired formal page, it is likely in scope.

If a task introduces a new center, new authority, new control surface, or new product direction, it is likely drift.

Sprint A-specific drift check:

If a proposed edit starts specifying concrete page redesigns, new surface areas, or new product centers before hierarchy/role freeze is accepted, it is already out of scope for this run.

---

## Recommended Follow-up Docs

After this document is frozen, the next recommended doc is:

- `docs/WEB_INFORMATION_HIERARCHY_CONTRACT.md`

That follow-up doc should define:

- page-level primary / secondary / contextual zones
- section ordering rules
- action weight rules
- state-block placement rules
- cross-page hierarchy consistency rules

It should remain a UI contract document, not a visual redesign manifesto.

---

## Validation

Use the existing Web baseline docs and current Web tests as the primary validation path.

The validation style remains:

- baseline doc alignment
- local page smoke
- seeded demo DB verification
- existing Web contract tests
- existing page-specific Web tests

No separate large validation system should be invented for this round.

The minimum alignment references are:

- `docs/WEB_CONSUMPTION_BASELINE.md`
- `docs/WEB_SHARED_STATE_POLISH_BASELINE.md`
- `docs/WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
- `docs/WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
