# Step 8 Task 2: Web Workbench Home

## Purpose

Step 8 Task 2 establishes the Web workbench homepage as the main entry layer.
It does not replace formal business pages. It organizes entry context around:

1. current mode
2. templates
3. fixed shortcuts
4. recent context
5. dashboard summary

## Frontend Boundary

- the page consumes `/api/workbench/*` contracts instead of defining local template semantics
- workbench mode changes only adjust entry context
- formal business meaning for expense, knowledge, health, and pending remains unchanged
- local UI state is limited to transient editing state and flash feedback
- local storage is not used as a second source of truth

## Current Behavior

- `/workbench` is the default homepage route
- the navigation keeps existing formal pages intact
- template apply calls the backend directly
- user template create/edit/enable flows call the backend directly
- shortcut create/edit/delete/enable flows call the backend directly
- recent context shows the backend-provided five-item recovery surface
- dashboard summary stays at the bottom and keeps a summary-only role

## Non-Goals Still Preserved

- no frontend-defined template engine
- no shortcut action chaining
- no recent-as-history timeline
- no dashboard rewrite
- no second fact source in the browser
