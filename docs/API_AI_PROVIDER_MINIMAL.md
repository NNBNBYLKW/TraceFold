# API AI Provider Minimal

## Purpose

This document records the minimal real provider integration now used by the formal `knowledge_summary` derivation path.

It is intentionally narrow:

- one provider abstraction style: OpenAI-compatible
- one first implementation: local Ollama
- one formal derivation target: `knowledge_summary`

It is not:

- a multi-provider platform
- a prompt registry
- a model routing matrix
- an AI center

## Current Integration Status

Current formal status:

- `knowledge_summary` is now connected to a real provider path
- provider type: `openai_compatible`
- default local target: Ollama
- current local model: `qwen3.5:9b`

This is a minimal Post-Phase-1 provider integration, not a general AI platform baseline.

## Current Provider Shape

TraceFold now uses a minimal provider split:

- `app.ai.providers`: provider resolution and shared config handoff
- `app.ai.openai_compatible`: actual HTTP call implementation
- `app.ai.service`: fixed prompt assembly and derivation-oriented metadata

The only formal provider type in scope is:

- `openai_compatible`

The default local target is:

- base URL: `http://127.0.0.1:11434/v1`
- model: `qwen3.5:9b`
- completion allowance: tuned to `1800`

## Current Env Contract

Current env variables are:

- `TRACEFOLD_AI_PROVIDER`
- `TRACEFOLD_AI_BASE_URL`
- `TRACEFOLD_AI_API_KEY`
- `TRACEFOLD_AI_MODEL`
- `TRACEFOLD_AI_TIMEOUT_SECONDS`

Current default local values:

```env
TRACEFOLD_AI_PROVIDER=openai_compatible
TRACEFOLD_AI_BASE_URL=http://127.0.0.1:11434/v1
TRACEFOLD_AI_API_KEY=
TRACEFOLD_AI_MODEL=qwen3.5:9b
TRACEFOLD_AI_TIMEOUT_SECONDS=60
```

For local Ollama, `TRACEFOLD_AI_API_KEY` may stay empty.

These defaults are also the current formal local acceptance path for provider-backed `knowledge_summary`.

## Knowledge Summary Flow

The formal flow is:

1. `POST /api/ai-derivations/knowledge/{id}/recompute`
2. create task run: `knowledge_summary_recompute`
3. executor calls derivation service
4. derivation service reads formal `knowledge_entries`
5. fixed backend prompt is built
6. provider returns JSON
7. JSON is validated into:
   - `summary`
   - `key_points`
   - `keywords`
8. result is written to `ai_derivations`

Formal facts are never rewritten by this path.

## Current Real Ollama Response Behavior

Current local Ollama behavior with `qwen3.5:9b` matters for troubleshooting:

- Ollama may place a large amount of reasoning text in `choices[0].message.reasoning`
- when completion allowance is not enough, `finish_reason` may be `length`
- in that state, `choices[0].message.content` may still be empty
- `reasoning` must not be treated as the formal derivation result

The current implementation only accepts formal completion content. It does not promote reasoning text into `summary`.

## Current Local Stability Tuning

The current local tuning remains intentionally small:

- completion allowance is kept at `1800`
- the fixed backend prompt was compressed to reduce repeated formatting instructions
- null-valued formal fields are omitted from the user prompt payload
- the model remains `qwen3.5:9b`

This tuning exists to reduce:

- `finish_reason=length`
- empty formal `content`
- partial JSON output

It does not introduce:

- multi-model routing
- retries
- fallback providers
- prompt registry mechanics

## Output Contract

Provider output must be valid JSON and must contain exactly:

```json
{
  "summary": "string",
  "key_points": ["string"],
  "keywords": ["string"]
}
```

Current implementation rules:

- only formal completion content is accepted
- only strict JSON results are accepted
- required fields are:
  - `summary`
  - `key_points`
  - `keywords`
- empty content, non-JSON, missing fields, or invalid payload shape all land `failed`

The current implementation does not do:

- stub results
- automatic retry
- fallback provider

Reasoning text, markdown, code fences, or other non-JSON output do not count as success.

## Failure Classification

Current failure categories are intentionally explicit:

- `AI provider request timed out.`
- `AI provider returned a non-2xx response.`
- `AI provider request failed.`
- `AI provider returned invalid response JSON.`
- `AI provider returned empty completion content.`
- `AI provider returned non-JSON completion content.`
- `AI provider returned an invalid JSON payload shape.`

At the derivation level, these provider-side failures result in derivation `failed`.

## Failure Boundary

Provider failure does not block:

- API startup
- formal fact reads
- formal fact writes

It only affects derivation generation:

- the derivation row lands in `failed`
- task runtime shows a failed recompute
- Web keeps formal knowledge content readable while AI summary shows failed / unavailable

## Investigation Notes

If `knowledge_summary` falls to `failed`, check these first:

- whether `finish_reason` is `length`
- the logged `response.text` excerpt
- the raw `choices[0].message` object
- whether `choices[0].message.content` is empty
- whether `choices[0].message.reasoning` exists
- whether the returned completion content is actually valid JSON

Current debug logging captures the empty-content case so this path can be inspected without changing formal fact behavior.

## Current Scope Boundary

Current formal scope remains narrow:

- only `knowledge_summary`
- no health AI
- no dashboard AI
- no multi-provider platform
- no prompt platform
- no formal fact mutation

AI remains a derivation / explanation layer only.

## Manual Check

With API running and provider reachable:

1. `GET /api/knowledge?page=1&page_size=20`
2. pick one knowledge id
3. `POST /api/ai-derivations/knowledge/{id}/recompute`
4. `GET /api/tasks?limit=20`
5. `GET /api/ai-derivations/knowledge/{id}`

Expected result:

- a new `knowledge_summary_recompute` task appears
- final derivation status becomes `ready`
- `content_json.summary` contains real model output
- `content_json.key_points` and `content_json.keywords` are populated
- the existing Web Knowledge detail page can display the real AI summary
