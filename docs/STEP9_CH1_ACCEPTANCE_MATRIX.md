# Step 9 Chapter 1 Acceptance Matrix

## Chain A: Web Mainline Closure

- Preconditions:
  - API and Web workbench routes are available.
  - Empty or isolated test database.
- Steps:
  - Submit capture from Web path.
  - Read pending detail.
  - If the shared pending semantics require correction for that item, run the minimal fix path.
  - Confirm pending.
  - Open resulting formal detail.
  - Read dashboard summary.
- Expected results:
  - Capture enters the shared capture mainline.
  - Low-confidence input becomes pending.
  - The item follows the same pending review semantics as every other entry surface.
  - Confirm produces a formal record.
  - Formal detail references source capture / pending correctly.
  - Dashboard consumes formal-layer or enhancement-layer data, not transient UI state.
- Failure conditions:
  - Pending can be bypassed without the shared semantics.
  - Formal record appears without shared confirm semantics.
  - Dashboard reflects temporary state instead of formal / enhancement layers.
- Boundary checkpoints:
  - Same capture chain.
  - Same pending semantics.
  - Formal fact boundary preserved.

## Chain B: Telegram Entry Through Shared Mainline

- Preconditions:
  - Telegram adapter uses the shared HTTP API only.
  - Shared API capture and pending routes are available.
- Steps:
  - Submit plain-text capture through Telegram.
  - Read pending through shared API / Web-side contract.
  - Verify unsupported high-risk command remains unavailable.
- Expected results:
  - Telegram capture becomes a normal shared capture.
  - Pending is visible through the shared API surface.
  - Telegram does not expose force-insert or template/workbench logic.
- Failure conditions:
  - Telegram adds a special write path.
  - Telegram state diverges from Web/API state.
  - Telegram grows a second workbench or review center.
- Boundary checkpoints:
  - Adapter-only behavior.
  - No force_insert.
  - No Telegram-only semantics.

## Chain C: Formal Facts With Rules / AI Enhancement Visibility

- Preconditions:
  - Formal health and knowledge write paths are available.
  - Rules and AI derivation layers are enabled as already implemented.
- Steps:
  - Create a formal health record that should trigger a rule alert.
  - Create a formal knowledge entry that should trigger an AI derivation.
  - Read formal detail pages.
  - Read alerts / AI derivations / dashboard summary.
- Expected results:
  - Formal fact is visible as a formal fact.
  - Rule output is visible as alert output.
  - AI output is visible as derivation output.
  - Dashboard summary shows the relevant summary / alert signal without collapsing the layers.
- Failure conditions:
  - AI overwrites formal fact meaning.
  - Rule alerts and AI derivations collapse into one layer.
  - Summary surfaces cannot distinguish fact vs enhancement.
- Boundary checkpoints:
  - Formal fact layer remains authoritative.
  - AI does not write back into formal facts.
  - Rules and AI consume allowed inputs only.

## Chain D: Desktop Shell To Shared Workbench

- Preconditions:
  - Desktop shell runtime skeleton is available.
  - Shared workbench and status APIs are available.
- Steps:
  - Bootstrap the Desktop shell.
  - Open the default workbench target.
  - Read current mode from the shared workbench home.
  - Verify recent context remains a shared workbench concept.
- Expected results:
  - Desktop opens the shared `/workbench` target.
  - Active mode is visible through shared workbench state.
  - Recent recovery remains in shared workbench data, not a desktop-owned source.
  - Desktop exposes shell actions only.
- Failure conditions:
  - Desktop owns business pages or business write paths.
  - Desktop has a separate template / recent system.
  - Desktop runtime is described as more complete than the current skeleton reality.
- Boundary checkpoints:
  - Shell-only role preserved.
  - Shared status path reused.
  - Shared workbench path reused.

## Chain E: Template Apply To Real Work Context

- Preconditions:
  - Workbench home, templates, shortcuts, recent, and preferences APIs are available.
- Steps:
  - Create or select a template.
  - Apply the template.
  - Read workbench home.
  - Verify the resulting context and pinned shortcuts.
- Expected results:
  - Current mode changes.
  - Shortcut / Recent / Dashboard roles remain distinct.
  - The result points back to a real work context.
  - Formal fact tables are unchanged by apply.
- Failure conditions:
  - Template apply mutates formal facts.
  - Template behaves like an automation chain.
  - Dashboard, Shortcut, or Recent absorbs template responsibility.
- Boundary checkpoints:
  - Template is mode / context only.
  - Shortcut is fixed entry only.
  - Recent is continue-work only.
  - Dashboard remains summary only.
