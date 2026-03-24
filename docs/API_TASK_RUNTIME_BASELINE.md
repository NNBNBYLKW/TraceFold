# API Task Runtime Baseline

## 1. Purpose

This document records the minimal formal background task runtime baseline for `apps/api`.

Its role is intentionally limited:

- provide a shared async execution entry
- persist task state
- make task failure visible
- give later rule / derivation / refresh work one common runtime baseline

It is not a workflow engine, cron platform, queue cluster, or user-facing task center.

## 2. Runtime Positioning

TraceFold task runtime is a shared execution layer, not a second business center.

The boundary is:

- service layer owns business logic
- task runtime owns async execution, state persistence, and failure visibility
- routers stay thin and only trigger or read task state

Task runtime must not invent new business truth on its own.

## 3. Sync And Async Boundary

Suitable for task runtime:

- AI derivation recompute
- dashboard summary refresh
- health rules rerun
- slower export-like work

Not required to enter task runtime:

- ordinary read requests
- short synchronous validation
- primary fact write paths that should complete inside the request transaction

This baseline only connects one formal example task:

- `dashboard_summary_refresh`

## 4. Task Model

Formal task persistence now uses:

- `task_runs`

Current fields:

- `id`
- `task_type`
- `status`
- `trigger_source`
- `payload_json`
- `result_json`
- `error_message`
- `attempt_count`
- `requested_at`
- `started_at`
- `finished_at`
- `idempotency_key`
- `created_at`
- `updated_at`

Field intent stays narrow:

- `payload_json` stores only minimal task input
- `result_json` stores only minimal task result summary
- `idempotency_key` is currently a reserved field position, not a full idempotent deduplication system
- raw sensitive capture content should not be copied into task rows by default

Current idempotency boundary:

- the schema keeps `idempotency_key` so later work has a stable field to extend from
- this baseline does not implement a general duplicate-request protection platform
- later idempotency work, if needed, should stay narrow and task-specific rather than turning task runtime into a generic idempotency subsystem

## 5. Task Status Enum

Shared task status values are:

- `pending`
- `running`
- `succeeded`
- `failed`
- `cancelled`

This baseline deliberately does not add extra intermediate states.

## 6. Trigger Source Enum

Current trigger source values are:

- `api`
- `system`
- `manual`

The enum is shared runtime metadata, not a product-facing menu.

## 7. API Surface

Formal task API entry points are:

- `POST /api/tasks/{task_type}`
- `GET /api/tasks`
- `GET /api/tasks/{id}`
- `POST /api/tasks/{id}/cancel`

Current list query filters are intentionally minimal:

- `status`
- `task_type`

Current supported task types:

- `dashboard_summary_refresh`
- `knowledge_summary_recompute`

The trigger path is intentionally small. The main goal is to establish a real runtime baseline, not to build a large task submission product.

## 8. Example Task

Current example task:

- `dashboard_summary_refresh`

Why this task was chosen:

- it exercises real async execution
- it stays inside current Phase 1 boundaries
- it reuses existing dashboard service logic
- it avoids dragging in a full AI runtime redesign

The task runtime executor calls the dashboard service layer and stores a compact summary result, rather than a large workflow artifact.

Later shared runtimes may attach to the same task baseline without changing its role. The current derivation runtime baseline does this with:

- `knowledge_summary_recompute`

This does not turn task runtime into a general AI platform. It remains a narrow execution layer for service-owned work.

## 9. Runtime Status Integration

`GET /api/system/status` now reflects task runtime state through `task_runtime_status`.

Current values:

- `ready`
- `degraded`

Current degraded signal:

- failed task runs present -> `task_runs_failed_present`

This is intentionally minimal. It does not yet attempt queue depth analytics or worker fleet monitoring.

## 10. Logging Baseline

Key task runtime events are logged with the shared structured logging helper.

Current coverage includes:

- `task_requested`
- `task_started`
- `task_succeeded`
- `task_failed`
- `task_cancelled`
- `task_query_failed`
- `task_execution_skipped`
- `task_wrapper_failed`

Preferred stable fields:

- `event`
- `domain`
- `action`
- `task_type`
- `task_id`
- `result`
- `error_code`
- `trigger_source`

## 11. Migration Baseline

Task runtime schema is formally migrated through Alembic.

Current head revision:

- `apps/api/migrations/versions/20260323_0005_ai_derivation_runtime_baseline.py`

The task runtime baseline still comes from `20260323_0002_task_runtime_baseline.py`; later revisions extend shared schema baselines without changing task runtime ownership.

## 12. Commands And Tests

Run from `apps/api/`.

Task runtime focused tests:

```powershell
python -m pytest -q tests\domains\system_tasks\test_task_service.py tests\domains\system_tasks\test_task_api.py
```

Recommended validation with runtime status and migration smoke:

```powershell
python -m pytest -q tests\domains\system_tasks\test_task_service.py tests\domains\system_tasks\test_task_api.py tests\core\test_runtime_status.py tests\system\test_runtime_status_api.py tests\db\test_migrations.py
```
