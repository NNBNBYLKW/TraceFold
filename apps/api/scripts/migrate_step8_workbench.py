from __future__ import annotations

"""Legacy Step 8 schema entry that now delegates to the formal migration path."""

import sys
from pathlib import Path


API_ROOT = Path(__file__).resolve().parents[1]
if str(API_ROOT) not in sys.path:
    sys.path.insert(0, str(API_ROOT))

from app.db.init_db import init_db


def main() -> None:
    init_db()
    print("Database upgraded to the current schema baseline; Step 8 workbench tables are included.")


if __name__ == "__main__":
    main()
