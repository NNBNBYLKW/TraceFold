# Step 8 Task 1: API Workbench

## Purpose

Step 8 Task 1 establishes the backend workbench domain for the Web workbench home surface.
It does not redefine formal business semantics. It adds entry configuration, recent recovery context,
and a thin home aggregation contract on top of the existing service layer.

## What Was Added

- Independent `workbench` domain under `apps/api/app/domains/workbench`
- Persistent tables for:
  - `workbench_templates`
  - `workbench_shortcuts`
  - `workbench_recent_contexts`
  - `workbench_preferences`
- `GET /api/workbench/home`
- Template CRUD and apply APIs
- Shortcut CRUD APIs
- Recent and preferences APIs
- Thin recent recorder hooks on existing detail and pending action routes
- `default_query_json` whitelist validation in the workbench service layer

## Boundary Notes

- `workbench` is not a replacement for `dashboard`
- `dashboard_summary` in `/api/workbench/home` reuses existing dashboard summary semantics
- `apply template` only updates workbench preference context
- `recent` is a recovery surface, not a formal fact table and not an audit log
- recent recording is best-effort and must not roll back the main business flow

## Recent Recorder Scope

Current recording points:

- `viewed`
  - `GET /api/expense/{id}`
  - `GET /api/knowledge/{id}`
  - `GET /api/health/{id}`
  - `GET /api/pending/{id}`
- `acted`
  - `POST /api/pending/{id}/confirm`
  - `POST /api/pending/{id}/discard`
  - `POST /api/pending/{id}/fix`

Current rules:

- dedupe by `object_type + object_id + action_type`
- keep only the most recent occurrence
- cap total records at `20`
- default API/home return limit at `5`

## Migration Note

This repository currently uses `init_db()` / `Base.metadata.create_all()` instead of a separate migration framework.
`apps/api/scripts/migrate_step8_workbench.py` ensures the Step 8 workbench schema is registered and created.
