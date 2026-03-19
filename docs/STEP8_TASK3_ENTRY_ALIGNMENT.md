# Step 8 Task 3: Entry Regression And Shell Alignment

## Purpose

Step 8 introduced the Web workbench homepage as the main entry surface.
This task keeps Telegram and Desktop aligned with that change without allowing either entry to grow into a second business center.

## Desktop Alignment

- Desktop now normalizes the default shell URL to `/workbench`
- Desktop still opens the Web workbench instead of rendering its own business page
- Desktop may show a minimal workbench status label, including the current active mode name when the shared API exposes it
- Desktop still does not own template management, shortcut management, pending logic, or formal write paths

## Telegram Alignment

- Telegram remains limited to capture, pending minimal review, and lightweight summary/status reads
- Telegram does not expose template CRUD
- Telegram does not expose a workbench command surface
- Telegram does not introduce template semantics, mode systems, or high-risk write paths

## Regression Claims

- no second business center
- no special backdoor entry
- desktop remains shell-only
- telegram remains lightweight

## Validation Note

Validation stays at adapter and shell regression level.
It does not require real Telegram networking or real OS-level desktop runtime.
