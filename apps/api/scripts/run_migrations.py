from __future__ import annotations

import argparse
import sys
from pathlib import Path


API_ROOT = Path(__file__).resolve().parents[1]
if str(API_ROOT) not in sys.path:
    sys.path.insert(0, str(API_ROOT))

from app.db.migrations import downgrade_database, get_current_revision, upgrade_database


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run TraceFold API schema migrations.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    upgrade_parser = subparsers.add_parser("upgrade", help="Upgrade to a target revision.")
    upgrade_parser.add_argument("revision", nargs="?", default="head")
    upgrade_parser.add_argument("--db-url", dest="db_url")

    downgrade_parser = subparsers.add_parser("downgrade", help="Downgrade to a target revision.")
    downgrade_parser.add_argument("revision")
    downgrade_parser.add_argument("--db-url", dest="db_url")

    current_parser = subparsers.add_parser("current", help="Show the current revision.")
    current_parser.add_argument("--db-url", dest="db_url")

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.command == "upgrade":
        upgrade_database(args.revision, database_url=args.db_url)
        print(f"Upgraded schema to revision {args.revision}.")
        return 0

    if args.command == "downgrade":
        downgrade_database(args.revision, database_url=args.db_url)
        print(f"Downgraded schema to revision {args.revision}.")
        return 0

    current_revision = get_current_revision(database_url=args.db_url)
    print(current_revision or "None")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
