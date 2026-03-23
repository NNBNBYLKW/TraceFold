# Step 9 Chapter 2 Scope

## Purpose

Step 9 Chapter 2 is a weak-boundary cleanup pass. It is not a feature chapter and it is not a broad refactor.

This chapter exists to make current system boundaries more truthful and more controllable by:

- finding temporary compromises and weak constraints
- grading them explicitly
- fixing the low-cost, high-value ones now
- recording the rest as formal technical debt

## Why This Is Not Feature Work

The chapter does not expand TraceFold capabilities. It only reduces hidden risk in existing capabilities:

- no new business domain
- no new entry surface
- no new migration framework
- no new runtime stack

## Cleanup Model

Every issue item in this chapter must be placed in one category:

- `BLOCKER`
- `MUST_FIX_IN_PHASE1`
- `ACCEPTABLE_TECH_DEBT`
- `NOT_A_PROBLEM`

## Cleanup Focus Areas

This chapter checks:

1. entry-layer overreach
2. UI-layer second-truth risk
3. service / repository boundary weakening
4. formal fact / pending / rules / AI boundary drift
5. workbench and template expansion risk
6. config and script sprawl
7. docs / code / current-state mismatch
8. Phase 1 non-goal leakage

## First-Wave Fix Strategy

This chapter prefers:

- wording corrections that remove pseudo-completion risk
- configuration validation that turns soft assumptions into explicit constraints
- script-role clarification that prevents repo-style utilities from being mistaken for formal platforms

It explicitly avoids:

- large UI rewrites
- desktop runtime replacement
- migration-system introduction
- service-layer reshaping

## Current Chapter Judgment

At the start of Chapter 2, no new blocker was identified from Chapter 1 acceptance. The main risk shape is weaker than a blocker but still worth cleaning:

- soft config constraints
- script-role ambiguity
- skeleton/runtime wording drift
- test-runner / repo-bootstrap technical debt that must be documented honestly
