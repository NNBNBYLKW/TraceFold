# Development Entrypoints

## Purpose

This document is the default local development and fresh demo DB entrypoint for the current TraceFold baseline.

Use this document when you need:

- a fresh SQLite database
- schema at the current migration head
- repeatable demo seed data
- API and Web startup commands
- a short path to seeded local smoke checks

For deeper detail, follow the linked baseline docs after this file.

## Default Reading Order For Setup

1. `docs/DEVELOPMENT_ENTRYPOINTS.md`
2. `docs/API_MIGRATION_BASELINE.md`
3. `docs/API_DEMO_SEED_BASELINE.md`
4. `docs/API_SEEDED_INTEGRATION_SMOKE.md`
5. `docs/WEB_CONSUMPTION_BASELINE.md`

## Current Web Entry (Post-Phase-1 / Phase 2 V1 Closure-Ready State)

For the current closed Web state, use this reading order:

1. `docs/WEB_CONSUMPTION_BASELINE.md`
2. `docs/WEB_SHARED_STATE_POLISH_BASELINE.md`
3. `docs/WEB_KNOWLEDGE_AI_PRESENTATION_BASELINE.md`
4. `docs/WEB_HEALTH_ALERTS_PRESENTATION_BASELINE.md`
5. `docs/WEB_POST_PHASE1_OPTIMIZATION_PLAN.md`
6. `docs/WEB_INFORMATION_HIERARCHY_CONTRACT.md`
7. `docs/WEB_PP1_CROSS_PAGE_CONSISTENCY_AND_SMOKE_BASELINE.md`
8. `docs/PHASE2_V1_CLOSURE_BASELINE.md`

The first four define the current formal Web baseline.
The optimization and hierarchy docs define the restrained Post-Phase-1 polish direction.
The closure baseline records the currently closed state for the first Web optimization round.
The Phase 2 V1 closure baseline records the currently implemented closure-ready expansion across Pending, Capture, Expense, Workbench Round 2, Templates restraint, and local operability.

This closed round should not be implicitly reopened.
Any further Web work should be opened as a new scoped task.

## Standard Fresh Demo DB Flow

Recommended order:

1. Choose a fresh SQLite file
2. Run migrations to `head`
3. Run demo seed
4. Point API at that DB
5. Start API
6. Check `/api/healthz`
7. Check `/api/system/status`
8. Point Web at the API
9. Start Web
10. Run the minimum smoke reads

Do not use an unstamped or legacy local DB for this flow.

## Standard Commands

Run from the repo root unless noted otherwise.

### 1. Migrate Fresh Demo DB

```powershell
python apps/api/scripts/run_migrations.py upgrade head --db-url sqlite:///./data/tracefold_demo.db
```

### 2. Seed Fresh Demo DB

```powershell
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///./data/tracefold_demo.db --with-derivations
```

If you intentionally want to recreate an existing prior demo DB:

```powershell
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///./data/tracefold_demo.db --force --with-derivations
```

### 3. Point API At The Demo DB

In `apps/api/.env`:

```env
TRACEFOLD_API_DB_URL=sqlite:///../../data/tracefold_demo.db
TRACEFOLD_API_HOST=127.0.0.1
TRACEFOLD_API_PORT=8000
```

Start API:

```powershell
cd apps/api
uvicorn app.main:app --host 127.0.0.1 --port 8000
```

### 4. Point Web At The Shared API

In `apps/web/.env`:

```env
VITE_API_BASE_URL=http://127.0.0.1:8000
```

Start Web:

```powershell
cd apps/web
npm run dev
```

## Minimum Verification Path

Check these API routes first:

- `GET http://127.0.0.1:8000/api/healthz`
- `GET http://127.0.0.1:8000/api/system/status`
- `GET http://127.0.0.1:8000/api/system/local-operability`
- `GET http://127.0.0.1:8000/api/capture?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/pending?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/expense?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/knowledge?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/health?page=1&page_size=20`
- `GET http://127.0.0.1:8000/api/alerts?domain=health&status=open`
- `GET http://127.0.0.1:8000/api/tasks?limit=20`
- `GET http://127.0.0.1:8000/api/dashboard`
- `GET http://127.0.0.1:8000/api/workbench/home`

Then check these Web routes:

- `http://127.0.0.1:3000/workbench`
- `http://127.0.0.1:3000/capture`
- `http://127.0.0.1:3000/pending`
- `http://127.0.0.1:3000/expense`
- `http://127.0.0.1:3000/knowledge`
- `http://127.0.0.1:3000/health`

Expected baseline:

- API starts cleanly on the seeded DB
- runtime status is readable
- local continuity status is readable
- capture and pending list routes are readable
- expenses, knowledge, and health are not empty
- at least one health alert exists
- at least one task run exists
- workbench and dashboard are no longer empty shells

## One-Shot Smoke

For the full fresh-demo recreation path:

```powershell
python apps/api/scripts/smoke_seeded_demo.py --db-url sqlite:///./data/tracefold_demo.db --force
```

This is a development smoke helper, not a production runbook.

## Common First Checks

### API unavailable

Check:

- API process is actually running
- `apps/api/.env`
- `TRACEFOLD_API_DB_URL`
- the DB file path is writable

### `/api/healthz` unreachable

Check:

- host and port are `127.0.0.1:8000`
- another process is not holding the port

### Wrong schema state

Check:

```powershell
python apps/api/scripts/run_migrations.py current --db-url sqlite:///./data/tracefold_demo.db
```

Also verify the API is pointing at the same DB you migrated.

### Seed refused

The seed baseline intentionally refuses:

- non-migrated DBs
- non-demo formal data
- repeated demo seeding without `--force`

Start with a brand-new SQLite file unless you explicitly mean to recreate a prior demo DB.

### Web shows API unavailable

Check:

- `http://127.0.0.1:8000/api/healthz`
- `apps/web/.env`
- `VITE_API_BASE_URL=http://127.0.0.1:8000`

Restart the Web dev server after fixing `.env`.

## Related Docs

- `docs/API_MIGRATION_BASELINE.md`
- `docs/API_DEMO_SEED_BASELINE.md`
- `docs/API_SEEDED_INTEGRATION_SMOKE.md`
- `docs/WEB_CONSUMPTION_BASELINE.md`
- `docs/PHASE2_V1_CLOSURE_BASELINE.md`
- `docs/POST_PHASE1_A3_TASK2_STARTUP_BASELINE.md`
- `docs/POST_PHASE1_A3_TASK4_VALIDATION_BASELINE.md`
