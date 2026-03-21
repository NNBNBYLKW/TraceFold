# Post-Phase-1 Desktop R2 Task 2 Acceptance

## Acceptance Goal

Desktop tray integration should behave like a steadier shell entry, not just like a minimal placeholder menu.

This task is accepted when:

- tray role is explicit
- tray action semantics are explicit
- tray and lifecycle state stay aligned after tray actions
- `window` and `tray` mode tray behavior is easier to explain
- docs and tests reflect the same tray model

## What Was Hardened

- tray role is now explicitly documented as shell presence plus minimal shell action entry
- tray action tracking now separates menu action from effective shell action
- tray action results now carry a clearer shell-state snapshot
- tray reopen / hide / quit flows are protected by tray-oriented tests

## What Was Not Changed

- no business menu tree was added
- no Desktop business pages were added
- no Desktop-owned business logic was added
- no workbench role expansion was added

## Validation

The tray baseline is protected by the Desktop test suite, especially:

- `apps/desktop/tests/test_tray.py`
- `apps/desktop/tests/test_app.py`
- `apps/desktop/tests/test_lifecycle.py`
- `apps/desktop/tests/test_lifecycle_docs.py`

## Remaining Technical Debt

This task does not claim that Desktop now has a fully hardened OS-native tray runtime.

The remaining acceptable gaps include:

- a minimal tray integration model
- no advanced OS-native tray features
- shell-only tray actions
