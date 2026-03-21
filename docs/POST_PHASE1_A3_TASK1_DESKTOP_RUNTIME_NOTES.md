# Post-Phase-1 A3 Task 1 Desktop Runtime Notes

## What Was Wrong Before This Task

Before this task, the Desktop shell boundary was already correct, but the
runtime was still too close to a scaffold:

- `main()` only bootstrapped the shell and then immediately closed it
- startup mode existed in config, but it did not actually drive the runtime
- `window` startup still required extra manual calls to feel like a real shell
- `tray` state existed, but it was not applied as a coherent startup behavior
- service status lived in shell state, but startup feedback was not visible
  enough for daily use

## What This Task Changes

- the Desktop entry now starts the runtime instead of bootstrapping and
  immediately exiting
- `window` startup opens the shared Web Workbench automatically
- `tray` startup keeps the shell resident without auto-opening the window
- showing the window from tray state now opens Workbench if it has not already
  been opened
- tray actions now route through one shell-level action path
- startup now prints minimal, truthful shell feedback:
  - startup mode
  - workbench target
  - workbench state
  - service status
  - service error hint when unavailable

## Current Runtime Shape

This task makes Desktop a daily usable shell runtime, not a desktop-native
business client.

Desktop now provides:

- a formal startup path
- a resident shell runtime
- automatic Workbench opening in `window` mode
- minimal tray/open/hide/quit consistency
- explicit `service unavailable` startup feedback

Desktop still does not provide:

- native business pages
- desktop-owned business logic
- desktop-only data flows
- a fully native tray runtime with complex OS integrations

## Boundary Reminder

Desktop remains shell-only:

- it opens the shared Web workbench
- it shows minimal shell status
- it surfaces minimal shell notifications
- it does not own business semantics

This task hardens the runtime. It does not expand the product surface.
