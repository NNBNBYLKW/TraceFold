# Local Testing

This document records the minimum local testing commands and current testing status for the TraceFold Phase 1 API baseline.

## Preconditions

Run all commands from:

```text
apps/api/
```

Install dependencies first:

```powershell
python -m pip install -r requirements.txt
```

---

## Start API

Run the API locally:

```powershell
uvicorn app.main:app --reload --host 127.0.0.1 --port 8000
```

---

## Initialize Database

Initialize the current local SQLite schema:

```powershell
python -c "from app.db.init_db import init_db; init_db()"
```

Use this for local initialization of currently implemented models.
It is not a full migration system.

---

## Run All Tests

```powershell
python -m pytest
```

## Run System Smoke Tests Only

```powershell
python -m pytest tests/test_system.py
```

---

## Current Test Coverage

### System Smoke Tests

Current covered endpoints:

- `GET /api/ping`
- `GET /api/healthz`

Expected responses:

- `/api/ping` -> `{"message": "pong"}`
- `/api/healthz` -> `{"status": "ok"}`

### Capture Smoke Test

Current file:

- `tests/domains/capture/test_capture_smoke.py`

Current status:

- placeholder only
- currently skipped
- should be enabled after capture endpoints are fully implemented

Planned minimum capture smoke chain:

1. `POST /api/capture`
2. `GET /api/capture`
3. `GET /api/capture/{id}`

---

## Current Expected Result

At the current Step 2 state, running:

```powershell
python -m pytest
```

should produce a result equivalent to:

```text
2 passed, 1 skipped
```

---

## Notes

- This is a **P0 minimal quality guardrail**, not a full CI/CD setup.
- The current goal is to verify that the API can start, core system endpoints can be checked, and the first business-domain test position already exists.
- Do not expand this into GitHub Actions, pre-commit, a complex test matrix, or a separate test platform during the current Phase 1 step.
- If database schema changes are made, also update the model documentation and re-run local initialization plus the relevant tests.
