# Post-Phase-1 A3 Task 4 Scope

## Goal

Task 4 closes A3 by reducing validation friction around the entry layer. The goal is not to build a bigger test platform. The goal is to make the existing entry-layer baseline easier to run, easier to explain, and less likely to drift apart across docs, tests, and runtime behavior.

This task only covers:

- Desktop shell runtime validation
- API / Web / Desktop startup and config baseline validation
- failure signal and recovery hint validation
- the role split between daily entry validation, broader smoke, build validation, and support scripts

## Out Of Scope

Task 4 does not:

- add business features
- change the mainline
- change formal fact writes
- change Pending semantics
- change Telegram
- expand Desktop beyond shell-only
- introduce browser E2E
- turn the acceptance runner into a large test framework

## Boundary Reminder

The entry layer remains bounded:

- Web is the main business interface
- Desktop remains a shell-level entry
- the service layer remains the only business logic center

This task is successful only if validation becomes easier to use without creating a new logic layer or a new platform inside the validation stack.
