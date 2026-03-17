# Step 7 Chapter 3 Acceptance Smoke

## Chapter 3 Purpose

Chapter 3 accepts Desktop as a shell layer on top of the Chapter 1 API and the
existing Web workbench. The goal is to provide a desktop-style entry wrapper,
not to create a second business frontend.

## What Desktop Does In Step 7

- Loads configuration for the shell entry.
- Holds a minimal shell state.
- Opens or hosts the configured Web workbench URL.
- Exposes minimal tray behavior:
  - `Open TraceFold`
  - `Show Window` or `Hide Window`
  - `Quit`
- Keeps a minimal resident shell lifecycle.
- Shows minimal service status visibility.
- Performs one bootstrap status check and supports one explicit status refresh.
- Accepts one minimal notification type:
  `service unavailable`

## What Desktop Intentionally Does Not Do

- Does not rewrite the Web workbench as native business UI.
- Does not connect to the database directly.
- Does not call internal domain services.
- Does not implement independent pending logic.
- Does not implement independent rule evaluation.
- Does not implement independent AI flows.
- Does not expose a desktop-only write path.
- Does not become a notification platform.
- Does not implement complex tray navigation, reminder logic, or OS-level E2E runtime in this chapter.

## Minimal Smoke Scenarios

1. Desktop shell assembly loads configuration and bootstraps.
2. Desktop opens the configured Web workbench URL.
3. Missing or invalid workbench URL enters a minimal shell error state.
4. Tray menu exposes only the minimal shell actions.
5. Desktop can hide the window while remaining resident.
6. Desktop can show the window again from shell state.
7. Desktop can quit from shell state.
8. Bootstrap performs one service status check.
9. Explicit `check_service_status()` performs one manual refresh.
10. Service unavailable triggers one minimal desktop notification.
11. Notification action can reopen the Web workbench path.

## Why Desktop Remains A Shell

- Desktop carries or opens the Web workbench instead of rebuilding business pages.
- Desktop only consumes existing HTTP status capability and shell-local state.
- Desktop does not bypass the service/API boundary.
- Desktop does not hold business records, business routing, or business transitions.
- Desktop behavior stays limited to window, tray, status visibility, and minimal notification handling.

## Why This Is Enough For The Next Step

- Chapter 3 already proves the desktop entry can exist without becoming a second business frontend.
- The shell boundary is now explicit:
  no DB access, no domain service calls, no independent pending or rule logic.
- The minimal shell lifecycle and service visibility paths are already test-covered.
- This is sufficient to move into Step 7 final consistency validation without reopening desktop architecture.
