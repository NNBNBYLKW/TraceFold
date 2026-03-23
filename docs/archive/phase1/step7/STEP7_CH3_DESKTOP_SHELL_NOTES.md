# Step 7 Chapter 3 Desktop Shell Notes

## Purpose

Chapter 3 defines the Desktop shell baseline for Step 7. The shell exists to
host minimal system-facing capabilities around the existing Web workbench and
HTTP API surface. It does not introduce a second business frontend.

## Directory Structure

- `apps/desktop/app/core`
- `apps/desktop/app/clients`
- `apps/desktop/app/shell`
- `apps/desktop/app/main.py`
- `apps/desktop/tests`

## Shell Boundary

- Desktop shell is a shell layer, not a business layer.
- Desktop shell does not import API domain services.
- Desktop shell does not connect to SQLite directly.
- Desktop shell does not own pending logic, rule evaluation, or AI workflows.
- Desktop shell uses existing HTTP status endpoints and the Web workbench URL.

## Current Skeleton Scope

- configuration loading
- shell assembly
- main window skeleton
- tray integration skeleton
- shell state skeleton
- service status client skeleton
- notification bridge skeleton

## Intentionally Not Wired Yet

- business UI pages
- pending review UI
- formal record detail UI
- tray interaction details
- notification rules
- shortcut details
- desktop-side write paths

## Why This Is Not A Second Frontend

The shell only knows how to hold state about shell visibility, probe service
status, and open the existing Web workbench URL. It does not own business
pages, business writes, or business state transitions.

## Web Workbench Hosting / Opening Flow

- The main window skeleton receives the configured Web workbench URL.
- `open_workbench` validates the URL and then opens or hosts that URL as the shell target.
- The shell keeps only minimal load state:
  `idle`, `loading`, `ready`, or `error`.
- If the URL is missing or invalid, the shell reports a minimal shell-side error state.
- This does not create a desktop business UI. The business surface still belongs to the Web workbench.

## Tray And Shell Lifecycle Behavior

- The tray exposes only three minimal menu items:
  - `Open TraceFold`
  - `Show Window` or `Hide Window`
  - `Quit`
- Opening the shell shows the main window and loads the configured Web workbench URL.
- Hiding the window keeps the shell resident.
- Quitting hides the window, removes the tray, and marks the shell as exiting.
- The tray is not a business navigation tree and does not expose pending, record, or write actions.

## Service Status Visibility

- Desktop reuses the existing status client and the Chapter 1 status endpoint.
- The shell checks service status once during bootstrap and may refresh it on an explicit shell action.
- The shell stores only minimal status fields:
  - available or unavailable
  - last checked timestamp
  - short error hint
- The current presentation is a minimal main-window status slot, not a diagnostics panel.
- The shell does not invent an offline write path or a second fact source when the service is unavailable.

## Minimal Notification Handling

- The current desktop shell only actively notifies for `service unavailable`.
- That notification is triggered only from the existing shell status check path:
  bootstrap or an explicit service check.
- The notification stays short and may point back to the workbench by exposing one shell action:
  `open_workbench`.
- `high-priority health alert` and `pending reminder` are intentionally not wired yet.
- They are deferred because the current desktop shell does not have a similarly natural, low-drift read path without adding more polling or alert semantics into the shell.
- This keeps Desktop as a shell-level notification receiver, not a notification platform or reminder engine.

## Final Conclusion

Chapter 3 is sufficient as the Step 7 desktop shell baseline: Desktop now wraps
the existing Web workbench and a minimal shell lifecycle without introducing a
second business frontend or a second business center.
