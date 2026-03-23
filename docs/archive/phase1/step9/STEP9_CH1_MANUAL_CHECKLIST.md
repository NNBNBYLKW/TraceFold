# Step 9 Chapter 1 Manual Checklist

Use this checklist after running the automated smoke set. Record pass / fail for every item.

## Chain A: Web Mainline

- Submit a low-confidence capture from the Web/API path.
- Confirm it appears in pending before formalization.
- If that pending item needs a minimal correction, use the shared fix path first.
- Confirm the item through the shared pending action path.
- Open the resulting formal record.
- Verify dashboard summary and recent activity are visible.
- Fail if pending is bypassed or dashboard reflects transient UI state.

## Chain B: Telegram Entry

- Send a plain-text capture through Telegram adapter flow.
- Verify the same capture is visible through the shared pending/API surface.
- Verify Telegram does not expose `/force_insert`.
- Verify Telegram still does not expose template/workbench commands.
- Fail if Telegram behaves like a second review center or workbench.

## Chain C: Facts, Rules, and AI

- Create a formal health record that should open a health alert.
- Create a formal knowledge entry that should produce an AI derivation.
- Open the formal detail pages.
- Open alerts / AI derivations / dashboard summary.
- Verify the UI/API semantics still distinguish formal fact, rule alert, and AI derivation.
- Fail if AI appears to overwrite formal facts or if layers are indistinguishable.

## Chain D: Desktop Shell

- Start the Desktop shell skeleton.
- Verify the default target is the shared workbench homepage.
- Verify current mode is visible in the shell state.
- Verify recent recovery stays in shared workbench data.
- Verify Desktop still has no business page tree or business write action.
- Fail if Desktop behaves like a second business frontend.

## Chain E: Template To Work Context

- Apply a template.
- Verify current mode changes in workbench home.
- Verify shortcut / recent / dashboard roles remain separated.
- Verify the result points back to an actual work context.
- Verify formal facts were not changed by apply.
- Fail if template apply mutates facts or behaves like automation.

## Global Boundary Checks

- Verify there is still one shared capture mainline.
- Verify there is still one pending semantics.
- Verify Web, Telegram, and Desktop still consume shared API semantics.
- Verify no second fact source or silent fallback path appears.
- Verify critical failures remain visible and do not pretend success.
