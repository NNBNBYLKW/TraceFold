# Step 8 Scope

## Step 8 Goal

Step 8 establishes the Web workbench homepage as the main entry layer for continuing work.
It is not a feature pile homepage and not a replacement for formal business pages.

## In Scope

- independent workbench backend domain
- workbench home API and data structures
- Web workbench homepage
- template, shortcut, recent, and preferences contracts
- minimal recent recorder
- Desktop alignment to the workbench homepage
- Telegram regression protection

## Out of Scope

- template automation chains
- generic workflow or script engines
- drag-and-drop layout systems
- dashboard rewrite
- desktop-native business UI
- desktop-heavy business client
- Telegram template CRUD
- Telegram workbench or management-client mode
- AI write-back into formal fact tables
- knowledge-graph-centered product restructuring
- external-tool-led product reshaping
- second fact sources in entry layers

## Four Capability Roles

- `Template`: named work mode
- `Shortcut`: high-frequency fixed entry
- `Recent`: continue-work entry
- `Dashboard`: summary layer

## Homepage IA

The homepage order is fixed:

1. current mode
2. templates
3. fixed shortcuts
4. recent context
5. dashboard summary

## Mode Switching Boundary

- mode switching only changes entry context
- it does not change formal business semantics
- it does not mutate formal fact tables
- it does not introduce frontend-defined business rules
- frontend state remains transient UI state only
