from __future__ import annotations

import logging
from datetime import UTC, datetime
from typing import Any

from sqlalchemy.orm import Session

from app.core.error_codes import ErrorCode
from app.core.exceptions import AppException, RuleEvaluationFailedError
from app.core.logging import build_log_message, get_logger, log_event
from app.domains.alerts import service as alerts_service
from app.domains.alerts.schemas import AlertListRead
from app.domains.health.models import HealthRecord
from app.domains.rules import health_rules


logger = get_logger(__name__)
_INVALIDATION_NOTE = "Rule no longer matches the current formal record."


def evaluate_health_alerts_for_record(
    db: Session,
    *,
    health_record: HealthRecord,
) -> AlertListRead:
    try:
        managed_alerts = [
            alert
            for alert in alerts_service.list_alert_models_for_source(
                db,
                domain=health_rules.RULE_DOMAIN,
                source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
                source_record_id=health_record.id,
            )
            if alert.rule_key in health_rules.MANAGED_RULE_KEYS
        ]
        match = health_rules.evaluate_health_rule_match(
            metric_type=health_record.metric_type,
            value_text=health_record.value_text,
        )

        if match is None:
            for alert in managed_alerts:
                alerts_service.invalidate_alert(
                    db,
                    alert_id=alert.id,
                    resolution_note=_INVALIDATION_NOTE,
                )
            log_event(
                logger,
                level=logging.INFO,
                event="rule_evaluated",
                domain=health_rules.RULE_DOMAIN,
                action="evaluate",
                source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
                source_record_id=health_record.id,
                result="no_match",
            )
            return alerts_service.list_alert_reads_for_source(
                db,
                domain=health_rules.RULE_DOMAIN,
                source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
                source_record_id=health_record.id,
            )

        current_alert = alerts_service.upsert_rule_alert(
            db,
            domain=health_rules.RULE_DOMAIN,
            rule_key=match.rule_key,
            severity=match.severity,
            source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
            source_record_id=health_record.id,
            message=match.message,
            details_json=_build_health_alert_details(health_record=health_record, match=match),
            triggered_at=_utcnow(),
        )
        for alert in managed_alerts:
            if alert.id != current_alert.id:
                alerts_service.invalidate_alert(
                    db,
                    alert_id=alert.id,
                    resolution_note=_INVALIDATION_NOTE,
                )

        log_event(
            logger,
            level=logging.INFO,
            event="rule_evaluated",
            domain=health_rules.RULE_DOMAIN,
            action="evaluate",
            rule_key=match.rule_key,
            source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
            source_record_id=health_record.id,
            result="matched",
        )
        return alerts_service.list_alert_reads_for_source(
            db,
            domain=health_rules.RULE_DOMAIN,
            source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
            source_record_id=health_record.id,
        )
    except AppException:
        raise
    except Exception as exc:
        logger.exception(
            build_log_message(
                "rule_evaluation_failed",
                domain=health_rules.RULE_DOMAIN,
                action="evaluate",
                source_record_type=health_rules.RULE_SOURCE_RECORD_TYPE,
                source_record_id=health_record.id,
                result="failed",
                error_code=ErrorCode.RULE_EVALUATION_FAILED,
            )
        )
        raise RuleEvaluationFailedError(
            message="Health rule rerun failed.",
            details={"target_domain": health_rules.RULE_DOMAIN, "target_id": health_record.id},
        ) from exc


def _build_health_alert_details(
    *,
    health_record: HealthRecord,
    match: health_rules.HealthRuleMatch,
) -> dict[str, Any]:
    details: dict[str, Any] = {
        "title": match.title,
        "explanation": match.explanation,
        "metric_type": health_record.metric_type,
    }
    if health_record.value_text is not None:
        details["value_text"] = health_record.value_text
    return details


def _utcnow() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)
