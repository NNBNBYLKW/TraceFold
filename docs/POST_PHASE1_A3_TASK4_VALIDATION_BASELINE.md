# Post-Phase-1 A3 Validation Baseline

## Purpose

This document defines the minimum validation baseline for A3 entry-layer work. It exists so that a maintainer can answer one question quickly:

What is the minimum set of checks to run after changing Desktop runtime behavior, startup/config baseline behavior, or failure-signal/recovery wording?

## Daily Entry Baseline Validation

This is the minimum A3 validation set.

Run these three checks after entry-layer changes:

1. Desktop runtime minimum checks

```powershell
python -m pytest -q apps\desktop\tests
```

This protects the shell-only runtime baseline, including:

- startup path
- workbench open path
- tray / open / hide / quit behavior
- Desktop status wording
- Desktop failure-signal contracts

2. Startup and config baseline checks

```powershell
python -m pytest -q apps\api\tests\system\test_entry_startup_baseline_contract.py
```

This protects the Task 2 baseline for:

- recommended startup order
- recommended entry commands
- minimum config sources
- startup and recovery wording around API / Web / Desktop

3. Failure signal and recovery hint checks

```powershell
python -m pytest -q apps\api\tests\system\test_entry_failure_signal_contract.py apps\web\tests\test_semantics_contract.py apps\web\tests\test_workbench_home_contract.py apps\web\tests\test_health_ai_ui_contract.py apps\web\tests\test_knowledge_ai_ui_contract.py
```

This protects the Task 3 baseline for:

- API unavailable vs invalid response wording
- Desktop service-unavailable vs workbench URL failure wording
- AI not-generated vs failed wording
- recovery hints and formal-fact safety hints

## Broader System Smoke

This is not part of the daily A3 minimum validation set.

Use broader smoke when the change may affect the overall system posture, not just the entry-layer baseline.

```powershell
python apps\api\scripts\step9_ch1_acceptance_smoke.py
```

Broader smoke verifies the wider integrated system. It is not the first command to reach for after a small entry-layer wording or runtime adjustment.

## Build Validation

Build validation is also separate from the daily A3 minimum validation set.

```powershell
cd apps\web
npm run build
```

This validates the Web build output. It does not replace runtime or wording contracts.

## Support And Setup Scripts

Support and setup scripts are not validation entry points.

Examples:

- `apps/api/scripts/migrate_step8_workbench.py`
- `apps/api/scripts/step9_ch1_acceptance_smoke.py`

Their roles are different:

- the ensure script is a support script, not a formal migration framework
- the acceptance smoke runner is a broader validation runner, not the daily A3 entry baseline

## Mapping Back To Task 1 To Task 3

- Task 1 runtime hardening is primarily protected by `apps\desktop\tests`
- Task 2 startup/config/recovery baseline is primarily protected by `apps\api\tests\system\test_entry_startup_baseline_contract.py`
- Task 3 failure signals and recovery hints are primarily protected by:
  - `apps\api\tests\system\test_entry_failure_signal_contract.py`
  - `apps\web\tests\test_semantics_contract.py`
  - `apps\web\tests\test_workbench_home_contract.py`
  - `apps\web\tests\test_health_ai_ui_contract.py`
  - `apps\web\tests\test_knowledge_ai_ui_contract.py`
  - relevant Desktop tests in `apps\desktop\tests`

## Use This Baseline When

Use the daily A3 minimum validation set when a change touches:

- Desktop shell runtime behavior
- Desktop status or startup wording
- entry-layer config guidance
- startup order or startup commands
- failure signal wording
- recovery hints
- docs that define the Task 1 to Task 3 entry baseline

If the change goes beyond that, run the broader system smoke as well.
