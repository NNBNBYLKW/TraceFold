# Web Information Hierarchy Contract

## Purpose

This document freezes the page-level information hierarchy contract for the current Post-Phase-1 Web optimization round.

It answers:

- what counts as primary information on each included page
- what counts as secondary or contextual information
- where shared states should appear
- how action weight should be controlled
- how to keep the Web feeling like one workbench rather than several unrelated pages

It is not:

- a visual redesign manifesto
- a component-library rewrite plan
- a page copywriting spec
- a new product-definition document
- a new backend aggregation brief

For Sprint A, this contract is a freeze document first.
It defines hierarchy and role boundaries; it does not authorize a broad page redesign by itself.

---

## Current Scope

This contract applies only to the current main Web lines already covered by the formal Web consumption baseline:

1. Workbench / Dashboard
2. Knowledge detail + `knowledge_summary`
3. Health records + rule alerts

It also governs shared state presentation used by these pages.

It does not automatically expand to all other pages.

---

## Alignment With Product Shape

TraceFold is a personal data workbench rather than a collection of isolated pages.
The Web must support that shape by making the user feel:

- there is one center
- there is one main chain
- there is one shared interaction language
- there is one consistent distinction between facts, derived layers, and supporting states

The contract therefore optimizes for:

- unified workbench feeling
- low cognitive switching cost
- stable primary-vs-secondary signals
- readable local degradation
- restrained action emphasis

It must preserve one fixed distinction across all included pages:

- formal facts or page-defining read content are primary
- AI, alerts, and runtime/support states are secondary or contextual support

It does not optimize for:
- feature accumulation
- turning each page into a local center
- making AI visually dominant
- making alerts visually dominant
- expanding the Web into a task control surface

---

## Shared Hierarchy Vocabulary

Every included page should organize its information using the following semantic zones.

### 1. Primary Zone

The Primary Zone contains the page’s main truth-bearing or main task-setting content.

This is the first zone the user should understand.
It should visually carry the strongest reading priority.

Typical examples:
- Workbench entry context
- Dashboard summary blocks that define current system overview
- Knowledge formal content
- Health formal records

### 2. Secondary Zone

The Secondary Zone contains useful but non-primary interpretation, reminder, or supporting content.

It should be easy to discover, but it must not compete with the page’s primary truth surface.

Typical examples:
- AI-derived Summary on Knowledge detail
- Rule Alerts on Health
- secondary summary/supporting modules on Workbench

### 3. Contextual Zone

The Contextual Zone contains metadata, source references, explanatory labels, timestamps, provenance, or supporting notes.

It should help interpretation without displacing the page’s main reading flow.

Typical examples:
- source reference blocks
- derivation metadata
- runtime explanation copy
- section descriptions
- lifecycle labels and timestamps

### 4. Action Zone

The Action Zone contains page actions or section-local actions.

Actions must be weighted according to business meaning, not according to implementation visibility.

This round keeps action emphasis intentionally restrained.
The user should understand the page first, then act.

### 5. State Zone

The State Zone contains loading, empty, unavailable, degraded, derivation-state, or alert-state messages.

A State Zone must explain what is happening without falsely implying that unrelated sections failed.

State messaging should stay as local as possible.

---

## Cross-Page Hierarchy Rules

The following rules apply across all included pages.

### Rule 1 — Primary comes before secondary

Every page must make its primary content visually and structurally legible before its secondary layers.

### Rule 2 — Derived layers never outrank formal facts

AI-derived or rule-derived content may be useful, but it must not visually outrank formal record content.

Runtime and shared system-status support also must not outrank the page’s primary read surface.

### Rule 3 — Context should clarify, not compete

Source references, metadata, labels, and helper copy should support reading, not become the page center.

### Rule 4 — Actions should follow understanding

The page should not feel like a control panel first and a reading surface second.

### Rule 5 — State is local by default

If one section fails, the whole page should not pretend to have failed unless the primary read actually failed.

### Rule 5.1 — Support state never becomes page center by default

Runtime status, derivation state, and alert state may be important, but they should remain attached to the page section they explain unless the page’s primary read itself is unavailable.

### Rule 6 — Same meaning, not forced sameness

The same semantics should feel related across pages, but different page roles may still justify different layout details.

---

## Shared State Placement Rules

### Global page state

Use full-page state only when the page’s required primary read cannot be completed.

Examples:
- Workbench cannot load its required main inputs
- Knowledge formal detail fails
- Health primary record read fails

### Section-local state

Use section-local state when the failure or absence belongs only to one supporting layer.

Examples:
- derivation unavailable while formal knowledge content still loads
- alerts unavailable while formal health records still load
- degraded runtime block while workbench content remains readable

### Meaning requirements

The current aligned meanings remain:

- `loading`: waiting for required input
- `empty`: read succeeded but no current data exists
- `unavailable`: relevant read failed or is unusable
- `degraded`: warning-level condition exists while readable content may still remain

Derived states remain distinct from formal read states.

Alert states remain distinct from formal record states.

---

## Action Weight Rules

### High-level rule

No included page should read like a workflow engine, control center, or management console.

Actions should be present, but subordinate to understanding.

### Allowed action weights

#### Light action

Small local state-change or refresh action.
Use this for:
- `Recompute AI-derived Summary`
- `Acknowledge Alert`
- `Resolve Alert`

These actions should remain visible but not dominant.

#### Medium action

Navigation or section-entry action that helps the user continue work.
This may appear on Workbench when it helps enter a next page or mode.

#### Disallowed for this round

Do not introduce action emphasis that suggests:
- AI operator console
- alert operator console
- rule management console
- task runtime control center
- prompt/model configuration surface

Action weight must continue to reflect current product roles:

- Workbench actions help entry and continuation
- Knowledge actions help local derivation refresh
- Health actions help local alert lifecycle

---

## Workbench / Dashboard Hierarchy Contract

## Page Role

Workbench remains:
- an entry layer
- a context-setting page
- a page that helps the user understand “where the system stands now” and “where to go next”

Dashboard remains:
- a summary layer
- a high-level read surface
- not a replacement for formal domain pages

## Zone Ordering

### Primary Zone
The first thing Workbench should answer is:
- what the current overall situation is
- what matters now
- what the user can enter next

This zone may include:
- workbench home summary
- current actionable overview
- top-level dashboard signals
- workbench-level next-step entry points

### Secondary Zone
Secondary content may include:
- supporting summaries
- additional overview cards
- lower-priority aggregates
- locally degraded but still informative supporting blocks

Secondary content must not flatten the page into an undifferentiated dashboard wall.

### Contextual Zone
Context may include:
- runtime status explanation
- supporting labels
- timestamps or freshness hints
- section descriptions

Context must not overtake the main entry/situation signals.

### Action Zone
Workbench may include entry actions or navigation affordances, but they must reinforce:
- entering the correct context
- continuing into the correct domain page

They must not imply that Workbench itself is a second business center.

### State Zone
Workbench uses:
- `loading`
- `empty`
- `unavailable`
- `degraded`

`degraded` should appear as local warning-level support where possible.
Runtime/supporting degradation should not collapse the whole page when readable content remains.

## Specific Freeze

Workbench must feel like:
- a workbench entry page
- a context and direction page

It must not drift into:
- a giant all-in-one dashboard
- a business control center
- a page that tries to replace formal domain pages

---

## Knowledge Detail Hierarchy Contract

## Page Role

Knowledge detail remains:
- a formal record read page
- a page where formal content is the record of truth
- a page where AI-derived summary is secondary interpretation

## Zone Ordering

### Primary Zone
The first readable and dominant zone is:
- `Formal Content`

This zone may include:
- title
- body/content
- formal fields
- source reference entry
- truth-bearing structured information

### Secondary Zone
The secondary zone is:
- `AI-derived Summary`

This zone may include:
- summary
- key points
- keywords
- derivation-level helper metadata

This zone must remain visibly separate from formal content.

### Contextual Zone
Context may include:
- source reference
- derivation status note
- derivation timestamps/metadata
- explanation that AI is derived from the formal record

Context should clarify trust boundaries rather than compete with reading.

### Action Zone
The allowed action is:
- `Recompute AI-derived Summary`

Its weight must remain intentionally light.
It is a local refresh/regeneration request, not a page-level control center affordance.

### State Zone
Knowledge detail must distinguish:
- formal-detail unavailable
- derivation `ready`
- derivation `not generated`
- derivation `failed`
- derivation `invalidated`
- derivation `pending` / `running`
- derivation `unavailable`

If formal detail fails, the page is unavailable.
If only derivation fails, formal content remains readable and primary.

## Specific Freeze

Knowledge detail must feel like:
- a formal record page with a derived reading aid

It must not drift into:
- an AI center
- a model console
- a prompt surface
- a task management page
- a page where derived text visually overrides formal truth

---

## Health Hierarchy Contract

## Page Role

Health remains:
- a formal record read surface
- a page where rule alerts are secondary reminder layers
- not a medical judgment surface
- not an alerts management console

## Zone Ordering

### Primary Zone
The first readable and dominant zone is:
- `Formal Records`

This zone may include:
- current or selected health record detail
- structured formal measurements
- formal record context
- truth-bearing record content

### Secondary Zone
The secondary zone is:
- `Rule Alerts`

This zone may include:
- open alerts
- acknowledged alerts
- resolved alerts

Alerts remain reminders derived from record/rule logic.
They do not replace the formal record.

### Contextual Zone
Context may include:
- alert explanation
- lifecycle labels
- timestamps
- supporting note that alerts are separate from formal facts

Context should improve interpretation without turning the page into an operations console.

### Action Zone
Allowed actions:
- `Acknowledge Alert`
- `Resolve Alert`

These remain light lifecycle actions.
They should not read like workflow management or incident operations.

### State Zone
Health must distinguish:
- formal record unavailability
- `alerts empty`
- `alerts unavailable`
- alert lifecycle grouping: `open` / `acknowledged` / `resolved`

Alerts empty and alerts unavailable must not look like the same thing.

## Specific Freeze

Health must feel like:
- a formal health record page with secondary reminder layers

It must not drift into:
- an alert center
- a medical assistant UI
- a rule console
- a workflow board

---

## Source Reference Placement Rule

Where source reference appears, it should behave as contextual support, not as the primary reading center.

This is especially important on Knowledge detail:
- `Source Reference` supports the formal record
- it does not replace formal content
- it does not visually compete with the AI distinction

A similar principle applies to metadata blocks across pages:
they clarify provenance and status, but do not become the page center.

---

## Section Density Rule

This round aims to improve readability and density balance without inflating the number of concepts shown at once.

Therefore:

- prefer fewer clearer sections over more shallow panels
- prefer stable repeated section meanings over page-specific novelty
- prefer visible grouping over flat card accumulation
- prefer readable secondary blocks over noisy “helpful” side surfaces

This is a hierarchy contract, not a feature expansion excuse.

---

## Drift Checks

A proposed Web change violates this contract if it does any of the following:

- makes AI-derived content look like formal truth
- makes alerts look like page center
- makes Workbench look like a second business center
- makes actions visually dominate reading
- makes section-local failure collapse whole pages unnecessarily
- adds Web-owned business logic to compensate for weak hierarchy
- introduces Desktop-only or Telegram-only hierarchy semantics
- uses “consistency” as justification for full redesign

A quick check:

If the user can more quickly understand what is primary, what is secondary, and what failed locally without learning new system concepts, the change is likely aligned.

If the change introduces a new center, new authority, or new control surface, it is likely drift.

---

## Validation

This contract should be validated using:

- current Web consumption baseline
- current Knowledge AI presentation baseline
- current shared state polish baseline
- current Health alerts presentation baseline
- current Post-Phase-1 Web optimization charter
- existing Web tests and manual smoke paths

This round succeeds when:

- the page hierarchy becomes easier to scan and more consistent
- Workbench still reads as entry layer, not business center
- Dashboard still reads as summary layer, not formal-record replacement
- Knowledge detail still reads as formal-first, AI-secondary
- Health still reads as formal-first, alerts-secondary
- no out-of-scope center or control-surface semantics are introduced

Sprint A validation remains document alignment first.
If no implementation or tests are changed, consistency should be confirmed by doc cross-check rather than invented test output.
