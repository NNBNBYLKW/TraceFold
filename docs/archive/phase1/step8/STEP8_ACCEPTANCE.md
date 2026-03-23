# Step 8 Acceptance

## Acceptance Goals

Step 8 is accepted when it proves:

1. entering a useful work state requires less selection cost
2. `Template / Shortcut / Recent / Dashboard` remain distinct in role
3. formal fact semantics remain unpolluted

## Smoke Checklist

- workbench home renders and respects the frozen five-section IA
- active mode is visible
- builtin and user templates are visible
- template apply works
- shortcut management works
- recent shows the backend-defined five-item continue-work surface
- `viewed` and `acted` are visibly distinct
- dashboard summary is present but not dominant
- Desktop defaults to the workbench homepage and stays shell-only
- Telegram stays lightweight and does not grow a template/workbench system

## How To Judge Lower Selection Cost

- the user can land on `/workbench` and see current mode, fixed entries, recent recovery context, and summary in one place
- switching into a known context can happen through template or shortcut without re-deriving filters manually
- recent recovery does not require reopening a full dashboard-first flow

## How To Judge Role Separation

- templates define work modes, not actions
- shortcuts define entry targets, not workflows
- recent defines recovery context, not history logging
- dashboard summary remains summary-only and does not take over the page

## How To Judge No Formal-Fact Pollution

- template apply does not mutate expense, knowledge, health, or pending facts
- recent remains outside formal fact tables
- frontend state does not become a second source of truth
- Desktop and Telegram do not introduce entry-specific business writes
