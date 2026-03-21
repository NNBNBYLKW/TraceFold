# Post-Phase-1 Desktop R2 Task 2 Tray Model

## Purpose

This document defines the minimum supported Desktop tray integration model after Desktop Runtime Hardening Round 2 Task 2.

It defines a tray-backed shell integration model, not a fully hardened OS-native tray platform.

## Tray Role

The tray has two supported roles only:

- shell presence indicator
- minimal shell action entry

The tray is not:

- not a business navigation tree
- a pending manager
- a template manager
- a shortcut manager
- not a second business interface

## Tray State Relationship

### `tray_visible`

Meaning:

- the shell currently exposes tray presence

### `resident`

Meaning:

- the shell is still resident through tray presence

In the current model:

- `resident` follows tray presence
- if `tray_visible = False`, then `resident = False`

### `window_visible`

Meaning:

- the shell window is currently visible

This is independent from tray presence:

- a tray-backed shell can stay resident while the window is hidden

### `runtime_started`

Meaning:

- the shell runtime has been started

Tray presence may exist after `bootstrap()` before `runtime_started = True`, but the supported daily runtime path is still `start_runtime()`.

## Supported Tray Actions

### `open`

Meaning:

- open the shared TraceFold workbench
- surface the shell window as part of that open path

Expected result:

- `workbench_status` moves toward `ready` or `error`
- `window_visible` reflects the resulting shell window state
- tray presence remains visible

### `toggle_window`

Meaning:

- if the window is visible, hide it
- if the window is hidden, show it

Expected result:

- tray presence stays visible
- the runtime stays resident
- the effective shell action is either `show_window`, `open_workbench`, or `hide_window`

`toggle_window` is a tray menu action. It is not a business action.

### `quit`

Meaning:

- explicit final shell exit path

Expected result:

- `quit_requested = True`
- `runtime_started = False`
- `tray_visible = False`
- `resident = False`
- `window_visible = False`

## Mode Semantics

### `window` mode

- the runtime starts with tray presence
- the shared workbench is opened immediately
- tray actions remain available for hide / reopen / quit

### `tray` mode

- the runtime starts with tray presence
- the workbench remains unopened until the user explicitly opens or shows it
- tray actions remain the primary shell action entry

## Action Tracking

The tray model now distinguishes:

- `last_menu_action`
  - the tray menu action the user invoked
- `last_shell_action`
  - the shell action that actually ran

This distinction prevents the tray model from blurring “which tray item was chosen” and “which lifecycle transition was executed”.

## Intentional Limits

The tray model remains intentionally small:

- it is still minimal shell integration
- it is not a fully hardened OS-native tray runtime
- it is not a business control surface
