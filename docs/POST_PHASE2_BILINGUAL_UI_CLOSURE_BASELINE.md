# Post-Phase-2 Bilingual UI Closure Baseline

## Purpose

This document records the closure-ready state for the bounded bilingual Web UI support already implemented on top of the current Post-Phase-1 and Phase 2 V1 baselines.

Use it to answer:

- what the current bilingual UI support actually includes
- which current Web surfaces are covered by zh/en UI copy switching
- which content and data boundaries remain intentionally preserved
- what this closure does not claim

It is a closure document, not a new multilingual architecture document and not a new feature charter.

## Closure-Ready Included Scope

The current bounded bilingual Web UI support includes only:

1. zh/en locale switching in the Web UI
2. local locale persistence after manual user selection
3. shared shell copy switching
4. shared state copy switching
5. page-level zh/en UI copy coverage for the current main surfaces:
   - Workbench / Dashboard
   - Pending
   - Capture
   - Expense
   - Knowledge
   - Health
6. built-in template and mode label localization where the distinction is safe
7. local continuity and runtime wrapper wording where that wording is owned by the Web UI

This document does not claim whole-product multilingual completion or a generalized localization platform.

## Preserved Content And Data Boundaries

The bounded bilingual UI support preserves these boundaries:

- backend and application-layer semantics remain unchanged
- backend payloads are not localized
- formal record values remain raw stored values
- raw capture content remains raw stored content
- user-entered notes and text remain user-authored values
- AI-derived content remains untranslated
- user-defined template and mode names remain user-authored values

Only built-in or frontend-owned UI copy is localized.

## Cross-Page Consistency Freeze

Across the current covered surfaces:

- shared shell wording remains coherent in both zh and en
- shared `loading`, `empty`, `unavailable`, and `degraded` semantics remain aligned
- page-level bilingual copy preserves the same role and hierarchy boundaries already frozen in the Web baseline
- built-in action wording remains consistent where centrally expressed

This bounded bilingual layer must not drift into:

- backend i18n
- API payload localization
- AI translation
- Desktop or Telegram language switching
- a heavy i18n platform or generalized multilingual system

## Surface Coverage Notes

The current page-level bilingual closure applies to:

### Workbench / Dashboard

- entry-layer, summary-support, template-entry, runtime, and local-continuity UI wording
- without changing Workbench, Dashboard, or Template roles

### Pending

- queue and detail workbench UI copy
- review-action explanations and resolved/non-actionable UI wording
- without changing review semantics

### Capture

- list/detail shell copy, linkage labels, and minimal entry wrapper copy
- without translating raw captured content

### Expense

- list/detail formal-consumption copy and provenance/context wrapper copy
- without introducing analytics-center behavior

### Knowledge

- formal-content shell copy and AI-summary wrapper/state copy
- without translating AI-derived summary content

### Health

- formal-record shell copy, rule-alert wrapper copy, and alert-lifecycle headings
- without introducing Health AI or assistant-style behavior

## What This Closure Explicitly Does Not Include

This bounded bilingual UI closure does not include:

- backend i18n
- localized API payloads
- AI translation
- translation of raw or user-authored data
- translation of formal fact values
- translation of raw capture content
- translation of AI-derived content
- forced translation of user-defined template or mode names
- Desktop or Telegram language switching
- generalized multilingual platformization

## Bounded Closure Verification Record

If a live browser-click smoke is not available in the current environment, use this bounded substitute:

- route and render inspection of the current covered pages
- existing Web locale tests
- shared semantics tests
- Web build verification from the implementation round
- explicit code inspection of the raw-content and user-content render paths

Do not claim a live browser-click smoke if it did not happen.

## Closure Status Meaning

The bounded bilingual Web UI support may be treated as formally closed when:

- the included scope above is recorded in the current docs entrypoints
- the shared-copy and page-level role boundaries remain aligned
- preserved content/data boundaries remain explicit
- closure verification is recorded without overclaiming broader multilingual completion

This still does not mean:

- backend multilingual support exists
- all future Web copy is already covered
- Desktop or Telegram switching exists
- the whole frontend or whole product is complete
