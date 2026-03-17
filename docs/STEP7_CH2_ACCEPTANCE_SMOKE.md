# Step 7 Chapter 2 Acceptance Smoke

## Chapter 2 Purpose

Chapter 2 accepts Telegram as a lightweight entry adapter on top of the
Chapter 1 API baseline. The goal is not to create a second business client.
The goal is to verify that Telegram can submit input, view minimal state, and
trigger minimal actions through the same system path.

## What Telegram Does In Step 7

- Accepts private text capture input.
- Supports a thin `/capture <text>` alias.
- Reads minimal pending state with `/pending` and `/pending <id>`.
- Triggers minimal pending actions with `/confirm`, `/discard`, and `/fix`.
- Reads lightweight summaries with `/dashboard`, `/alerts`, and `/status`.
- Uses short formatter output and unified error feedback.

## What Telegram Intentionally Does Not Do

- Does not connect to the database directly.
- Does not call internal domain services.
- Does not expose `force_insert`.
- Does not implement multi-turn fix state.
- Does not duplicate review rules.
- Does not replace the Web workbench.
- Does not become a notification platform.
- Does not provide Knowledge or Health detail reading.

## Minimal Smoke Scenarios

1. Private plain text message -> `POST /api/capture` -> short capture result text.
2. `/capture <text>` -> `POST /api/capture` -> same adapter path as plain text capture.
3. `/pending` -> `GET /api/pending` -> short open pending list.
4. `/pending <id>` -> `GET /api/pending/{id}` -> short single-item action context.
5. `/confirm <id>` -> `POST /api/pending/{id}/confirm` -> short action result.
6. `/discard <id>` -> `POST /api/pending/{id}/discard` -> short action result.
7. `/fix <id> <text>` -> `POST /api/pending/{id}/fix` -> single-turn minimal correction result.
8. `/dashboard` -> `GET /api/dashboard` -> short summary view.
9. `/alerts` -> `GET /api/alerts` -> short open-alert summary.
10. `/status` -> `GET /api/healthz` -> short service availability result.
11. API unavailable or stable API error -> short unified Telegram error text without internal details.

## Why Telegram Remains An Adapter

- Telegram only calls the unified HTTP API.
- Telegram does not bypass service or API semantics.
- Telegram does not define parse, review, confirm, or formal-write rules.
- Telegram does not own business state transitions.
- Telegram only performs message normalization, API invocation, and short text rendering.

## Why This Is Enough For Chapter 3

- Telegram Step 7 scope is already covered by one adapter path:
  capture, pending minimal read, pending minimal actions, dashboard, alerts,
  and status.
- The adapter boundary is already frozen:
  HTTP only, no DB access, no internal service coupling, no force-insert path.
- Existing tests already verify the key command-to-API flows and basic failure paths.
- Chapter 3 can therefore focus on Desktop shell work without reopening Telegram
  architecture or Chapter 1 API semantics.
