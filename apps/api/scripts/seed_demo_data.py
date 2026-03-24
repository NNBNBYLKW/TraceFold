from __future__ import annotations

import argparse
import sys
from pathlib import Path


API_ROOT = Path(__file__).resolve().parents[1]
if str(API_ROOT) not in sys.path:
    sys.path.insert(0, str(API_ROOT))

from app.db.migrations import get_current_revision, get_head_revision
from app.db.session import build_session_local
from app.seed.demo_data import DemoSeedOptions, seed_demo_data


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Seed deterministic TraceFold demo data into a fresh migrated database.",
    )
    parser.add_argument("--db-url", dest="db_url")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--random-seed", type=int, default=20260323)
    parser.add_argument("--expenses", type=_non_negative_int, default=60)
    parser.add_argument("--knowledge", dest="knowledge_entries", type=_non_negative_int, default=30)
    parser.add_argument("--health", dest="health_records", type=_non_negative_int, default=45)
    parser.add_argument(
        "--with-alerts",
        action="store_true",
        help="Run an explicit post-seed health rule rerun pass after formal facts are created.",
    )
    parser.add_argument(
        "--with-derivations",
        action="store_true",
        help="Run explicit knowledge_summary recompute tasks after the formal seed write path completes.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    current_revision = get_current_revision(database_url=args.db_url)
    head_revision = get_head_revision(database_url=args.db_url)
    if current_revision != head_revision:
        print(
            "Seed data refused because the database is not at migration head. "
            f"Current revision: {current_revision or 'None'}, head revision: {head_revision or 'None'}. "
            "Run migrations first.",
            file=sys.stderr,
        )
        return 1

    session_local = build_session_local(database_url=args.db_url)
    engine = session_local.kw.get("bind")
    db = session_local()
    try:
        result = seed_demo_data(
            db,
            options=DemoSeedOptions(
                expenses=args.expenses,
                knowledge_entries=args.knowledge_entries,
                health_records=args.health_records,
                random_seed=args.random_seed,
                force=args.force,
                with_alerts=args.with_alerts,
                with_derivations=args.with_derivations,
            ),
        )
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        return 1
    finally:
        db.close()
        if engine is not None:
            engine.dispose()

    print("TraceFold demo seed completed.")
    print(f"Expenses: {result.expense_count}")
    print(f"Knowledge entries: {result.knowledge_count}")
    print(f"Health records: {result.health_count}")
    print(f"Health alerts: {result.health_alert_count}")
    print(f"Knowledge derivations: {result.knowledge_derivation_count}")
    print(f"Task runs: {result.task_run_count}")
    print(f"Dashboard refresh task: {result.dashboard_task_id}")
    if result.derivation_task_ids:
        print("Knowledge recompute tasks: " + ", ".join(str(task_id) for task_id in result.derivation_task_ids))
    return 0


def _non_negative_int(value: str) -> int:
    parsed = int(value)
    if parsed < 0:
        raise argparse.ArgumentTypeError("value must be greater than or equal to 0")
    return parsed


if __name__ == "__main__":
    raise SystemExit(main())
