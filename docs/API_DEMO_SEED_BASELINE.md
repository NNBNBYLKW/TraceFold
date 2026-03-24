# API Demo Seed Baseline

## Purpose

This document defines the formal demo seed baseline for a fresh TraceFold API SQLite database.

It is for:

- local development
- API integration checks
- page and contract smoke
- task / alert / derivation baseline verification

It is not:

- a production import path
- a long-running automation platform
- a CSV / Excel ingestion system
- a background worker or scheduler

## Entry Point

Run migrations first, then run the seed script:

```bash
python apps/api/scripts/run_migrations.py upgrade head --db-url sqlite:///tracefold_demo.db
python apps/api/scripts/seed_demo_data.py --db-url sqlite:///tracefold_demo.db
```

Keep the database target identical across migration, seed, and later API startup. If you use a relative SQLite URL, reuse the exact same `--db-url` string each time.

Recommended optional flags:

```bash
python apps/api/scripts/seed_demo_data.py \
  --db-url sqlite:///tracefold_demo.db \
  --force \
  --random-seed 20260323 \
  --with-alerts \
  --with-derivations
```

Supported parameters:

- `--db-url`: target SQLite database
- `--force`: clear prior demo-seed formal data, then reseed
- `--random-seed`: deterministic seed for reproducible demo data
- `--expenses`: number of seeded expense records, default `60`
- `--knowledge`: number of seeded knowledge entries, default `30`
- `--health`: number of seeded health records, default `45`
- `--with-alerts`: run an explicit post-seed health rule rerun pass on top of the normal formal write path
- `--with-derivations`: run explicit `knowledge_summary` recompute tasks after seeding, on top of the normal formal write path

## Safety Rules

The seed script is intentionally conservative.

- It expects the database to already be migrated to `head`
- It refuses to run over non-demo formal data
- It refuses to run twice on prior demo formal data unless `--force` is supplied
- `--force` is only intended for re-seeding a prior demo database; it is not a general-purpose reset command for real user data

The seed marker used for formal lineage is:

- `capture_records.source_type = "seed_demo"`
- `capture_records.source_ref` prefixed with `tracefold_demo_seed_v1`

## What Gets Seeded

Default counts:

- expenses: `60`
- knowledge entries: `30`
- health records: `45`

### Expense

The seed creates a realistic spread across categories:

- groceries
- rent
- transport
- utilities
- entertainment
- eating_out
- shopping
- subscription

The dataset spans roughly the last 175 days and is deterministic enough for:

- list/detail reads
- category filters
- keyword filters
- sort and pagination smoke

### Knowledge

The seed creates notes across:

- learning notes
- project ideas
- reading excerpts
- technical summaries
- scratch notes

Knowledge entries are written through the formal knowledge service, so the baseline `knowledge_summary` derivation is generated through the formal derivation path instead of being inserted as static table data.

### Health

The seed creates objective health records across:

- `heart_rate`
- `sleep_duration`
- `blood_pressure`

Some records are intentionally normal, and some intentionally cross rule thresholds so that formal health alerts are created through the rule evaluation service.

## Derived Layers and Task Runtime

The seed respects the existing layer boundaries:

- formal facts are written first
- health alerts are created through the formal rules path
- `knowledge_summary` derivations are created through the formal derivation path
- the seed also runs a formal `dashboard_summary_refresh` task so task runtime is not empty

If `--with-derivations` is enabled, the seed additionally requests and executes formal `knowledge_summary_recompute` tasks for a small subset of seeded knowledge entries.

This keeps the seed useful for:

- task runtime smoke
- derivation recompute smoke
- runtime status checks

## Verification Checklist

After migrations and seed:

1. Expense list

```bash
curl "http://127.0.0.1:8000/api/expense?page=1&page_size=20"
```

2. Knowledge detail

```bash
curl "http://127.0.0.1:8000/api/knowledge/1"
```

3. Health alerts

```bash
curl "http://127.0.0.1:8000/api/alerts?domain=health&status=open"
```

4. Knowledge summary derivation

```bash
curl "http://127.0.0.1:8000/api/ai-derivations/knowledge/1"
```

5. Dashboard summary

```bash
curl "http://127.0.0.1:8000/api/dashboard"
```

6. Runtime status

```bash
curl "http://127.0.0.1:8000/api/system/status"
```

Expected outcomes:

- expense, knowledge, and health lists are non-empty
- at least some open health alerts are present
- `knowledge_summary` derivations exist and can be read
- dashboard summary is no longer empty
- runtime status remains readable after the seed run

## Boundary Notes

- This seed baseline does not bypass service boundaries for alerts or AI derivations
- It does not introduce a worker, scheduler, or separate runtime
- It does not mutate formal facts through AI or alerts
- It is intentionally scoped to fresh or prior-demo databases only
