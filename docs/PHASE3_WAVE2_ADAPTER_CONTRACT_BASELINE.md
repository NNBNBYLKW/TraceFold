# Phase 3 Wave 2 Adapter Contract Baseline

## Purpose

This document defines the bounded unified contract for lightweight capture adapters in Phase 3 Wave 2.

It is the implementation-grounded contract baseline that follows the direction note in:

- `docs/PHASE3_WAVE2_LIGHTWEIGHT_CAPTURE_ADAPTERS_DIRECTION_NOTE_V1`

Use it to answer:

- what a lightweight capture adapter is allowed to submit
- which minimal source metadata belongs in adapter submissions
- what success and failure feedback semantics should look like
- which behaviors are explicitly forbidden in adapters
- how future Telegram and Feishu adapter work should stay aligned with the existing capture-first chain

It is a contract baseline, not a bot feature spec, not a generalized adapter framework, and not an authorization to implement Telegram or Feishu bot V1 in this run.

## Project Position

This contract inherits the current frozen project shape:

- local-first remains central
- SQLite remains the single source of truth
- the application service layer remains the only business-logic center
- Web remains the main business interface
- Capture remains the upstream record layer
- Pending remains the review workbench
- formal facts remain downstream and primary
- Desktop remains shell-level entry
- Telegram remains a lightweight adapter in project positioning

The main chain remains:

`Capture -> Parse -> Pending -> Confirm -> Formal Record -> Query / Analysis / Derivation`

Adapters must feed that chain. They must not redefine it.

## Why This Contract Exists

Phase 3 Wave 1 already reduced Web-side input friction through:

- Quick Capture
- Bulk Intake with Preview
- Capture Inbox / Intake Triage

The next bounded problem is not “build a bot product.” It is:

- reduce the friction of starting a capture from lightweight message surfaces
- keep future Telegram and Feishu entry points on one capture-first contract
- prevent adapter-specific business logic branches from emerging

Without a shared contract, each adapter can drift into its own semantics, its own source metadata shape, or its own downstream control behavior.

This document exists to stop that drift before channel-specific implementation begins.

## First-Version Contract Scope

The unified lightweight adapter contract supports only:

- pure-text
- single-item
- one-shot capture submission
- light success/failure acknowledgement

It does not support:

- attachments
- images
- voice
- multimodal payloads
- bulk import
- review actions
- complex command systems
- adapter-side workflow behavior

## Canonical Submission Contract

The current backend grounding point is the existing capture submit surface:

- `POST /api/capture`

The current request and response shape already lives in:

- `apps/api/app/domains/capture/schemas.py`
- `apps/api/app/domains/capture/router.py`
- `apps/api/app/domains/capture/service.py`

### Adapter Request Shape

For this bounded contract, an adapter submits:

- `raw_text`
- `source_type`
- `source_ref` when available

The current backend request model is:

```json
{
  "raw_text": "user text",
  "source_type": "telegram",
  "source_ref": "chat:10:message:9:user:11"
}
```

In first-version adapter work:

- `raw_text` is required and must be plain text
- `source_type` is required and identifies the adapter channel
- `source_ref` is optional but strongly recommended for traceability

Adapters do not submit:

- formal payloads
- parsed payload overrides
- review decisions
- direct formal-write instructions

### Source Metadata Rules

`source_type` should be a stable adapter identifier, for example:

- `telegram`
- `feishu`

It should describe the entry source, not the downstream domain.

`source_ref` should be an opaque adapter-side reference string that helps trace the origin of the message without moving business logic into the adapter. The current repo already uses this pattern:

- Telegram handler builds references like `chat:<id>:message:<id>:user:<id>`
- bulk intake uses bounded file-item references for imported text blocks

The backend should treat `source_ref` as traceability metadata, not as a command channel.

## Canonical Response Semantics

The current capture submit result model already returns:

- `capture_created`
- `capture_id`
- `status`
- `route`
- `target_domain`
- optional `pending_item_id`
- optional `formal_record_id`

For lightweight adapters, this result must be interpreted narrowly.

### Required Success Meaning

On success, an adapter may acknowledge only that:

- a capture record was created
- the message entered the normal chain
- the immediate route was capture-only, pending, or formal under existing backend rules

The adapter response should stay short and light. It may mention:

- capture created
- pending item created
- direct formal commit occurred under existing backend semantics

It should not turn that result into a deeper adapter-side workflow.

### Required Failure Meaning

On failure, an adapter should provide short feedback only, such as:

- service unavailable
- invalid input
- try again later

It should not:

- expose stack traces
- expose raw backend internals
- create a multi-step recovery flow
- silently drop text without feedback

## Adapter Responsibilities

A lightweight adapter is responsible for only these things:

1. accept plain-text single-item input
2. normalize it enough to call the shared capture submit API
3. attach stable `source_type` and best-effort `source_ref`
4. return short success/failure feedback
5. hand deeper follow-up back to the Web workbench surfaces when needed

The adapter is not responsible for:

- deciding whether something should become Pending
- deciding whether something should become a formal record
- applying review actions
- interpreting the formal record as a local business object
- managing downstream workflow

## Forbidden Adapter Behaviors

Under this contract, adapters are explicitly forbidden from:

- writing directly to formal records
- bypassing Pending or review semantics
- performing `confirm`, `discard`, `force_insert`, or `fix`
- introducing bot-side review workbenches
- performing AI parsing, classification, or suggestions
- adding workflow or orchestration behavior
- adding automation or template-execution behavior
- adding attachments or multimodal input in the first version
- becoming management or control surfaces

These are not “later in the same contract.” They require separate scoped work if they are ever opened at all.

## Relationship To Existing Repo State

This contract is intentionally grounded in the current implementation rather than being purely aspirational.

### Capture grounding

The current capture submit path already provides the needed minimal contract base:

- plain-text `raw_text`
- explicit `source_type`
- optional `source_ref`
- lightweight result fields suitable for short acknowledgement

That is enough to support first-version lightweight adapters without introducing a second business path.

### Web grounding

Quick Capture is the current Web analogue of this contract:

- one text input
- one submit action
- capture-first semantics
- success/failure handling designed for repeated use

Future adapters should feel closer to that surface than to a workflow console.

### Existing Telegram code reality

The repo currently contains `apps/telegram` code and tests that expose a broader historical adapter surface, including:

- plain-text capture
- some pending reads and actions
- dashboard, alerts, and status reads

That existing code is useful grounding for source metadata and short feedback patterns, but it is not the contract boundary for future Phase 3 Wave 2 lightweight capture adapters.

For this Wave 2 contract:

- the capture path is the canonical inheritance point
- broader Telegram command behavior is not automatically carried forward
- future Telegram and Feishu capture work should start from this narrower contract, not from the broadest historical adapter surface present in the repo

## Handoff Back Into The Main Workbench

Lightweight adapters are entry points, not completion environments.

When deeper work is needed, the handoff target remains the main Web workbench and its current pages:

- Capture for inbox and traceability
- Pending for formal review
- downstream formal pages for deeper readback

This contract does not require a deep-link system in its first version.

It requires only that adapters do not pretend to own the deeper workflow.

## Future Inheritance Rule

Future Telegram and Feishu adapter work should inherit this contract unless a later scoped pack explicitly changes it.

That means both should start with the same narrow assumptions:

- pure-text single-item submission only
- capture-first semantics only
- light success/failure feedback only
- no review actions
- no direct formal writes
- no AI behavior
- no workflow behavior

If a future implementation needs more than that, it should open a new bounded scope instead of silently widening the adapter contract.

## Final Contract Statement

The unified lightweight adapter contract for Phase 3 Wave 2 is:

- submit plain-text single-item input into the existing capture submit path
- include stable adapter source metadata
- return short acknowledgement or failure feedback
- stop at capture-first entry semantics

Everything deeper than that remains in the existing service-centered main chain and Web workbench surfaces.
