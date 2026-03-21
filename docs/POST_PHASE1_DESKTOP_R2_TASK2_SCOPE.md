# Post-Phase-1 Desktop R2 Task 2 Scope

## Goal

This task hardens Desktop tray integration. It does not expand Desktop into a richer desktop product.

The target is a steadier tray-backed shell model:

- clearer tray role
- clearer tray action semantics
- tighter synchronization between tray presence and shell lifecycle state
- less ambiguity across `open`, `toggle`, `hide`, and `quit`

## In Scope

- tray action model
- tray-to-lifecycle synchronization
- tray-related Desktop tests
- tray integration documentation

## Out Of Scope

This task does not:

- add business menu trees
- add template / shortcut / recent / pending tray actions
- add Desktop-owned business logic
- change the shared workbench role
- change the mainline
- turn the tray into a management console

## Boundary Reminder

The tray remains part of shell integration only:

- presence indicator
- minimal shell action entry
- no business navigation tree
- no second business center
