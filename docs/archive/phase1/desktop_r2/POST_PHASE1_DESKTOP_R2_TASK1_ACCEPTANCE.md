# Post-Phase-1 Desktop R2 Task 1 Acceptance

## Acceptance Goal

Desktop should behave like a steadier shell runtime, not just a minimum shell that happens to work.

This task is accepted when:

- lifecycle state fields are clearer
- shell actions and state updates stay aligned
- `window` and `tray` startup modes remain easy to explain
- `quit`, `hide`, `toggle`, and reopen behavior are less ambiguous
- docs and tests reflect the same lifecycle model

## What Was Hardened

- `resident` no longer starts in a fake already-running state
- runtime stop paths now clear shell presence more explicitly
- repeated `start_runtime()` calls no longer reopen the workbench while the runtime is already active
- tray-mode toggle behavior is protected by lifecycle tests
- `close()` now leaves a clearer stopped shell state

## What Was Not Changed

- Desktop still does not own business pages
- Desktop still does not own business logic
- Desktop still does not own write paths
- Desktop still opens the shared Web workbench rather than embedding a native business client

## Validation

The lifecycle baseline is protected by the Desktop test suite, including:

- `apps/desktop/tests/test_app.py`
- `apps/desktop/tests/test_lifecycle.py`
- `apps/desktop/tests/test_main.py`
- `apps/desktop/tests/test_window.py`
- `apps/desktop/tests/test_tray.py`

## Remaining Technical Debt

This task does not claim that Desktop is a fully hardened native runtime.
It is still not a fully hardened native runtime platform.

The remaining acceptable gaps include:

- minimal tray model rather than full OS-native integration
- no native business UI
- shell-level lifecycle only
