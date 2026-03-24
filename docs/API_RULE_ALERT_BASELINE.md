# API Rule And Alert Baseline

## 1. Purpose

This document records the minimal formal rule evaluation and alert lifecycle baseline for `apps/api`.

Its role is intentionally narrow:

- evaluate a small set of explicit, explainable rules
- persist derived alert lifecycle state
- provide shared alert read and state-change entry points
- keep a clean handoff point for later task runtime driven rule reruns

It is not a DSL rule engine, notification platform, workflow engine, or alert center product.

## 2. Boundary

TraceFold keeps three layers separate:

- formal facts remain the system-of-record layer
- rules perform explicit, stable evaluation on formal inputs
- alerts are derived reminders, not formal facts

This means:

- acknowledging, resolving, or invalidating an alert does not mutate the formal record
- rule evaluation can read formal facts, but it does not gain authority over fact mutation
- model / AI output must not masquerade as rule output

## 3. Current Scope

This baseline only formalizes one small rule family:

- objective health metric rules

Current covered metric types are:

- `heart_rate`
- `sleep_duration`
- `blood_pressure`

The baseline is deliberately limited to health metrics because they are the clearest explainable rules already present in the repo.

## 4. Alert Model

Formal alert persistence now uses:

- `rule_alerts`

Current fields:

- `id`
- `domain`
- `rule_key`
- `severity`
- `status`
- `source_record_type`
- `source_record_id`
- `message`
- `details_json`
- `triggered_at`
- `acknowledged_at`
- `resolved_at`
- `resolution_note`
- `created_at`
- `updated_at`

Field intent stays narrow:

- `details_json` stores only minimal derived context such as display title, explanation, and source metric hints
- alerts should not become a second raw-data store
- alert rows describe reminder lifecycle, not fact history ownership

## 5. Status And Severity

Shared alert status values are:

- `open`
- `acknowledged`
- `resolved`
- `invalidated`

Shared severity values are:

- `info`
- `warning`
- `high`

The meaning is:

- `open`: active reminder
- `acknowledged`: seen and temporarily accepted as active
- `resolved`: explicitly handled by the user
- `invalidated`: no longer matches the current formal record state

## 6. Rule Evaluation Flow

Current lifecycle is:

`rule evaluation -> alert create/reopen -> acknowledge/resolve/invalidate`

For the current health rules:

- if a record matches a rule, the corresponding alert is opened or reopened
- if a different managed rule previously matched the same record, that older alert is invalidated
- if the record no longer matches any managed rule, existing managed alerts for that record are invalidated instead of being deleted

This keeps lifecycle visible without turning alerts into a full event-history system.

## 7. API Surface

Formal alert API entry points are:

- `GET /api/alerts`
- `GET /api/alerts/{id}`
- `POST /api/alerts/{id}/acknowledge`
- `POST /api/alerts/{id}/resolve`

Current list query filters are intentionally minimal:

- `status`
- `domain`
- `rule_key`

Compatibility aliases from the earlier baseline are still accepted where helpful, but the formal lifecycle contract is based on the fields above.

## 8. Logging Baseline

Rule and alert lifecycle now use the shared structured logging helper.

Current key events include:

- `rule_evaluated`
- `rule_evaluation_failed`
- `alert_created`
- `alert_reopened`
- `alert_acknowledged`
- `alert_resolved`
- `alert_invalidated`
- `alert_query_failed`

Preferred stable fields:

- `event`
- `domain`
- `action`
- `rule_key`
- `alert_id`
- `source_record_type`
- `source_record_id`
- `result`
- `error_code`

## 9. Task Runtime Handoff

This task does not move all rule evaluation into background tasks.

The formal handoff point is:

- the rule evaluation service is callable on its own
- synchronous/manual rerun paths still exist
- later task runtime work can invoke the same service boundary instead of inventing rule logic inside task executors

This keeps task runtime as an execution layer and keeps rule logic inside the rule service boundary.

## 10. Migration Baseline

Rule/alert lifecycle schema is formally migrated through Alembic.

Current head revision:

- `apps/api/migrations/versions/20260323_0005_ai_derivation_runtime_baseline.py`

The formal alert lifecycle schema itself was introduced by:

- `apps/api/migrations/versions/20260323_0004_rule_alert_lifecycle_baseline.py`

## 11. Commands And Tests

Run from `apps/api/`.

Targeted rule/alert validation:

```powershell
python -m pytest -q tests\domains\alerts\test_alert_service.py tests\domains\alerts\test_alert_consumption.py tests\domains\health\test_health_rules.py
```

Recommended combined verification with migration and runtime status:

```powershell
python -m pytest -q tests\domains\alerts\test_alert_service.py tests\domains\alerts\test_alert_consumption.py tests\domains\health\test_health_rules.py tests\db\test_migrations.py tests\core\test_runtime_status.py tests\system\test_runtime_status_api.py
```
