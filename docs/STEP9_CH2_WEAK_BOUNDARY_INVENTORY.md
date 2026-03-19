# Step 9 Chapter 2 Weak Boundary Inventory

Each item uses the same record fields:

- `id`
- `title`
- `layer`
- `symptom`
- `why_it_exists`
- `risk_if_ignored`
- `category`
- `recommended_action`
- `whether_to_fix_now`
- `owner_hint`
- `linked_docs_or_files`

## WB-001

- `id`: WB-001
- `title`: Desktop shell config accepted weak startup and URL inputs
- `layer`: desktop / config
- `symptom`: `startup_mode`, `web_workbench_url`, and `api_base_url` were effectively soft strings.
- `why_it_exists`: Early shell bootstrap favored quick bring-up over stronger config validation.
- `risk_if_ignored`: Shell behavior could drift by environment typo, and startup/runtime ambiguity would keep spreading.
- `category`: MUST_FIX_IN_PHASE1
- `recommended_action`: Validate allowed startup modes and require explicit http/https URLs.
- `whether_to_fix_now`: yes
- `owner_hint`: codex
- `linked_docs_or_files`: `apps/desktop/app/core/config.py`, `apps/desktop/tests/test_config.py`, `apps/desktop/.env.example`

## WB-002

- `id`: WB-002
- `title`: Telegram adapter config accepted blank or weak API settings
- `layer`: telegram / config
- `symptom`: blank token, invalid API URL, and non-positive timeout were not explicitly rejected.
- `why_it_exists`: Adapter skeleton kept config permissive during initial bring-up.
- `risk_if_ignored`: Runtime failures would stay late and ambiguous, making the adapter feel more complete than it really is.
- `category`: MUST_FIX_IN_PHASE1
- `recommended_action`: Validate token presence, enforce explicit API URL shape, and require positive timeout.
- `whether_to_fix_now`: yes
- `owner_hint`: codex
- `linked_docs_or_files`: `apps/telegram/app/core/config.py`, `apps/telegram/tests/test_config.py`, `apps/telegram/.env.example`

## WB-003

- `id`: WB-003
- `title`: Workbench schema ensure script could be mistaken for a formal migration system
- `layer`: scripts / docs
- `symptom`: `migrate_step8_workbench.py` name and output could be read too quickly as a real migration subsystem.
- `why_it_exists`: Repository currently uses metadata bootstrap, not a formal migration stack.
- `risk_if_ignored`: Repo-style bootstrap could be overstated as hardened migration capability.
- `category`: MUST_FIX_IN_PHASE1
- `recommended_action`: Clarify in code and docs that it is a repo-style schema ensure path only.
- `whether_to_fix_now`: yes
- `owner_hint`: codex
- `linked_docs_or_files`: `apps/api/scripts/migrate_step8_workbench.py`, `docs/STEP8_TASK1_API_WORKBENCH.md`

## WB-004

- `id`: WB-004
- `title`: Step 9 acceptance runner could be overstated as a full E2E harness
- `layer`: scripts / docs
- `symptom`: The acceptance runner is a useful smoke orchestrator, but without explicit wording it can be mistaken for a new generalized test platform.
- `why_it_exists`: Step 9 needed a repeatable runner quickly and reused existing commands.
- `risk_if_ignored`: Later milestone language could drift into “full platform validation” claims that are not yet true.
- `category`: MUST_FIX_IN_PHASE1
- `recommended_action`: Mark the runner as a minimal smoke orchestrator and not a new framework.
- `whether_to_fix_now`: yes
- `owner_hint`: codex
- `linked_docs_or_files`: `apps/api/scripts/step9_ch1_acceptance_smoke.py`

## WB-005

- `id`: WB-005
- `title`: Desktop shell runtime remains skeleton-level rather than full native runtime
- `layer`: desktop / docs
- `symptom`: Current shell is intentionally limited to skeleton-level lifecycle, status, workbench opening, and minimal notification handling.
- `why_it_exists`: Phase 1 intentionally prioritized unified API/service semantics over native desktop completeness.
- `risk_if_ignored`: Only wording drift is risky; the implementation itself is within scope.
- `category`: ACCEPTABLE_TECH_DEBT
- `recommended_action`: Keep documenting it explicitly as skeleton/runtime baseline, not as fully completed native runtime.
- `whether_to_fix_now`: no further code fix now
- `owner_hint`: later
- `linked_docs_or_files`: `docs/STEP7_CH3_DESKTOP_SHELL_NOTES.md`, `docs/STEP7_CH3_ACCEPTANCE_SMOKE.md`, `docs/STEP9_CH1_SCOPE.md`

## WB-006

- `id`: WB-006
- `title`: Telegram remains a pull-based adapter instead of a richer workbench client
- `layer`: telegram / product boundary
- `symptom`: Telegram does not have template CRUD, workbench commands, or push orchestration.
- `why_it_exists`: This is the frozen Step 7 / Phase 1 scope.
- `risk_if_ignored`: The risk comes from people repeatedly treating the limitation as a defect rather than a deliberate boundary.
- `category`: NOT_A_PROBLEM
- `recommended_action`: Keep the boundary written down and reject scope creep.
- `whether_to_fix_now`: no
- `owner_hint`: manual
- `linked_docs_or_files`: `docs/STEP7_CH2_TELEGRAM_ADAPTER_NOTES.md`, `docs/STEP8_TASK3_ENTRY_ALIGNMENT.md`

## WB-007

- `id`: WB-007
- `title`: Repository still lacks a formal migration framework
- `layer`: api / scripts / platform
- `symptom`: Schema evolution still depends on metadata bootstrap and repo-style ensure scripts.
- `why_it_exists`: Phase 1 explicitly avoided introducing a new migration/deployment system.
- `risk_if_ignored`: Future schema evolution becomes harder to audit if the repo grows without a later migration decision.
- `category`: ACCEPTABLE_TECH_DEBT
- `recommended_action`: Keep it documented as repo-style bootstrap and revisit only when Phase 1 closure no longer holds.
- `whether_to_fix_now`: no
- `owner_hint`: later
- `linked_docs_or_files`: `apps/api/scripts/migrate_step8_workbench.py`, `docs/STEP8_TASK1_API_WORKBENCH.md`

## WB-008

- `id`: WB-008
- `title`: Web workbench verification is still source-contract plus build smoke, not browser E2E
- `layer`: web / tests
- `symptom`: Current UI verification relies on source-contract tests and `vite build`.
- `why_it_exists`: The repo does not yet maintain a dedicated browser-level test harness.
- `risk_if_ignored`: UI regressions can still slip between contract and real browser behavior.
- `category`: ACCEPTABLE_TECH_DEBT
- `recommended_action`: Record it honestly and revisit only if the repo adopts a sustained browser test stack.
- `whether_to_fix_now`: no
- `owner_hint`: later
- `linked_docs_or_files`: `apps/web/tests/test_workbench_home_contract.py`, `docs/STEP8_ACCEPTANCE.md`

## WB-009

- `id`: WB-009
- `title`: Existing `datetime.utcnow()` usage still produces deprecation warnings
- `layer`: api / service
- `symptom`: Several legacy services still emit UTC deprecation warnings during acceptance runs.
- `why_it_exists`: The warnings come from earlier service code and were not the mainline target of Step 7-9 work.
- `risk_if_ignored`: Noise in acceptance output and future compatibility pressure.
- `category`: ACCEPTABLE_TECH_DEBT
- `recommended_action`: Track as service-layer cleanup debt, but do not block Phase 1 closure on it.
- `whether_to_fix_now`: no
- `owner_hint`: later
- `linked_docs_or_files`: `apps/api/app/domains/pending/service.py`, `apps/api/app/domains/dashboard/service.py`

## WB-010

- `id`: WB-010
- `title`: Pytest collection conflicts still require separate Telegram/Desktop suite invocation
- `layer`: tests / repo hygiene
- `symptom`: Same-basename test modules across apps can still collide in some combined runs.
- `why_it_exists`: Multi-app test layout evolved incrementally without a unified naming cleanup pass.
- `risk_if_ignored`: Test runner usage remains slightly more fragile than the docs might imply if phrased too broadly.
- `category`: ACCEPTABLE_TECH_DEBT
- `recommended_action`: Keep scripted acceptance paths explicit about separate invocation until a later test-layout cleanup.
- `whether_to_fix_now`: no
- `owner_hint`: later
- `linked_docs_or_files`: `apps/api/scripts/step9_ch1_acceptance_smoke.py`

## WB-011

- `id`: WB-011
- `title`: Workbench frontend transient state does not form a second fact source
- `layer`: web / UI boundary
- `symptom`: The frontend does keep temporary UI editing state for template and shortcut forms.
- `why_it_exists`: Some transient UI state is necessary to support input and feedback.
- `risk_if_ignored`: None, as long as persisted semantics still come only from the shared API.
- `category`: NOT_A_PROBLEM
- `recommended_action`: Keep the current transient-only boundary and avoid local storage truth.
- `whether_to_fix_now`: no
- `owner_hint`: manual
- `linked_docs_or_files`: `apps/web/src/main.ts`, `docs/STEP8_SCOPE.md`
