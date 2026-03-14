from __future__ import annotations

from typing import Any

from app.domains.capture.models import ParseConfidenceLevel, ParseTargetDomain


def parse_raw_text(raw_text: str | None) -> dict[str, Any]:
    text = (raw_text or "").strip().lower()

    if not text:
        return {
            "target_domain": ParseTargetDomain.UNKNOWN,
            "confidence_score": 0.1,
            "confidence_level": ParseConfidenceLevel.LOW,
            "parsed_payload_json": None,
        }

    if any(keyword in text for keyword in ("￥", "元", "支出", "消费", "买")):
        return {
            "target_domain": ParseTargetDomain.EXPENSE,
            "confidence_score": 0.8,
            "confidence_level": ParseConfidenceLevel.MEDIUM,
            "parsed_payload_json": {"raw_text": raw_text},
        }

    if any(keyword in text for keyword in ("体重", "血压", "跑步", "睡眠")):
        return {
            "target_domain": ParseTargetDomain.HEALTH,
            "confidence_score": 0.8,
            "confidence_level": ParseConfidenceLevel.MEDIUM,
            "parsed_payload_json": {"raw_text": raw_text},
        }

    return {
    "target_domain": ParseTargetDomain.UNKNOWN,
    "confidence_score": 0.3,
    "confidence_level": ParseConfidenceLevel.LOW,
    "parsed_payload_json": {"raw_text": raw_text},
}
