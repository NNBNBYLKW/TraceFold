# Post-Phase-1 A3 Task 2 Scope

## Purpose

This task defines the minimum daily startup, config, and recovery baseline for
the API, Web, and Desktop entries.

It does not add business functionality. It only makes the existing entry
surfaces easier to start, understand, and recover without relying on project
memory or chat history.

## In Scope

- formal daily startup order for API, Web, and Desktop
- required minimal configuration for each entry
- relationship between API health, Web API access, Desktop service status, and
  Desktop Workbench URL
- short recovery paths for common startup failures
- explicit separation between daily startup paths and validation-only runners

## Out Of Scope

- Telegram
- deployment platform changes
- migration framework changes
- business logic changes
- Desktop product expansion
- new entry surfaces

## Boundary Reminder

- API remains the shared service center
- Web remains the main business interface
- Desktop remains shell-only
- acceptance runners and ensure scripts remain support tools, not daily entry
  points
