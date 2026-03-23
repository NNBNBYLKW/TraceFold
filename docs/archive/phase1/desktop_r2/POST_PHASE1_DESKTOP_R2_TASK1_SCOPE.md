# Post-Phase-1 Desktop R2 Task 1 Scope

## Goal

This task hardens the Desktop shell runtime lifecycle. It does not expand Desktop into a richer product surface.

The target is a more credible shell lifecycle:

- clearer runtime state semantics
- tighter action-to-state updates
- more stable window / tray / hide / quit behavior
- less risk of a fake running state

## In Scope

- Desktop shell lifecycle state
- Desktop shell runtime transitions
- Desktop lifecycle tests
- Desktop lifecycle documentation

## Out Of Scope

This task does not:

- add native business pages
- add Desktop-owned business logic
- add Desktop-owned template / shortcut / recent management
- add Desktop write paths
- change the shared workbench role
- change the mainline
- add a WebView-based business client

## Boundary Reminder

Desktop remains a shell-level entry:

- it opens the shared Web workbench
- it exposes minimal shell state
- it does not become a second business center

Lifecycle hardening is only successful if the runtime becomes steadier without adding new business meaning to the Desktop layer.
