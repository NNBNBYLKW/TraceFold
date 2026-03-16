from __future__ import annotations

from app.db.base import Base
from app.db.session import engine


def init_db() -> None:
    """
    Initialize database tables.

    Rules:
    - This function is responsible only for schema creation.
    - It should not seed business data.
    - Domain model modules must be imported before create_all() so that
      SQLAlchemy metadata is fully registered.
    """

    # Import domain models here to ensure they are registered on Base.metadata
    from app.domains.ai_derivations import models as ai_derivations_models  # noqa: F401
    from app.domains.alerts import models as alerts_models  # noqa: F401
    from app.domains.capture import models as capture_models  # noqa: F401
    from app.domains.system_tasks import models as system_tasks_models  # noqa: F401
    from app.domains.pending import models as pending_models  # noqa: F401
    from app.domains.expense import models as expense_models  # noqa: F401
    from app.domains.knowledge import models as knowledge_models  # noqa: F401
    from app.domains.health import models as health_models  # noqa: F401

    Base.metadata.create_all(bind=engine)
