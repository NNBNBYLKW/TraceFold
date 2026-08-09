from __future__ import annotations

from enum import StrEnum


class InboxStatus(StrEnum):
    DISCOVERED = "discovered"
    ANALYZING = "analyzing"
    PROCESSING = "processing"
    READY_TO_TAG = "ready_to_tag"
    CONFLICT = "conflict"
    INGESTING = "ingesting"
    COMPLETED = "completed"
    FAILED = "failed"


class InboxAction(StrEnum):
    PROCESS = "process"
    RETRY = "retry"
    EDIT_METADATA = "edit_metadata"
    INGEST = "ingest"
    RESOLVE_CONFLICT = "resolve_conflict"


class InvalidTransition(ValueError):
    def __init__(self, *, current: InboxStatus, target: InboxStatus) -> None:
        self.current = current
        self.target = target
        super().__init__(f"cannot transition inbox item from {current} to {target}")


class InvalidRetryTarget(ValueError):
    def __init__(self, failed_from_status: InboxStatus) -> None:
        self.failed_from_status = failed_from_status
        super().__init__(f"cannot retry inbox item from {failed_from_status}")


TRANSITIONS: dict[InboxStatus, frozenset[InboxStatus]] = {
    InboxStatus.DISCOVERED: frozenset({InboxStatus.ANALYZING, InboxStatus.FAILED}),
    InboxStatus.ANALYZING: frozenset(
        {
            InboxStatus.PROCESSING,
            InboxStatus.READY_TO_TAG,
            InboxStatus.FAILED,
        }
    ),
    InboxStatus.PROCESSING: frozenset(
        {InboxStatus.READY_TO_TAG, InboxStatus.FAILED}
    ),
    InboxStatus.READY_TO_TAG: frozenset(
        {
            InboxStatus.ANALYZING,
            InboxStatus.INGESTING,
            InboxStatus.CONFLICT,
            InboxStatus.FAILED,
        }
    ),
    InboxStatus.CONFLICT: frozenset(
        {InboxStatus.READY_TO_TAG, InboxStatus.INGESTING}
    ),
    InboxStatus.INGESTING: frozenset(
        {InboxStatus.COMPLETED, InboxStatus.CONFLICT, InboxStatus.FAILED}
    ),
    InboxStatus.COMPLETED: frozenset(),
    InboxStatus.FAILED: frozenset(
        {
            InboxStatus.ANALYZING,
            InboxStatus.PROCESSING,
            InboxStatus.READY_TO_TAG,
        }
    ),
}


_ALLOWED_ACTIONS: dict[InboxStatus, frozenset[InboxAction]] = {
    InboxStatus.DISCOVERED: frozenset({InboxAction.PROCESS}),
    InboxStatus.ANALYZING: frozenset(),
    InboxStatus.PROCESSING: frozenset(),
    InboxStatus.READY_TO_TAG: frozenset(
        {InboxAction.EDIT_METADATA, InboxAction.INGEST}
    ),
    InboxStatus.CONFLICT: frozenset({InboxAction.RESOLVE_CONFLICT}),
    InboxStatus.INGESTING: frozenset(),
    InboxStatus.COMPLETED: frozenset(),
    InboxStatus.FAILED: frozenset({InboxAction.RETRY}),
}


def transition_status(current: InboxStatus, target: InboxStatus) -> InboxStatus:
    if target not in TRANSITIONS[current]:
        raise InvalidTransition(current=current, target=target)
    return target


def retry_target(failed_from_status: InboxStatus) -> InboxStatus:
    if failed_from_status not in {
        InboxStatus.ANALYZING,
        InboxStatus.PROCESSING,
        InboxStatus.INGESTING,
    }:
        raise InvalidRetryTarget(failed_from_status)
    if failed_from_status is InboxStatus.INGESTING:
        return InboxStatus.READY_TO_TAG
    return failed_from_status


def allowed_actions(status: InboxStatus) -> set[InboxAction]:
    return set(_ALLOWED_ACTIONS[status])
