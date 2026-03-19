from __future__ import annotations

"""Repo-style schema ensure for Step 8 workbench tables.

This script does not introduce a standalone migration framework. It only ensures
the current SQLAlchemy metadata is registered and created through the existing
repository bootstrap path.
"""

from app.db.init_db import init_db


def main() -> None:
    init_db()
    print("Step 8 workbench schema ensured via repo-style bootstrap (not a standalone migration system).")


if __name__ == "__main__":
    main()
