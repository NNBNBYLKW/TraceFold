from __future__ import annotations

from app.db.base import Base


def import_model_modules() -> None:
    """
    Register all ORM models on the shared SQLAlchemy metadata.

    This remains the single schema registry point for:
    - formal Alembic migrations
    - runtime schema bootstrap through `init_db()`
    - schema-oriented smoke tests
    """

    from app.domains.ai_derivations import models as ai_derivations_models  # noqa: F401
    from app.domains.alerts import models as alerts_models  # noqa: F401
    from app.domains.capture import models as capture_models  # noqa: F401
    from app.domains.system_tasks import models as system_tasks_models  # noqa: F401
    from app.domains.pending import models as pending_models  # noqa: F401
    from app.domains.expense import models as expense_models  # noqa: F401
    from app.domains.knowledge import models as knowledge_models  # noqa: F401
    from app.domains.health import models as health_models  # noqa: F401
    from app.domains.workbench import models as workbench_models  # noqa: F401


def get_target_metadata():
    """
    Return the single SQLAlchemy metadata object used as the schema source.
    """

    import_model_modules()
    return Base.metadata
