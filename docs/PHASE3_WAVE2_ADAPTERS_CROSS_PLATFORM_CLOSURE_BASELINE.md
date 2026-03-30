# Phase 3 Wave 2 Adapters Cross-Platform Closure Baseline

## Purpose

This document records the bounded cross-platform closure state for the first lightweight capture adapter pack in Phase 3 Wave 2.

It exists to answer:

- whether Telegram and Feishu now behave as true siblings under one adapter contract
- what was actually closed in this bounded adapter pack
- what evidence supports the closure
- what remains explicitly outside the pack

This is a closure baseline, not a new system-definition document and not an authorization to expand adapter scope.

## Closure Scope

This bounded closure covers only:

- Telegram bot V1 as a lightweight text-to-capture adapter baseline
- Feishu bot V1 as a lightweight text-to-capture adapter baseline
- shared inheritance of the Wave 2 adapter contract in `docs/PHASE3_WAVE2_ADAPTER_CONTRACT_BASELINE.md`
- shared capture-first message-to-capture semantics
- shared light success/failure acknowledgement semantics
- shared source-traceability shape and boundaries

This bounded closure does not cover:

- broader bot platformization
- review actions in adapters
- direct-to-formal behavior
- Pending bypass
- AI parsing, suggestions, or classification
- workflow or approval behavior
- rich command, menu, or card growth
- multimodal input
- broader Wave 2 work beyond this adapter pack

## Contract-Sibling Confirmation

Telegram and Feishu now preserve the same core semantics in implementation and tests:

- text-only
- one message -> one capture
- capture-first
- structure-later
- review-later
- light success/failure acknowledgement
- no direct formal write
- no Pending bypass
- no review actions
- no AI behavior
- no workflow or control-surface drift
- no second intake model

Platform-specific implementation differences remain acceptable and bounded:

- Telegram uses a minimal polling runtime and Bot API client
- Feishu uses a minimal webhook runtime and Open API reply flow

These runtime differences do not change product meaning. Both remain lightweight adapters feeding the same capture-first chain.

## Source-Traceability Consistency

Telegram and Feishu now use parallel source metadata semantics:

- Telegram:
  - `source_type="telegram"`
  - `source_ref="chat:<id>:message:<id>:user:<id>"`
- Feishu:
  - `source_type="feishu"`
  - `source_ref="chat:<id>:message:<id>:user:<id>"`

In both adapters:

- `source_type` identifies the adapter channel only
- `source_ref` is traceability-oriented metadata only
- neither adapter turns source metadata into a separate business model

This is close enough for bounded cross-platform closure and future shared reasoning about capture origin.

## Acceptance-Parity Review

Telegram and Feishu are now at a sufficiently similar maturity level to be treated as one bounded adapter pack:

- both have a real runtime entry shape
- both have bounded setup documentation
- both have focused handler and client tests
- both have contract-consistency tests
- both have bounded substitute acceptance records instead of claimed live manual sessions

The current runtime shapes differ, but they are both credible for the narrow adapter role:

- Telegram:
  - minimal polling runtime
  - `/start`
  - `/help`
  - plain private text capture
- Feishu:
  - minimal webhook runtime
  - URL verification
  - minimal `start` / `help` guidance
  - plain text message capture

Neither adapter was hardened into a broader product surface.

## Verification Evidence

This bounded closure is supported by focused repo evidence rather than by a claimed live manual cross-platform session.

Focused tests that support the pack include:

- Telegram:
  - `apps/telegram/tests/test_app.py`
  - `apps/telegram/tests/test_main.py`
  - `apps/telegram/tests/test_capture_handler.py`
  - `apps/telegram/tests/test_dispatch.py`
  - `apps/telegram/tests/test_formatting.py`
  - `apps/telegram/tests/test_observability.py`
  - `apps/telegram/tests/test_telegram_final_consistency.py`
- Feishu:
  - `apps/feishu/tests/test_app.py`
  - `apps/feishu/tests/test_capture_handler.py`
  - `apps/feishu/tests/test_feishu_api_client.py`
  - `apps/feishu/tests/test_observability.py`
  - `apps/feishu/tests/test_feishu_final_consistency.py`
- shared capture-path grounding:
  - `apps/api/tests/domains/capture/test_capture_smoke.py`

The acceptance method for both adapters remains the strongest bounded substitute available in the current environment:

- runtime/startup or webhook entry inspection
- focused adapter tests
- focused capture-path verification
- explicit refusal to overclaim a live manual chat session where one did not occur

## Relationship To Current Project State

This closure does not reopen the current project center.

It preserves the frozen posture that:

- Web remains the main business interface
- the application service layer remains the only business-logic center
- Capture remains the upstream record layer
- Pending remains the review workbench
- Telegram and Feishu remain lightweight adapters, not product centers

It also preserves the current Wave 2 boundary:

- lightweight message-to-capture adapters
- not workflow bots
- not management surfaces
- not AI surfaces

## Final Closure Statement

Telegram and Feishu now form a formally closed bounded lightweight adapter pack within the current Phase 3 Wave 2 scope.

This statement applies only to the shared lightweight adapter pack recorded here.
It does not claim:

- whole Wave 2 completion beyond this bounded adapter pack
- whole Phase 3 completion
- whole frontend completion
- whole product completion
