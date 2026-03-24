# API Seeded Integration Smoke

## Purpose

This document defines the fresh demo DB integration smoke baseline for TraceFold.

It is for:

- recreating a clean local integration environment
- validating the formal migration + seed path
- confirming API startup and core API reads
- confirming Web-to-API local wiring

It is not:

- a production runbook
- a replacement for normal day-to-day startup docs
- a new backend capability surface

## Standard Fresh Demo DB Flow

Use a new SQLite file. Do not point this flow at an old local DB.

Keep the DB target identical across migration, seed, and API startup. If you use a relative SQLite URL, reuse the exact same string each time.

Recommended sequence:

1. Choose a fresh SQLite file
2. Run migrations to `head`
3. Run demo seed
4. Start API
5. Check `/api/healthz`
6. Check `/api/system/status`
7. Start Web
8. Open the main Web routes and verify seeded state

## Standard Commands

Run from the repo root unless noted otherwise.

### 1. Migrate Fresh Demo DB

```powershell
python apps/api/scripts/run_migrations.py upgrade head --db-url sqlite:///./data/tracefold_demo_smoke.db
```

### 2. Seed Fresh Demo DB

```powershell
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///./data/tracefold_demo_smoke.db --with-derivations
```

If you intentionally want to recreate an existing prior demo DB:

```powershell
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///./data/tracefold_demo_smoke.db --force --with-derivations
```

### 3. Point API At The Fresh Demo DB

In `apps/api/.env`:

```env
TRACEFOLD_API_DB_URL=sqlite:///../../data/tracefold_demo_smoke.db
TRACEFOLD_API_HOST=127.0.0.1
TRACEFOLD_API_PORT=8000
```

Then start API:

```powershell
cd apps/api
uvicorn app.main:app --host 127.0.0.1 --port 8000
```

### 4. Point Web At The Shared API

In `apps/web/.env`:

```env
VITE_API_BASE_URL=http://127.0.0.1:8000
```

Then start Web:

```powershell
cd apps/web
npm run dev
```

Expected local URLs:

- API root: `http://127.0.0.1:8000`
- API health: `http://127.0.0.1:8000/api/healthz`
- API status: `http://127.0.0.1:8000/api/system/status`
- Web: `http://127.0.0.1:3000`
- Workbench home: `http://127.0.0.1:3000/workbench`

## Automated Smoke

### One-shot fresh demo smoke

This script recreates the fresh demo DB path and then validates core API reads:

```powershell
python apps/api/scripts/smoke_seeded_demo.py --db-url sqlite:///./data/tracefold_demo_smoke.db --force
```

It covers:

- migration to `head`
- demo seed
- API health
- runtime status
- expense list
- knowledge list/detail + derivation read
- health list
- alert existence
- task existence
- dashboard summary
- workbench home summary

### Recommended automated test commands

API seeded integration smoke:

```powershell
python -m pytest -q apps/api/tests/system/test_seeded_integration_smoke.py
```

API seed baseline smoke:

```powershell
python -m pytest -q apps/api/tests/system/test_seed_demo_data.py
```

Web contract smoke for local API wiring and workbench entry:

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_workbench_home_contract.py
```

## Manual Smoke Checklist

### API checks

Open or request:

- `GET http://127.0.0.1:8000/api/healthz`
- `GET http://127.0.0.1:8000/api/system/status`
- `GET http://127.0.0.1:8000/api/expense?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/knowledge?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/health?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/alerts?domain=health&status=open`
- `GET http://127.0.0.1:8000/api/tasks?limit=20`
- `GET http://127.0.0.1:8000/api/dashboard`
- `GET http://127.0.0.1:8000/api/workbench/home`

Expected outcomes:

- `healthz` returns success
- `system/status` returns success with DB reachable and schema at head
- expenses list is not empty
- knowledge list is not empty
- health list is not empty
- at least one health alert exists
- at least one task run exists
- dashboard and workbench home are no longer empty

### Knowledge detail + derivation check

Pick any seeded knowledge id from the list response, then verify:

- `GET /api/knowledge/{id}`
- `GET /api/ai-derivations/knowledge/{id}`

Expected outcome:

- the formal knowledge record is readable
- `knowledge_summary` is readable through the formal derivation path

### Web checks

Open these routes in the browser:

- `http://127.0.0.1:3000/workbench`
- `http://127.0.0.1:3000/expense`
- `http://127.0.0.1:3000/knowledge`
- `http://127.0.0.1:3000/health`

Expected outcomes:

- the app loads without the shared "TraceFold API is unavailable" startup failure
- workbench home shows non-empty summary state
- expense, knowledge, and health routes are no longer empty-only shells

## Common Failure Checks

### API does not start

Check:

- `apps/api/.env`
- `TRACEFOLD_API_DB_URL`
- whether the DB file path is writable

Also verify the DB was migrated:

```powershell
python apps/api/scripts/run_migrations.py current --db-url sqlite:///./data/tracefold_demo_smoke.db
```

### `/api/healthz` is unreachable

Check:

- the API process is actually running
- host/port are `127.0.0.1:8000`
- another process is not holding the port

### `/api/system/status` shows wrong schema state

Check:

- the DB file in `TRACEFOLD_API_DB_URL` is the same DB you migrated
- migration revision is at `head`
- you did not accidentally point API at an old local DB

### Seed was refused

The seed baseline intentionally refuses:

- non-migrated DBs
- non-demo formal data
- repeated demo seeding without `--force`

If this happens, either:

- point at a brand-new SQLite file
- or use `--force` only for a prior demo DB

### Web shows shared API unavailable

Check:

- `http://127.0.0.1:8000/api/healthz`
- `apps/web/.env`
- `VITE_API_BASE_URL=http://127.0.0.1:8000`

If the API is healthy but the page still fails, restart the Web dev server after correcting `.env`.

## Boundary Reminder

- This is a fresh demo DB validation baseline
- It does not replace the normal startup baseline
- It does not create new backend capability
- It does not change formal fact, alert, derivation, or task semantics
