# Step 8 API Contracts

## Workbench Home

`GET /api/workbench/home`

Returns:

- `current_mode`
- `templates`
- `pinned_shortcuts`
- `recent_contexts`
- `dashboard_summary`

`dashboard_summary` reuses existing dashboard summary semantics. It is not a second dashboard contract.

## Templates

- `GET /api/workbench/templates`
- `POST /api/workbench/templates`
- `GET /api/workbench/templates/{template_id}`
- `PATCH /api/workbench/templates/{template_id}`
- `POST /api/workbench/templates/{template_id}/apply`

Rules:

- builtin templates are readable and apply-able, but not mutable
- user templates are create/update/enable targets
- `apply` updates workbench preference context only
- `default_query_json` is validated in the service layer against a whitelist of frozen read-query keys

## Shortcuts

- `GET /api/workbench/shortcuts`
- `POST /api/workbench/shortcuts`
- `PATCH /api/workbench/shortcuts/{shortcut_id}`
- `DELETE /api/workbench/shortcuts/{shortcut_id}`

Rules:

- shortcuts express entry context only
- shortcuts do not express action chains, background jobs, or scripts
- ordering is stable and API-defined

## Recent

- `GET /api/workbench/recent`

Rules:

- recent is a continue-work surface, not a log surface
- default return remains homepage-safe
- dedupe key is `object_type + object_id + action_type`
- total cap remains `20`

## Preferences

- `GET /api/workbench/preferences`
- `PATCH /api/workbench/preferences`

Rules:

- only minimal Step 8 preference fields are supported
- invalid template ids return clear errors
- no hidden fallback semantics

## Error Semantics

- invalid template query defaults -> stable `400`
- invalid template references -> stable `404`
- internal failures do not expose internal exception text
- response envelope stays aligned with the shared `ApiResponse` contract
