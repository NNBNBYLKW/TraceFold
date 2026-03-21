# Post-Phase-1 A3 Validation Notes

## Why This Exists

Task 1 to Task 3 already established the core A3 entry baseline, but the protection was scattered:

- Desktop runtime checks lived mostly in `apps\desktop\tests`
- startup/config baseline checks lived in a system contract test
- failure-signal checks lived across API, Web, Desktop, and docs

That scattered shape was acceptable while the tasks were landing, but it created avoidable maintenance friction. Task 4 does not add a bigger validation system. It adds one clear baseline document and one light contract so future maintainers can find the right minimum checks quickly.

## Validation Role Split

Keep these roles separate:

- `daily entry baseline validation`
  - the minimum A3 checks after entry-layer changes
- `broader system smoke`
  - wider integrated validation across the system
- `build validation`
  - compile/build confirmation for the Web app
- `support / setup scripts`
  - helper scripts, not daily validation entry points

Do not collapse these roles into one command with one meaning. That would make validation feel more centralized while actually making it easier to misread what has and has not been verified.

## Acceptable Remaining Friction

Some friction remains on purpose:

- the A3 minimum set is still three explicit commands rather than one umbrella runner
- broader system smoke remains separate
- Web build validation remains separate

This is acceptable because it keeps the roles clear:

- the maintainer can see what each layer validates
- the acceptance runner does not get silently promoted into the daily entry-validation path
- build success does not get misread as runtime validation

## Minimum Reading Order

When touching the A3 entry baseline, read in this order:

1. `docs/POST_PHASE1_A3_TASK1_ACCEPTANCE.md`
2. `docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md`
3. `docs/POST_PHASE1_A3_TASK3_FAILURE_SIGNALS.md`
4. `docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md`

That reading order keeps the runtime baseline, startup baseline, failure-signal baseline, and validation baseline aligned.
