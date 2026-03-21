# Post-Phase-1 Desktop R2 Task 3 Acceptance

## Acceptance Goal

Desktop launch should feel more like a practical daily shell entry and less like a low-level engineering path.

This task is accepted when:

- the recommended daily launch command is shorter and clearer
- the lower-level path remains available but clearly secondary
- startup feedback is easier to scan
- the daily “open shell and enter the shared workbench” path is more direct

## What Was Hardened

- `python -m apps.desktop` is now the recommended repo-root entry
- `python -m apps.desktop.app.main` remains available as a lower-level compatibility path
- startup feedback now includes a clearer daily-entry line
- startup feedback now prints the recommended launch command explicitly

## What Was Not Changed

- no installer or packaging layer was added
- no Desktop-owned business UI was added
- no Desktop-owned business logic was added
- Desktop still opens the shared Web workbench rather than embedding a native business client

## Validation

The launch baseline is protected by:

- `apps/desktop/tests/test_main.py`
- `apps/desktop/tests/test_launch.py`
- the wider Desktop test suite

## Remaining Technical Debt

This task does not claim that Desktop now has a fully polished desktop-product launch experience.

The remaining acceptable gaps include:

- no installer or updater
- still a shell-level entry
- still an engineering repo runtime, not a packaged desktop distribution
