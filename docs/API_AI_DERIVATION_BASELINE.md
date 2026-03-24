# API AI Derivation Baseline

## 1. Purpose

This document records the minimal formal AI derivation runtime and storage baseline for `apps/api`.

Its role is intentionally narrow:

- persist formal derivation state
- keep derivation lifecycle visible
- provide a shared read path
- connect formal recompute to the existing task runtime

It is not an agent platform, prompt center, model orchestration layer, or AI-led fact mutation system.

## 2. Boundary

TraceFold keeps formal facts and AI derivations separate:

- formal facts remain the system-of-record layer
- AI derivations are derived explanation and presentation support
- derivations may be invalidated, recomputed, or fail
- derivations do not confirm, discard, force-insert, or rewrite formal facts

This means:

- a derivation row never becomes the authoritative fact row
- invalidating or recomputing a derivation does not mutate the formal record
- AI output must remain distinguishable, traceable, and replaceable

## 3. Current Scope

This baseline formalizes exactly one derivation kind:

- `knowledge_summary`

Why this one was chosen:

- it naturally belongs to the derivation / explanation layer
- it avoids drifting into medical judgment
- it provides a clear target, source-basis, and model-version contract
- it lets task runtime attach to AI work without expanding into a multi-domain AI platform

Existing older health summary paths may still exist for compatibility, but they are not the formal scope of this baseline.

## 3.1 Current Provider Integration

`knowledge_summary` now uses a minimal real provider path:

- provider contract: OpenAI-compatible
- current first provider: local Ollama
- current default model target: `qwen3.5:9b`

This stays intentionally narrow:

- one provider implementation
- one formal derivation kind
- fixed backend prompt
- strict JSON output

It does not introduce a multi-provider platform or prompt registry.

## 4. Derivation Model

Formal derivation persistence now uses:

- `ai_derivations`

Current fields:

- `id`
- `target_type`
- `target_id`
- `derivation_kind`
- `status`
- `model_key`
- `model_version`
- `source_basis_json`
- `content_json`
- `error_message`
- `generated_at`
- `invalidated_at`
- `created_at`
- `updated_at`

Field intent stays narrow:

- `source_basis_json` stores a compact target-and-basis summary rather than raw copied content
- `content_json` stores the formal derivation payload shown to callers
- `error_message` stores minimal visible failure context
- rows describe the current derivation lifecycle for one target/kind pair rather than a generic AI artifact warehouse

## 5. Status And Versioning

Shared derivation status values are:

- `pending`
- `running`
- `ready`
- `failed`
- `invalidated`

Current version semantics are intentionally minimal:

- `model_key` stores the configured provider model name used for generation
- `model_version` stores the current provider + prompt contract version used for the stored result

For the current formal derivation:

- `model_key = qwen3.5:9b` by default
- `model_version = openai_compatible:knowledge_summary_json_v3` by default

Provider-side runtime configuration is kept separate from derivation row identity. Current env variables are:

- `TRACEFOLD_AI_PROVIDER`
- `TRACEFOLD_AI_BASE_URL`
- `TRACEFOLD_AI_API_KEY`
- `TRACEFOLD_AI_MODEL`
- `TRACEFOLD_AI_TIMEOUT_SECONDS`

## 6. Source Basis And Invalidation

`source_basis_json` currently captures:

- `target_type`
- `target_id`
- `derivation_kind`
- `source_capture_id`
- `source_pending_id`
- `content_fingerprint`
- `source_updated_at`

This is deliberately compact. It exists so the system can distinguish:

- which formal target the derivation belongs to
- which upstream source references it came from
- what minimal basis fingerprint was used when the derivation was generated

Current invalidation semantics are also deliberately simple:

- explicit invalidation marks the current derivation row as `invalidated`
- recompute requests may first invalidate the current row, then hand off generation to task runtime
- invalidation does not delete the formal fact
- invalidation does not imply recompute already succeeded

This baseline does not maintain a full derivation history chain.

## 7. API Surface

Formal AI derivation API entry points are:

- `GET /api/ai-derivations`
- `GET /api/ai-derivations/{target_type}/{target_id}`
- `POST /api/ai-derivations/{target_type}/{target_id}/recompute`
- `POST /api/ai-derivations/{target_type}/{target_id}/invalidate`

Current list query filters are intentionally minimal:

- `target_type`
- `derivation_kind`
- `status`

Current formal target/kind support:

- `target_type = knowledge`
- `derivation_kind = knowledge_summary`

Compatibility aliases from the earlier baseline are still accepted where helpful, but the formal contract is based on the fields above.

## 8. Task Runtime Handoff

Formal recompute is intentionally attached to the existing single-process task runtime.

Current task type:

- `knowledge_summary_recompute`

The handoff boundary is:

- task runtime owns request/execute/failure visibility
- derivation service owns derivation lifecycle state
- knowledge service owns formal fact access
- the executor only calls the derivation service and does not contain AI business logic

This keeps task runtime as an execution layer, not an AI center.

Provider unavailability does not block API startup or formal fact reads/writes. It only affects derivation generation:

- derivation requests may end in `failed`
- task-backed recompute may fail visibly
- formal facts remain available

## 9. Logging And Runtime Status

Key derivation lifecycle events now use the shared structured logging helper.

Current covered events include:

- `derivation_requested`
- `derivation_recompute_requested`
- `derivation_started`
- `derivation_ready`
- `derivation_failed`
- `derivation_invalidated`
- `derivation_query_failed`

Preferred stable fields:

- `event`
- `domain`
- `action`
- `derivation_kind`
- `derivation_id`
- `target_type`
- `target_id`
- `result`
- `error_code`
- `task_id`

Runtime status does not introduce a second AI-specific status field. Instead:

- failed derivations contribute to `degraded_reasons`
- current degraded signal is `ai_derivations_failed_present`

## 9.1 Output Contract

The formal provider output for `knowledge_summary` is strongly constrained to JSON with exactly:

```json
{
  "summary": "string",
  "key_points": ["string"],
  "keywords": ["string"]
}
```

Real provider transport is now documented separately in:

- `docs/API_AI_PROVIDER_MINIMAL.md`

## 10. Migration Baseline

AI derivation runtime schema is formally migrated through Alembic.

Current head revision:

- `apps/api/migrations/versions/20260323_0005_ai_derivation_runtime_baseline.py`

This revision upgrades the older `ai_derivation_results` storage into formal `ai_derivations` lifecycle state.

## 11. Commands And Tests

Run from `apps/api/`.

Targeted derivation validation:

```powershell
python -m pytest -q tests\domains\ai_derivations\test_derivation_service.py tests\domains\ai_derivations\test_derivation_api.py tests\domains\ai_derivations\test_derivation_task_flow.py
```

Recommended combined verification with task runtime, runtime status, and migration smoke:

```powershell
python -m pytest -q tests\domains\ai_derivations\test_derivation_service.py tests\domains\ai_derivations\test_derivation_api.py tests\domains\ai_derivations\test_derivation_task_flow.py tests\domains\system_tasks\test_task_service.py tests\domains\system_tasks\test_task_api.py tests\core\test_runtime_status.py tests\system\test_runtime_status_api.py tests\db\test_migrations.py
```
