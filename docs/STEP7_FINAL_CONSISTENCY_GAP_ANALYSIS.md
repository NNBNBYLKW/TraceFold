# Step 7 Final Consistency Gap Analysis

## Completed Entry Surfaces

- `Web / API`
  - unified capture, pending, dashboard, alerts, ping, and healthz HTTP surface
- `Telegram`
  - text capture
  - minimal pending read and actions
  - lightweight dashboard / alerts / status read
- `Desktop`
  - workbench opening / hosting shell path
  - tray / resident / open-hide / quit shell lifecycle
  - minimal service status visibility
  - minimal notification handling for `service unavailable`

## Required Final Consistency Claims

### same record semantics across entries

- A capture submitted from any entry must land on the same capture -> pending or capture -> formal path.
- Desktop must open the same Web workbench semantics instead of introducing a desktop-only record path.

### same pending semantics across entries

- Pending list, detail, confirm, discard, and fix must keep the same service-defined status meaning across API and Telegram.
- Telegram fix must remain single-turn text correction only, with result semantics still owned by the pending review service.

### same alert/status meaning across entries

- Dashboard, alerts, and status must keep one meaning across API, Telegram, and Desktop shell visibility.
- Desktop `service unavailable` notification must be a shell-level rendering of the same status meaning, not a new status system.

### no special backdoor entry

- No Telegram `force_insert`.
- No desktop direct DB write or shell-owned write path.
- No entry-specific state transition logic outside the unified service/API path.

## Existing Evidence Already Available

- `Chapter 1`
  - capture HTTP smoke verifies capture -> pending and capture -> formal paths
  - pending HTTP tests verify confirm / discard / fix service path and stable errors
  - dashboard / alerts / ping / healthz tests verify shared read surface and unified response semantics
- `Chapter 2`
  - Telegram tests verify plain text capture -> API
  - Telegram tests verify `/pending`, `/confirm`, `/discard`, `/fix`
  - Telegram tests verify `/dashboard`, `/alerts`, `/status`
  - Telegram acceptance doc already states adapter-only boundary and no special write path
- `Chapter 3`
  - Desktop tests verify shell assembly, workbench opening, tray lifecycle, service status visibility, and `service unavailable` notification handling
  - Desktop acceptance doc already states shell-only boundary and no second frontend path
- `Step 7 frozen docs`
  - scope, contracts, review checklist, and acceptance baseline already freeze the no-backdoor and one-service-core requirements

## Remaining Minimal Validation Gaps

- `same record semantics across entries`
  - Evidence is strong but still mostly split across Chapter 1 API tests and Chapter 2 Telegram tests.
  - Final chapter should add one short cross-entry smoke that ties Telegram capture input back to the same Chapter 1 capture result semantics.
- `same pending semantics across entries`
  - Evidence is strong but still split across pending API tests and Telegram pending command tests.
  - Final chapter should add one short cross-entry smoke that validates Telegram pending action output against the same service-defined pending outcome shape.
- `same alert/status meaning across entries`
  - Service status consistency is mostly covered by Chapter 1 system tests, Telegram `/status`, and Desktop status visibility tests.
  - High-priority health alert consistency is only partially evidenced through API and Telegram summary use; Desktop intentionally does not yet consume this path.
  - Final chapter should validate only the supported Step 7 surface:
    API alerts meaning -> Telegram alerts rendering, and API healthz meaning -> Desktop status / notification rendering.
- `no special backdoor entry`
  - Most of this is already supported by docs, contracts, and adapter/shell structure.
  - Final chapter should add a short checklist-style validation, not a new deep test suite.

## Proposed Final Validation Order

1. Reuse Chapter 1 acceptance smoke as the backend truth baseline.
2. Add one Telegram cross-entry smoke for capture and pending semantics against the same API path.
3. Add one Desktop cross-entry smoke for service status meaning and shell notification meaning.
4. Close with one final checklist-style document review:
   no `force_insert`, no desktop write path, no duplicated business center, no entry-specific semantics.
