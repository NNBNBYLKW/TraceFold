from datetime import UTC

from media_api.infrastructure.db import Base, create_engine_and_session
from media_api.infrastructure.config import BootstrapSettings
from media_api.intake.models import InboxItem
from media_api.intake.state import InboxStatus

# Ensure cross-module relationships are registered before mapper configuration.
import media_api.catalog.models  # noqa: F401, E402
import media_api.processing.models  # noqa: F401, E402
import media_api.settings.models  # noqa: F401, E402


def test_sqlite_round_trip_restores_timezone_aware_utc_timestamps(
    settings: BootstrapSettings,
) -> None:
    session_factory = create_engine_and_session(settings)
    engine = session_factory.kw["bind"]
    Base.metadata.create_all(engine)

    with session_factory() as session:
        item = InboxItem(
            media_type="video",
            status=InboxStatus.DISCOVERED,
            suggested_actual_filename="clip.mp4",
        )
        session.add(item)
        session.commit()
        item_id = item.id

    with session_factory() as session:
        restored = session.get(InboxItem, item_id)
        assert restored is not None
        assert restored.created_at.tzinfo is UTC
        assert restored.updated_at.tzinfo is UTC
        assert restored.discovered_at.tzinfo is UTC

    Base.metadata.drop_all(engine)
    engine.dispose()
