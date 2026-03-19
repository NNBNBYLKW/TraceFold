# Step 9 Chapter 6 Phase 1 Acceptable Gaps And Tech Debt

## Acceptable Technical Debt

| item | why acceptable now | why not blocking Phase 1 closeout |
| --- | --- | --- |
| Desktop shell runtime is still skeleton-level | Desktop is shell-only by design in Phase 1 | It does not own business logic or facts |
| Repo-style ensure scripts are not a migration framework | Current bootstrap pattern is explicit and documented | It does not create a hidden write path |
| Web validation is not browser E2E | Build + contract tests still protect the current baseline | Lack of browser E2E does not break the unified mainline |
| `datetime.utcnow()` warnings remain in older services | Known technical debt with stable current behavior | Warning noise does not change semantics |
| Rule alert lifecycle does not expose `not run` as a dedicated user-facing state | Current UI already separates empty vs fetch failure | This is a clarity gap, not a fact-layer corruption risk |
| Multi-app pytest collection conflicts still exist | Explicit split commands are documented and used | Acceptance is still repeatable |

## Future-Possible But Not Part Of Phase 1

These are not Phase 1 gaps to “finish later inside the same phase”. They belong outside Phase 1 unless a later stage explicitly reopens them:

- automation engine
- graph-centered product design
- desktop-native business client
- Telegram management client
- AI-led formal fact mutation
- plugin platform
- multi-tenant / distributed architecture
- enterprise observability stack
- full browser E2E platform

## Minimum Skeletons That Must Not Be Misstated

- Desktop shell runtime
- acceptance smoke runners
- repo-style ensure scripts
- minimal entry adapters

These are acceptable in Phase 1 only if they continue to be described honestly as minimal or skeleton-level where that is the truth.
