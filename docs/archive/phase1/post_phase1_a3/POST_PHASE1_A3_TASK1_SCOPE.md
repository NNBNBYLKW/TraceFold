# Post-Phase-1 A3 Task 1 Scope

## Purpose

This task hardens the Desktop shell runtime so that it is usable day to day as
a shell-level entry. It does not add new business capability.

## In Scope

- make the Desktop startup path credible and documented
- stop the shell from starting and immediately exiting
- make `window` and `tray` startup modes actually affect runtime behavior
- keep Workbench opening on the shared `/workbench` URL stable
- keep tray/open/hide/quit shell behavior internally consistent
- make `service unavailable` feedback clear during startup and runtime checks

## Out Of Scope

- desktop-native business pages
- desktop-owned template or shortcut systems
- desktop-owned recent data sources
- pending / expense / knowledge / health native UI
- new write paths
- changes to the shared mainline semantics
- Desktop becoming a second frontend or business center

## Formal Entry Path

The formal Desktop startup path for this task is:

`python -m apps.desktop.app.main`

This is now intended to work from the repository root without requiring manual
bootstrap detours. Desktop settings prefer `apps/desktop/.env` and still allow
`.env` overrides when explicitly present.
