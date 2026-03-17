# Step 7 Chapter 2 Telegram Adapter Notes

## Purpose

This note defines the minimal Telegram adapter skeleton added in Chapter 2.
Its purpose is to freeze the adapter boundary before wiring capture, pending,
dashboard, or alerts behavior.

## Directory Structure

- `apps/telegram/app/core`
- `apps/telegram/app/clients`
- `apps/telegram/app/bot`
- `apps/telegram/app/formatting.py`
- `apps/telegram/app/main.py`
- `apps/telegram/tests`

## Boundary Notes

- Telegram remains an adapter, not a business domain.
- The adapter talks to TraceFold only through the Chapter 1 HTTP API.
- The adapter does not import repositories or domain business services.
- The adapter currently handles only startup probing, command dispatch
  skeletons, and minimal message rendering.

## Intentionally Not Wired Yet

- Capture submission
- Pending review actions
- Dashboard reads
- Alerts reads
- Push notifications
- Multi-turn conversation state

## Telegram Text Capture Flow

- Private plain text messages may be mapped to `POST /api/capture`.
- `/capture <text>` may use the same adapter path as an explicit command form.
- The adapter sends:
  - `raw_text` from the Telegram message text
  - `source_type = telegram`
  - `source_ref` from minimal chat/message/user identifiers
- The adapter does not parse or route business meaning locally.
- Success feedback stays short:
  - pending
  - committed domain
  - minimal ids only when useful

## Telegram Pending Minimal Flow

- `/pending` reuses `GET /api/pending` and renders only a short list summary.
- `/pending <id>` reuses `GET /api/pending/{id}` and renders only minimal action context.
- `/confirm <id>` and `/discard <id>` reuse the existing pending action APIs directly.
- `/fix <id> <text>` remains single-turn and single-item.
- Telegram does not infer corrected payload structure locally.
- The fix result is still defined by the unified pending review service.
- No force-insert path, batch path, or multi-turn review state is added in Telegram.

## Telegram Dashboard / Alerts / Status Flow

- `/dashboard` reuses `GET /api/dashboard` and renders only a short summary view.
- `/alerts` reuses `GET /api/alerts` and renders only open alert highlights.
- `/status` reuses the existing system status endpoint and renders only minimal availability text.
- Telegram does not add a new summary API or a second aggregation layer.
- Telegram remains a pull-based light viewer here, not a push notification engine.
- These commands do not replace the Web workbench or formal detail pages.

## Formatting And Error Handling Rules

- Telegram output stays short so the adapter remains a quick-entry and quick-check surface.
- Success and empty-state messages use one small formatting layer instead of command-local text templates.
- Error feedback reuses Chapter 1 error semantics and maps them to short adapter text without exposing internal details.
- Minimal observability records:
  - command or message type
  - chat id
  - message id
  - endpoint category
  - success or failure outcome
- This keeps Telegram debuggable without turning it into a business or observability center.

## Final Conclusion

Chapter 2 is sufficient as a Telegram Step 7 adapter baseline: the adapter now
reuses the Chapter 1 HTTP API for capture, pending, dashboard, alerts, and
status, while still remaining outside the business core.
