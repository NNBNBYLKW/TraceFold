# Step 9 Chapter 1 Scope

## Purpose

Step 9 Chapter 1 is a system-wide acceptance pass for Phase 1. It is not a new feature chapter and it is not a module-by-module reimplementation pass.

The chapter answers three questions:

1. Is TraceFold still one system?
2. Do Web, Telegram, Desktop, and Workbench still share one mainline and one business semantics layer?
3. Is the current Phase 1 system stable enough for long-term day-to-day use at the minimum acceptance level?

## Why This Is System Acceptance

This chapter does not stop at "the API responds" or "the page renders". It checks whether the completed parts from Step 3 through Step 8 still compose into one unified personal data workbench:

- one capture mainline
- one pending semantics
- one formal fact boundary
- one rules / AI enhancement boundary
- one shared entry meaning across Web, Telegram, and Desktop

## In Scope

- A system-level acceptance matrix across five fixed chains
- Minimal repeatable smoke tests
- Manual acceptance checklist
- A report template for pass / fail / risk recording
- Explicit blocker vs technical-debt classification

## Out of Scope

- New business domains
- New entry capabilities
- Rebuilding Step 3 through Step 8 implementations
- Real Telegram network E2E
- Real OS-level Desktop runtime E2E
- Step 8 UI redesign
- Future Step 9 feature planning

## Fixed Acceptance Dimensions

All Step 9 Chapter 1 judgments must use these dimensions:

1. Mainline consistency
2. Data boundary integrity
3. Multi-entry consistency
4. Workbench / Dashboard / Shortcut / Recent / Template role separation
5. Long-term usable baseline with visible failures and minimal recovery paths

## Covered Chains

- Chain A: Web mainline closure
- Chain B: Telegram entry through the shared mainline
- Chain C: Formal facts with rules / AI enhancement visibility
- Chain D: Desktop shell to shared Workbench
- Chain E: Template apply to real work context

## Current Reality Notes

- Desktop remains a shell-level runtime skeleton. This chapter records that as current reality and does not overstate it as a full native desktop runtime.
- Telegram remains a lightweight adapter. This chapter does not treat it as a second workbench.
- Workbench remains an entry layer. It does not replace the formal business pages.
