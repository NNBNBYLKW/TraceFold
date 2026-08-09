import pytest

from media_api.intake.state import (
    InboxAction,
    InboxStatus,
    InvalidTransition,
    allowed_actions,
    retry_target,
    transition_status,
)


def test_ready_item_can_edit_and_ingest() -> None:
    assert allowed_actions(InboxStatus.READY_TO_TAG) == {
        InboxAction.EDIT_METADATA,
        InboxAction.INGEST,
    }


def test_rejects_invalid_transition() -> None:
    with pytest.raises(InvalidTransition):
        transition_status(InboxStatus.DISCOVERED, InboxStatus.COMPLETED)


def test_failed_item_returns_to_recorded_retry_state() -> None:
    assert retry_target(InboxStatus.PROCESSING) is InboxStatus.PROCESSING
