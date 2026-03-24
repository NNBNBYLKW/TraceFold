# API Migration Baseline

## 1. Purpose

This document records the formal post-Phase-1 migration baseline for `apps/api`.

The goal is to stop relying on one-off `init_db()` / `create_all()` schema bootstrap as the primary evolution path. From this point forward:

- formal schema history lives under `apps/api/migrations/`
- schema upgrades run through Alembic revisions
- `app.db.init_db:init_db()` is a compatibility bootstrap that upgrades to `head`
- direct `Base.metadata.create_all()` is reserved for isolated tests only

## 2. Current Baseline

The initial formal baseline revision is:

- `apps/api/migrations/versions/20260323_0001_phase1_baseline.py`

The current migration head is:

- `apps/api/migrations/versions/20260323_0005_ai_derivation_runtime_baseline.py`

It captures the current SQLite schema for:

- capture / parse / pending
- formal read tables for expense / knowledge / health
- alerts
- AI derivations
- task runtime state
- workbench tables

## 3. Commands

Run commands from `apps/api/`.

Install dependencies:

```powershell
python -m pip install -r requirements.txt
```

Upgrade the configured database to the latest revision:

```powershell
python scripts/run_migrations.py upgrade head
```

Show the current revision:

```powershell
python scripts/run_migrations.py current
```

Downgrade to a target revision:

```powershell
python scripts/run_migrations.py downgrade <revision>
```

`init_db()` remains available for startup/bootstrap compatibility:

```powershell
python -c "from app.db.init_db import init_db; init_db()"
```

Its meaning is now: upgrade the configured database to `head`.

## 4. Development And Test Usage

Initialize an empty local SQLite database through the formal migration path:

```powershell
python scripts/run_migrations.py upgrade head
```

Run the migration smoke tests:

```powershell
python -m pytest -q tests\db\test_migrations.py tests\domains\workbench\test_workbench_migration.py
```

Use a temporary SQLite path in tests or scripts when needed:

```powershell
python scripts/run_migrations.py upgrade head --db-url sqlite:///C:/temp/tracefold-test.db
```

## 5. Responsibility Split

### Alembic migrations own

- formal schema history
- empty DB initialization to a known revision
- future schema upgrades and downgrades

### `init_db.py` owns

- a narrow compatibility/bootstrap entry for runtime startup
- delegating startup schema bootstrap to the formal migration path

### Domain models own

- the ORM schema source used by the service layer
- metadata for future Alembic autogeneration or manual revision authoring

Domain modules do not own independent migration logic.

## 6. Future Rule

When schema changes are introduced later:

1. update the relevant ORM models
2. add a new Alembic revision under `apps/api/migrations/versions/`
3. run the migration smoke tests
4. update this document if the execution path or operator guidance changes
