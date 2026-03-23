# Step 9 Chapter 6 Post-Phase-1 Baseline

## The Only Baseline To Continue From

After Phase 1 closeout, later work should start from these documents together:

1. `STEP9_CH6_PHASE1_DEFINITION_OF_DONE.md`
2. `STEP9_CH6_PHASE1_COMPLETED_CAPABILITIES.md`
3. `STEP9_CH6_PHASE1_ACCEPTABLE_GAPS_AND_TECH_DEBT.md`
4. `STEP9_CH6_PHASE1_LOCKED_BOUNDARIES.md`
5. `STEP9_CH5_PHASE1_NON_GOALS_REVIEW.md`
6. `STEP9_CH5_DRIFT_TRIGGER_TABLE.md`
7. `STEP9_CH4_MINIMUM_STABILITY_CHECKLIST.md`
8. `STEP9_CH1_ACCEPTANCE_MATRIX.md`

## Required Reading Order For New Work

Before starting a later task:

1. read the Phase 1 Definition of Done
2. read the locked boundary summary
3. read the acceptable gaps / tech debt summary
4. read the non-goals review and drift-trigger table
5. read the closest acceptance matrix or stability checklist that applies

## Rule For Later Tasks

Any new task after Phase 1 must explicitly answer:

- Does it touch the locked mainline?
- Does it touch the AI boundary?
- Does it change entry responsibilities?
- Does it change Workbench role separation?
- Does it reduce or increase an accepted technical debt item?
- Does it reopen a Phase 1 non-goal?

If the answer to any of those is “yes”, the task must say so before implementation instead of treating the boundary as implicitly reopened.

## What Later Work Must Not Reopen By Default

- project center
- unified mainline definition
- service-layer primacy
- entry-only role of Desktop and Telegram
- AI derivation-only boundary
- Workbench role separation
- recorded acceptable technical debts
