# Post-Phase-1 Desktop R2 Task 3 Launch Model

## Purpose

This document defines the minimum supported Desktop launch model after Desktop Runtime Hardening Round 2 Task 3.

It defines a more ergonomic shell launch path, not a desktop product or distribution platform.

## Recommended Daily Entry

Recommended repo-root launch command:

`python -m apps.desktop`

This is now the preferred day-to-day launch path because it is shorter, clearer, and still maps directly onto the existing Desktop shell runtime.

## Lower-Level Entry Path

Lower-level compatibility path:

`python -m apps.desktop.app.main`

This path still works and remains useful for low-level debugging, but it is no longer the primary recommended daily entry.

## Repo-Root Usage

The current intended repo-root daily flow is:

1. start the shared API
2. start the shared Web app
3. run `python -m apps.desktop`

This keeps Desktop aligned with the shared system while making the daily shell entry shorter and easier to remember.

## Startup Feedback Semantics

Startup feedback should let the user see, quickly:

- startup mode
- workbench target
- workbench state
- service status
- whether the shell has entered a usable daily-entry state

The feedback is still shell-oriented. It is not a desktop diagnostics console.

## Window Mode Daily Behavior

In `window` mode:

- the Desktop shell starts
- tray presence is established
- the shared workbench is opened immediately
- the user should be able to treat startup as “open the shell and enter the shared workbench”

## Tray Mode Daily Behavior

In `tray` mode:

- the Desktop shell starts
- tray presence is established
- the shared workbench remains unopened until the user explicitly opens it
- startup feedback should make that tray-resident state obvious

## Intentional Limits

This launch model is still intentionally small:

- no installer
- no updater
- no packaging platform
- no claim of a fully hardened desktop product experience
