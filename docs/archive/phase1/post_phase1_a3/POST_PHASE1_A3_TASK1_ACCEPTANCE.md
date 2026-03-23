# Post-Phase-1 A3 Task 1 Acceptance

## Acceptance Goal

Desktop should now meet the minimum daily-usable shell runtime bar:

1. it starts through the formal entry path without special detours
2. it opens the shared Workbench reliably
3. tray / open / hide / quit behavior is internally consistent
4. `service unavailable` is shown clearly instead of being implied or hidden

## What Counts As Passing

### Startup Path

- `python -m apps.desktop.app.main` is the formal entry
- startup no longer exits immediately after bootstrap
- startup mode is actually applied

### Workbench Opening

- `window` mode opens the shared `/workbench` target during startup
- tray-to-window flow opens Workbench if it was not already opened
- invalid or missing Workbench URLs still fail clearly

### Shell Behavior

- tray is visible after runtime start
- `open`, `show/hide`, and `quit` stay aligned with one shell action model
- quit stops the resident shell state cleanly

### Service Unavailable Feedback

- startup prints `Service status: unavailable` when the API cannot be reached
- shell state keeps the error hint
- service-unavailable notification behavior remains intact

## Validation Used

- `python -m pytest -q apps\\desktop\\tests`

Current result after this task:

- `26 passed`

## Remaining Honest Limits

These passing conditions do not mean:

- Desktop is now a heavy desktop-native client
- tray integration is a full OS-native runtime
- Desktop owns business pages or business logic

They only mean Desktop now behaves like a credible daily shell entry.
