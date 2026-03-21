# Post-Phase-1 A3 Task 2 Startup Baseline

## Daily Recommended Startup Order

1. Start the API
2. Start the Web app
3. Start the Desktop shell only after API and Web are reachable

This order is recommended because:

- Web depends on the shared API for real data and workbench home data
- Desktop depends on the shared Web Workbench URL and probes the shared API for
  service status
- Desktop is easier to reason about when the shared API and shared Web entry
  are already available

## Formal Recommended Entry Commands

### API

Recommended daily command:

`cd apps/api && uvicorn app.main:app --host 127.0.0.1 --port 8000`

Minimum configuration source:

- `apps/api/.env`
- fallback defaults are defined in `apps/api/app/core/config.py`, but daily use
  should still treat `apps/api/.env` as the explicit local baseline

Minimum required conditions:

- SQLite DB path resolves correctly
- API responds on `http://127.0.0.1:8000/api/healthz`

### Web

Recommended daily command:

`cd apps/web && npm run dev`

Minimum configuration source:

- `apps/web/.env`
- required key:
  - `VITE_API_BASE_URL`

Minimum required conditions:

- the Web dev server is reachable on `http://127.0.0.1:3000`
- `VITE_API_BASE_URL` points at the shared API root, normally
  `http://127.0.0.1:8000`

### Desktop

Recommended daily command:

`python -m apps.desktop`

Lower-level compatibility path:

`python -m apps.desktop.app.main`

Minimum configuration source:

- `apps/desktop/.env` is now the preferred settings file
- `.env` in the current working directory may still override explicitly, but it
  is not the recommended baseline

Minimum required conditions:

- `TRACEFOLD_DESKTOP_WEB_WORKBENCH_URL` points at the shared Web Workbench,
  normally `http://127.0.0.1:3000/workbench`
- `TRACEFOLD_DESKTOP_API_BASE_URL` points at the shared API root with `/api`,
  normally `http://127.0.0.1:8000/api`

## Status And Dependency Relationships

- `GET /api/healthz` answers whether the shared API is up
- `GET /api/ping` is a minimal reachability check
- Web request failures usually mean the API is unavailable, the API base URL is
  wrong, or the API response is invalid
- Desktop `service unavailable` means the API base URL cannot be reached from
  Desktop status probing
- Desktop Workbench opening uses the shared Web URL and is not the same thing
  as API health

## Validation-Only Paths That Are Not Daily Startup Paths

These are useful, but they are not the daily way to run the system:

- `python apps/api/scripts/step9_ch1_acceptance_smoke.py`
- repo-style ensure scripts such as `apps/api/scripts/migrate_step8_workbench.py`
- build-only validation like `cd apps/web && npm run build`

Their role is validation or schema ensure support, not day-to-day application
startup.
