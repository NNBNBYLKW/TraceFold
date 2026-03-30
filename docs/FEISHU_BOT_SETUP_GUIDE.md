# TraceFold Feishu Bot Setup Guide

## Purpose

This guide describes the current bounded Feishu adapter in the TraceFold repo.

It exists for one thing only:

- getting Feishu Bot V1 running as a lightweight text-to-capture adapter

It is not a workflow guide, not an approval-bot guide, and not a richer Feishu product surface.

## Current Role Boundary

The current Feishu adapter is intentionally narrow.

It supports:

- text-only message input
- one message -> one capture submission
- minimal `start` / `help` guidance
- lightweight success/failure replies
- capture-first handoff into the existing TraceFold chain

It does not support:

- review actions
- direct formal writes
- AI parsing or suggestions
- cards, menus, approvals, or workflow behavior
- multimodal input

## Runtime Shape

The current runtime is a small FastAPI webhook app:

- app module: `apps/feishu/app/main.py`
- webhook route: `POST /feishu/events`

This runtime handles:

- Feishu URL verification payloads
- Feishu text-message receive events
- lightweight reply delivery through the Feishu Open API

It is a webhook adapter, not a polling daemon.

## Required Environment Variables

Use `apps/feishu/.env.example` as the starting point.

Current variables:

- `TRACEFOLD_FEISHU_APP_ID`
- `TRACEFOLD_FEISHU_APP_SECRET`
- `TRACEFOLD_FEISHU_API_BASE_URL`
- `TRACEFOLD_FEISHU_OPEN_BASE_URL`
- `TRACEFOLD_FEISHU_TIMEOUT_SECONDS`
- `TRACEFOLD_FEISHU_DEBUG`
- `TRACEFOLD_FEISHU_LOG_ENABLED`

Minimal local example:

```env
TRACEFOLD_FEISHU_APP_ID=replace-me
TRACEFOLD_FEISHU_APP_SECRET=replace-me
TRACEFOLD_FEISHU_API_BASE_URL=http://127.0.0.1:8000/api
TRACEFOLD_FEISHU_OPEN_BASE_URL=https://open.feishu.cn/open-apis
TRACEFOLD_FEISHU_TIMEOUT_SECONDS=5
TRACEFOLD_FEISHU_DEBUG=false
TRACEFOLD_FEISHU_LOG_ENABLED=true
```

## Minimal Startup Path

1. Start the TraceFold API first.
2. Fill `apps/feishu/.env`.
3. Start the Feishu webhook app:

```powershell
cd apps/feishu
uvicorn app.main:app --host 127.0.0.1 --port 8100
```

4. Point Feishu event delivery at:

```text
http://127.0.0.1:8100/feishu/events
```

Use your actual reachable development URL if Feishu cannot reach localhost directly.

## URL Verification Behavior

The current webhook app supports the standard verification-style callback shape by returning:

```json
{"challenge": "<incoming challenge>"}
```

That behavior is covered in `apps/feishu/tests/test_app.py`.

## Minimal Usage Model

Current user-facing behavior is intentionally small:

- send `start`
- send `help`
- send normal plain text

Meaning:

- `start` and `help` return short usage guidance only
- a normal text message becomes one capture submission
- deeper review or formal follow-up still belongs in TraceFold Web

## Success And Failure Feedback

Current success feedback is intentionally short:

- `Captured first. You can send the next text now.`
- `Captured first. Pending review created. You can send the next text now.`

Current failure feedback is also intentionally short:

- `Not recorded. Service unavailable. Try again later.`
- `Not recorded. Input is invalid.`
- `Not recorded. Try again.`

These messages confirm whether the text entered the system without turning Feishu into a workflow surface.

## Source Traceability

The current Feishu adapter submits:

- `source_type="feishu"`
- `source_ref="chat:<chat_id>:message:<message_id>:user:<user_id>"`

This matches the bounded adapter contract and stays parallel to the Telegram source-traceability pattern.

## Current Acceptance Reality

The current repo has focused tests for:

- webhook entry
- URL verification
- normal text submission
- minimal help/usage behavior
- success/failure feedback
- contract inheritance and no-review/workflow drift

Those tests make Feishu Bot V1 more than a purely theoretical direction note, but they do not claim broader enterprise-bot hardening or richer Feishu platform support.
