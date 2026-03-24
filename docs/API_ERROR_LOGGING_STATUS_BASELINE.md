# API Error, Logging, And Runtime Status Baseline

## 1. Purpose

This document records the post-Phase-1 baseline for three shared API concerns:

- uniform API error payloads
- shared service/runtime logging rules
- minimal runtime status visibility

It is intentionally narrow. It does not introduce a new monitoring platform, a task runtime center, or a second business control surface.

## 2. Standard Error Response

TraceFold API continues to use the shared response envelope from `app.core.responses`.

Successful responses keep the existing shape:

```json
{
  "success": true,
  "message": "OK",
  "data": {},
  "meta": null,
  "error": null
}
```

Failed responses use the same envelope with a structured `error` object:

```json
{
  "success": false,
  "message": "Validation failed.",
  "data": null,
  "meta": null,
  "error": {
    "code": "VALIDATION_ERROR",
    "details": [],
    "request_id": "optional",
    "retryable": false
  }
}
```

Current rules:

- `code` is required and machine-readable
- `message` is stable caller-facing text
- `details` is optional structured context
- `request_id` is reserved for future request correlation and is not populated by default
- `retryable` is only included when the retry contract matters
- raw Python tracebacks are not returned as formal API payloads

## 3. Error Code Baseline

Shared codes live in `apps/api/app/core/error_codes.py`.

Current baseline categories include:

- `VALIDATION_ERROR`
- `NOT_FOUND`
- `CONFLICT`
- `ILLEGAL_STATE`
- `DEPENDENCY_UNAVAILABLE`
- `DATABASE_UNAVAILABLE`
- `MIGRATION_STATE_ERROR`
- `DERIVATION_FAILED`
- `RULE_EVALUATION_FAILED`
- `TASK_RUNTIME_NOT_READY`
- `TASK_RUNTIME_UNAVAILABLE`
- `INTERNAL_SERVER_ERROR`

Service/domain code should raise shared exceptions from `app.core.exceptions` instead of returning ad hoc error payloads.

## 4. Exception Mapping

The global exception handlers registered in `app.core.exceptions` are the single API mapping point.

Baseline mapping:

- request validation -> `400` + `VALIDATION_ERROR`
- domain/service not found -> `404`
- domain/service conflict or illegal state -> `409`
- database reachability failure -> `503` + `DATABASE_UNAVAILABLE`
- migration state failure -> `503` + `MIGRATION_STATE_ERROR`
- service-level derivation/runtime failure -> `503` + `DERIVATION_FAILED`
- service-level rule evaluation failure -> `503` + `RULE_EVALUATION_FAILED`
- unexpected internal failure -> `500` + `INTERNAL_SERVER_ERROR`

Routers should stay thin and should not handcraft their own error payload style.

## 5. Logging Baseline

TraceFold keeps a minimal centralized logger in `app.core.logging`.

This baseline adds structured event logging for key service paths only. It does not attempt full repository-wide audit logging.

Current priority coverage:

- capture submit / parse / route / direct commit
- pending fix / confirm / discard / force insert
- illegal pending state transition attempts
- rule evaluation / alert lifecycle create / acknowledge / resolve / invalidate
- knowledge summary derivation request / start / ready / failed / invalidate
- dashboard summary read failure
- health read not found failure
- knowledge read not found failure
- health rule rerun failure
- health AI summary rerun failure
- knowledge AI summary rerun failure
- migration upgrade / downgrade
- runtime startup schema bootstrap
- runtime status DB check failure

Structured event payloads should prefer these fields when available:

- `event`
- `domain`
- `target_id` or domain-specific id such as `capture_id` / `pending_item_id`
- `status` or result
- `error_code`

Do not log raw sensitive capture content by default.

## 6. Runtime Status Baseline

The formal shared status entry is:

- `GET /api/system/status`

Its purpose is a minimal system health summary, not a dashboard replacement.

Current response fields:

- `api_status`
- `db_status`
- `migration_head`
- `schema_version`
- `migration_status`
- `degraded_reasons`
- `task_runtime_status`
- `last_checked_at`

Current semantics:

- `api_status` is `ok` or `degraded`
- `db_status` is `ok` or `unavailable`
- `migration_status` is one of `ok`, `outdated`, `unknown`, `failed`
- `task_runtime_status` is now connected to formal task runtime state and currently returns `ready` or `degraded`
- failed formal AI derivations currently surface through `degraded_reasons` rather than a separate AI-only status field

This endpoint is shared system status. Web, Desktop, and Telegram may present it differently later, but they should not redefine its underlying meaning.

## 7. Runtime And Migration Responsibility Split

- formal schema state still belongs to the migration baseline in `docs/API_MIGRATION_BASELINE.md`
- `app.db.init_db` remains a runtime bootstrap compatibility entry that upgrades to migration `head`
- runtime status reads migration and DB state but does not take over migration ownership

## 8. Commands And Tests

Run from `apps/api/`.

Targeted validation commands:

```powershell
python -m pytest -q tests\core\test_error_responses.py tests\core\test_runtime_status.py tests\system\test_runtime_status_api.py
```

Recommended combined verification for this baseline plus migration smoke:

```powershell
python -m pytest -q tests\core\test_error_responses.py tests\core\test_runtime_status.py tests\system\test_runtime_status_api.py tests\db\test_migrations.py tests\domains\workbench\test_workbench_migration.py
```

If a later task extends runtime visibility, update this document instead of scattering new status semantics across multiple feature docs.
