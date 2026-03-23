# Post-Phase-1 Desktop R2 Task 1 Lifecycle Model

## Purpose

This document defines the minimum supported Desktop shell lifecycle model after Desktop Runtime Hardening Round 2 Task 1.

It describes a shell runtime model, not a full native desktop runtime platform.

## State Fields

### `runtime_started`

Meaning:

- the Desktop shell runtime has been started through `start_runtime()`
- the shell has entered its running lifecycle

It does not mean:

- the shared API is healthy
- the workbench opened successfully
- the Desktop runtime is a fully hardened native runtime

### `resident`

Meaning:

- the shell currently has active tray presence
- the shell is still resident as a tray-backed shell runtime

It does not mean:

- the workbench is open
- the shell is visible
- the API is healthy

### `window_visible`

Meaning:

- the shell window is currently visible

It does not mean:

- the workbench is healthy
- the API is healthy

### `tray_visible`

Meaning:

- the shell tray presence is currently visible

In the current lifecycle model, `resident` follows tray presence.

### `service_status`

Meaning:

- the last known shell-side service reachability result

Current supported values:

- `ok`
- `unavailable`
- `invalid_response`
- `error`
- `unknown`

### `workbench_state`

Meaning:

- the last known shell-side workbench open result

Current supported values:

- `idle`
- `loading`
- `ready`
- `error`

## Supported Lifecycle Flow

### Before bootstrap

- `runtime_started = False`
- `resident = False`
- `tray_visible = False`
- `window_visible = False`

### After `bootstrap()`

- tray presence is shown
- `resident = True`
- `runtime_started = False`

This is an initialized shell state, not yet the full runtime-started state.

### After `start_runtime()` in `window` mode

- `runtime_started = True`
- `resident = True`
- `tray_visible = True`
- the shell opens the shared workbench
- `window_visible` follows the open result

### After `start_runtime()` in `tray` mode

- `runtime_started = True`
- `resident = True`
- `tray_visible = True`
- `window_visible = False`
- `workbench_state` may remain `idle` until the user explicitly opens the workbench

### `show_window()`

- makes the window visible
- opens the workbench if the shell does not already have a ready workbench state
- keeps the shell resident

### `hide_window()`

- hides the window
- keeps the shell resident
- does not quit the runtime

### `toggle_window()`

- switches between `show_window()` and `hide_window()`
- does not change the shared workbench role

### `quit()`

- is the explicit exit path
- sets `quit_requested = True`
- clears tray visibility
- clears window visibility
- clears resident state
- clears `runtime_started`

### `close()`

- is cleanup for shell shutdown
- clears tray visibility
- clears window visibility
- clears resident state
- clears `runtime_started`
- does not force `quit_requested = True`

## Supported Mode Semantics

### `window` mode

- start the runtime
- keep tray presence
- open the shared workbench immediately

### `tray` mode

- start the runtime
- keep tray presence
- do not open the shared workbench until the user explicitly opens it

## Intentional Limits

This lifecycle model is still intentionally small:

- tray presence remains minimal
- Desktop does not own business navigation
- Desktop does not become a native business client
- this is a steadier shell lifecycle, not a fully hardened desktop platform
