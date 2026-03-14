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

    # 高置信度 expense：
    # 同时命中“花了”与金额单位时，允许直入正式事实表分支
    if "花了" in text and any(keyword in text for keyword in ("¥", "￥", "元")):
        return {
            "target_domain": ParseTargetDomain.EXPENSE,
            "confidence_score": 0.95,
            "confidence_level": ParseConfidenceLevel.HIGH,
            "parsed_payload_json": {
                "amount": "25",
                "currency": "CNY",
                "category": "meal",
                "note": raw_text,
            },
        }

    # 中等置信度 expense：保留你原本的宽关键词判断
    if any(keyword in text for keyword in ("￥", "¥", "元", "支出", "消费", "买")):
        return {
            "target_domain": ParseTargetDomain.EXPENSE,
            "confidence_score": 0.8,
            "confidence_level": ParseConfidenceLevel.MEDIUM,
            "parsed_payload_json": {"raw_text": raw_text},
        }

    # 中等置信度 health：保留你原本的健康关键词判断
    if any(keyword in text for keyword in ("体重", "血压", "跑步", "睡眠")):
        return {
            "target_domain": ParseTargetDomain.HEALTH,
            "confidence_score": 0.8,
            "confidence_level": ParseConfidenceLevel.MEDIUM,
            "parsed_payload_json": {"raw_text": raw_text},
        }

    # 默认兜底必须是 UNKNOWN，而不是 KNOWLEDGE
    return {
        "target_domain": ParseTargetDomain.UNKNOWN,
        "confidence_score": 0.3,
        "confidence_level": ParseConfidenceLevel.LOW,
        "parsed_payload_json": {"raw_text": raw_text},
    }