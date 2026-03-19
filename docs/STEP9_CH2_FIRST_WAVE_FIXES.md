# Step 9 Chapter 2 First-Wave Fixes

This chapter completed a deliberately small first wave of real fixes. The goal was not broad refactor coverage; it was to harden a few soft boundaries that were cheap to improve and easy to misread later.

## Fix Set 1: Desktop shell config validation

- Files:
  - `apps/desktop/app/core/config.py`
  - `apps/desktop/tests/test_config.py`
  - `apps/desktop/.env.example`
- What changed:
  - `web_workbench_url` and `api_base_url` now require explicit `http://` or `https://`.
  - `startup_mode` now accepts only `window` or `tray`.
  - Config tests now assert rejection of invalid values.
- Boundary value:
  - Shell startup assumptions are now explicit instead of soft strings.
  - This reduces silent environment drift.

## Fix Set 2: Telegram adapter config validation

- Files:
  - `apps/telegram/app/core/config.py`
  - `apps/telegram/tests/test_config.py`
  - `apps/telegram/.env.example`
- What changed:
  - Blank bot token is rejected.
  - `api_base_url` now requires explicit `http://` or `https://`.
  - `timeout_seconds` must be positive.
  - Config tests now cover invalid values.
- Boundary value:
  - Adapter setup is less ambiguous.
  - Runtime failures move earlier and become clearer.

## Fix Set 3: Script-role clarification

- Files:
  - `apps/api/scripts/migrate_step8_workbench.py`
  - `apps/api/scripts/step9_ch1_acceptance_smoke.py`
- What changed:
  - Added explicit script-level wording that these are repo-style utilities.
  - Clarified that the acceptance runner is a smoke orchestrator, not a new framework.
  - Clarified that the workbench schema script is not a standalone migration system.
- Boundary value:
  - Reduces pseudo-completion wording.
  - Prevents repo utilities from being mistaken for hardened platform subsystems.

## First-Wave Result

The first wave did not change business semantics. It made the current boundaries more explicit and less fragile:

- entry config is stricter
- script roles are more honest
- env examples communicate the current shell/adapter role more clearly
