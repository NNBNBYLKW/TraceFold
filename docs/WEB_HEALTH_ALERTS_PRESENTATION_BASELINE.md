# Web Health Alerts Presentation Baseline

## Purpose

This document records the current Web presentation baseline for Health pages and formal rule-based alerts.

Its role is intentionally narrow:

- Health pages remain formal record read surfaces
- health alerts are shown as a separate reminder layer
- the page consumes existing formal alert lifecycle APIs only

It is not:

- an alerts center
- a notification platform
- a rule configuration surface
- a replacement for the formal health record

## Consumed APIs

The current Health pages consume:

- `GET /api/health`
- `GET /api/health/{id}`
- `GET /api/alerts`

For minimal alert lifecycle actions, they also use:

- `POST /api/alerts/{id}/acknowledge`
- `POST /api/alerts/{id}/resolve`

Current Health alert reads use the existing formal filters:

- `domain=health`
- `source_record_id={id}` on detail pages when needed

## Presentation Boundary

The boundary is explicit:

- formal health records remain the primary read layer
- rule-based alerts are reminders derived from formal health records
- acknowledging or resolving an alert does not rewrite the formal health fact
- resolved does not mean the health record was automatically corrected

## Alert State Handling

The Health alert section now distinguishes:

- `open`
- `acknowledged`
- `resolved`
- `empty`
- `unavailable`

`open`, `acknowledged`, and `resolved` are presented in separate subsections so the lifecycle is visible without turning the page into an alert management console.

## Minimal Actions

Health alerts now include small lifecycle actions where the formal API already exists:

- `Acknowledge Alert`
- `Resolve Alert`

These actions stay intentionally small:

- they update alert lifecycle state
- they refresh the current page
- they do not open a workflow or task center

## Manual Smoke

With a fresh demo DB and the normal API/Web startup flow already running:

1. Open `http://127.0.0.1:3000/health`
2. Confirm the page shows a `Formal Records` section and a separate `Rule Alerts` section
3. Open any seeded health detail page
4. Confirm the page shows:
   - `Formal Record`
   - `Rule Alerts`
   - `Source Reference`
5. In the alerts section, verify any seeded alert appears under the correct lifecycle heading
6. Use `Acknowledge Alert` or `Resolve Alert` on an open alert and confirm the page refreshes into the updated lifecycle state

If you need a direct API check during smoke:

- `GET /api/alerts?domain=health`
- `GET /api/alerts?domain=health&status=open`

## Automated Verification

Recommended Web-focused checks:

```powershell
python -m pytest -q apps/web/tests/test_semantics_contract.py apps/web/tests/test_health_ai_ui_contract.py apps/web/tests/health/test_health_alerts_consumption.py
```
