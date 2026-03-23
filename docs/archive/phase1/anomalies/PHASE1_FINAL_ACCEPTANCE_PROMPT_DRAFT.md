# Codex Prompt Draft: Rewrite `PHASE1_FINAL_ACCEPTANCE.md`

## Purpose

Use Codex to rewrite a **repository-grounded, closeout-grade** version of:

- `PHASE1_FINAL_ACCEPTANCE.md`

This task is **not** to write a generic project summary.
It is to produce a final acceptance document that is:

- grounded in the **current repository reality**
- aligned with the **already frozen Phase 1 boundaries**
- explicit about **completed capabilities vs acceptable technical debt**
- careful **not to overstate maturity**
- suitable to act as a **project-level closeout document**

---

## Core instruction

Rewrite `PHASE1_FINAL_ACCEPTANCE.md` based on the **actual current repository state**, not just prior discussion summaries.

Before drafting, inspect the repository and relevant docs/tests/scripts first.
If docs and code disagree, prefer **current code/tests reality**, and explicitly note the mismatch rather than smoothing it over.

---

## Highest-priority constraints

### 1. Stay grounded in current repository reality

You must inspect the current repository before writing.

At minimum, review:

- relevant docs under `docs/`
- Step 8 and Step 9 closeout documents
- system tests
- acceptance runner / scripts
- key directory structure
- key entry/config files for:
  - API
  - Web
  - Desktop
  - Telegram

Do **not** invent or infer stronger capabilities than what the repository currently supports.

---

### 2. Preserve frozen project identity

TraceFold is:

- a **local-first personal data workbench / personal data platform**
- centered on the fixed mainline:

`Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation`

And:

- the **service layer** remains the only true business logic center
- Web / Desktop / Telegram are **entry surfaces**, not separate business centers

Do **not** rewrite the project identity, the mainline, or the architectural center.

---

### 3. Strictly distinguish three categories

The final document must clearly separate:

1. **Completed capabilities**
2. **Acceptable technical debt / acceptable gaps**
3. **Out-of-scope or future directions that are still not part of Phase 1**

These categories must not be blurred.

---

### 4. Do not overstate maturity

The document must **not** present the following as fully completed platform-grade capabilities:

- Desktop skeleton runtime
- repo-style ensure / bootstrap-style schema management
- minimum acceptance runner
- Web validation without browser E2E
- minimum enhancement layer as if it were a full AI/rules platform
- any skeleton / minimal / adapter-level capability as if it were fully hardened

If something is minimal, say it is minimal.

If something is a skeleton, say it is a skeleton.

If something is acceptable technical debt, say it is technical debt.

---

### 5. Do not reopen frozen boundaries

The document must not imply or normalize any of the following:

- Template as an automation engine
- AI directly mutating formal facts
- Desktop as the system’s true business client
- Telegram as a management client
- Knowledge as a graph-centered product core
- Health as a medical tool
- natural drift toward multi-tenant / microservices / MQ / cache / plugin system / enterprise observability

Those directions must remain clearly outside the Phase 1 acceptance boundary unless explicitly and narrowly documented as future possibilities outside current scope.

---

## Repository inspection requirements

Before drafting, inspect and summarize the repository state from at least these sources.

### A. Docs

Review closeout-relevant documentation, especially:

- Step 8 scope / contracts / acceptance / drift / closeout docs
- Step 9 Chapter 1 to Chapter 6 docs
- especially Chapter 6 docs:
  - `STEP9_CH6_PHASE1_DEFINITION_OF_DONE.md`
  - `STEP9_CH6_PHASE1_COMPLETED_CAPABILITIES.md`
  - `STEP9_CH6_PHASE1_ACCEPTABLE_GAPS_AND_TECH_DEBT.md`
  - `STEP9_CH6_PHASE1_LOCKED_BOUNDARIES.md`
  - `STEP9_CH6_POST_PHASE1_BASELINE.md`

### B. Tests and scripts

Review current evidence sources, especially:

- system tests
- acceptance smoke runner
- workbench tests
- desktop regression tests
- telegram regression tests
- web contract/build validation

### C. Core code structure

Inspect at least:

- `apps/api`
- `apps/web`
- `apps/desktop`
- `apps/telegram`

And especially:

- workbench-related code
- config and entry paths
- desktop shell layer
- telegram adapter / formatter / config
- key API router/service structure

### D. Real Phase 1 status

Identify what is currently:

- fully closed for Phase 1
- minimum-but-acceptable
- skeleton / adapter-level
- explicit technical debt

---

## Writing goal

Produce a **repository-aligned Phase 1 final acceptance document** that lets a new contributor understand:

- why Phase 1 is considered complete
- what is truly complete
- what remains incomplete but acceptable
- which boundaries are now locked
- where all future work must continue from

This document should function as:

- the project-level final Phase 1 acceptance statement
- a closeout companion to `POST_PHASE1_BASELINE`
- a truthful summary of current readiness, without marketing language

---

## Required output structure

Write the final document using the following structure.

# TraceFold Phase 1 Final Acceptance

## 1. Final Verdict
State clearly:

- whether Phase 1 is formally complete and closed out
- what that verdict is based on
- that this does **not** mean “everything is perfect” or “every future-facing capability is complete”
- that it means the **Phase 1 goals** have been met

---

## 2. What Phase 1 Was Actually Supposed to Achieve
Restate the real Phase 1 target in a restrained way.

Must emphasize:

- unified mainline
- unified service center
- local-first / SQLite-centered foundation
- Web as the primary business UI
- Desktop shell and Telegram adapter as bounded entry surfaces
- minimum rules / AI enhancement layer
- workbench homepage entry layer

Do **not** turn this into a future roadmap.

---

## 3. Why Phase 1 Is Considered Complete
Explain why Phase 1 can now be considered complete.

Base this on current repository reality, especially:

- mainline closure
- formal read layer
- dashboard/workbench layer
- multi-entry consistency
- rules / AI minimum enhancement layer
- Step 9 closeout outcomes

This section should feel grounded in the actual current repo, not abstract.

---

## 4. Completed Capabilities
List the capabilities that should now be considered completed **within the meaning of Phase 1**.

Each item should include a brief justification.

Use restrained wording such as:

- “minimum enhancement layer is established”

and **not** inflated wording such as:

- “advanced AI platform is complete”

---

## 5. Acceptable Gaps and Technical Debt
List the remaining acceptable gaps / technical debt.

For each item, explain:

- what the gap is
- why it does not block Phase 1 closeout
- why it must **not** be described as already complete

This section should explicitly address items such as:

- Desktop skeleton runtime
- repo-style ensure / no formal migration framework
- no browser E2E on Web
- `datetime.utcnow()` warnings
- missing user-facing `not run` state for rule alerts, if still true
- pytest collection conflicts, if still true

---

## 6. Locked Boundaries
State the boundaries that are now formally locked.

Must include at least:

- project center
- mainline
- service center
- formal facts / pending / rules / AI derivations boundary
- AI cannot mutate formal facts
- Desktop / Telegram boundary
- Template / Shortcut / Recent / Dashboard role boundary
- Phase 1 non-goals

This section should make it harder for future work to casually reopen foundational questions.

---

## 7. Conditions That Would Invalidate This Acceptance
List the kinds of changes or discoveries that would undermine the statement that “Phase 1 is complete.”

Examples include:

- second business center
- special write path
- AI mutating formal facts
- Desktop or Telegram boundary drift
- template automation drift
- documentation overstating actual maturity

This helps keep the closeout honest.

---

## 8. Post-Phase-1 Baseline
Explain clearly where future work must continue from.

Must include:

- which documents form the baseline
- what future contributors must read first
- what boundaries new work must check before proceeding
- why later tasks should **not** reopen the project center or re-argue whether Phase 1 is complete

---

## 9. Closing Statement
End with a concise, formal statement that:

- Phase 1 is now closed out
- future work must proceed from the post-Phase-1 baseline
- known gaps are recorded explicitly and must not be rebranded as completed capability

---

## Required preface before the final document

Before the final document body, include a short preface containing:

### A. What you inspected
List the major docs / tests / scripts / folders you checked.

### B. Two key tightening judgments
State the two most important judgment calls you made, such as:

- which items must not be described as completed capability
- which boundaries had to be reinforced to keep the final acceptance truthful

### C. Any risk of overstating reality
If you found places where the final acceptance could easily be written “too full” or too optimistic, call them out first.

---

## Style requirements

The writing must be:

- formal
- restrained
- repository-grounded
- trustworthy

Do **not** write:

- celebratory copy
- marketing language
- inflated maturity language
- vague “project is great” statements

Prefer:

- precise claims
- careful scope statements
- explicit distinction between complete vs acceptable gap
- language that matches current code/tests/docs reality

---

## Final instruction

Your job is **not** to make the document sound more impressive.

Your job is to make it:

- more detailed
- more repository-accurate
- more usable as a closeout document
- still appropriately conservative
- impossible to misread as overstating Phase 1 maturity