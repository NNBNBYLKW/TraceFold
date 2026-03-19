# Step 9 Chapter 5 Allowed Exceptions And Minimal Skeletons

## Allowed Minimal Skeletons

These are allowed now, but they are not completed Phase 1 capabilities:

- Desktop shell runtime skeleton
- repo-style schema ensure scripts
- minimum acceptance runners
- workbench mode scaffolding
- adapter-level Telegram commands

## What “Allowed Minimal Skeleton” Means

- it supports the current frozen scope only
- it does not silently unlock future scope
- it must be described as skeleton / minimum / repo-style when that is the truth
- it must not be presented as a fully hardened platform subsystem

## Future-Possible But Not Allowed To Mix In Now

- automation engine or workflow builder
- knowledge graph as system center
- desktop-native business client
- Telegram management client
- AI-led fact mutation
- plugin platform
- multi-tenant or distributed architecture

These may be revisited only if a later phase explicitly reopens them.

## Not A Problem Cases

- Desktop showing minimal current mode or service status
  - acceptable because it remains shell-only
- Telegram exposing lightweight capture/pending/status commands
  - acceptable because it stays adapter-only and uses the shared API
- Workbench holding templates, shortcuts, recent, and dashboard summary together
  - acceptable because role boundaries are still explicit and separate
