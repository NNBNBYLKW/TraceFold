from __future__ import annotations

from app.db.session import SessionLocal

# 显式导入模型模块，确保所有外键目标表都注册进 metadata
from app.domains.capture.models import CaptureRecord
from app.domains.pending.models import PendingItem  # noqa: F401
from app.domains.health.models import HealthRecord
from app.domains.knowledge.models import KnowledgeEntry


def create_capture(db, *, source_ref: str, raw_text: str) -> CaptureRecord:
    capture = CaptureRecord(
        source_type="manual_seed",
        source_ref=source_ref,
        raw_text=raw_text,
        status="committed",
    )
    db.add(capture)
    db.flush()
    return capture


def main() -> None:
    db = SessionLocal()
    try:
        # -------------------------
        # Health: 硬指标测试数据
        # -------------------------
        health_rows = [
            ("hr-99", "heart_rate", "99", "heart rate below threshold"),
            ("hr-100", "heart_rate", "100", "heart rate info boundary"),
            ("hr-110", "heart_rate", "110", "heart rate warning boundary"),
            ("hr-130", "heart_rate", "130", "heart rate high boundary"),
            ("sleep-420", "sleep_duration", "420", "sleep no alert boundary"),
            ("sleep-360", "sleep_duration", "360", "sleep info boundary"),
            ("sleep-300", "sleep_duration", "300", "sleep warning boundary"),
            ("sleep-299", "sleep_duration", "299", "sleep high boundary"),
            ("bp-129-79", "blood_pressure", "129/79", "bp no alert boundary"),
            ("bp-130-79", "blood_pressure", "130/79", "bp info boundary"),
            ("bp-140-89", "blood_pressure", "140/89", "bp warning boundary"),
            ("bp-180-120", "blood_pressure", "180/120", "bp high boundary"),
            # 非法格式
            ("hr-invalid", "heart_rate", "fast today", "invalid heart rate text"),
            ("sleep-invalid", "sleep_duration", "around six hours", "invalid sleep text"),
            ("bp-invalid", "blood_pressure", "120 over 80", "invalid blood pressure text"),
            # 主观健康记录（给 Health AI 用）
            (
                "subjective-1",
                "general_note",
                None,
                "I felt dizzy after waking up and had a mild headache in the afternoon.",
            ),
            (
                "subjective-2",
                "symptom_note",
                None,
                "Felt unusually tired today, with low appetite and some stomach discomfort.",
            ),
        ]

        for source_ref, metric_type, value_text, note in health_rows:
            capture = create_capture(
                db,
                source_ref=source_ref,
                raw_text=f"{metric_type}: {value_text or note}",
            )
            record = HealthRecord(
                source_capture_id=capture.id,
                source_pending_id=None,
                metric_type=metric_type,
                value_text=value_text,
                note=note,
            )
            db.add(record)

        # -------------------------
        # Knowledge: 正式知识测试数据
        # -------------------------
        knowledge_rows = [
            {
                "source_ref": "knowledge-1",
                "title": "SQLite notes",
                "content": "SQLite is a lightweight relational database. It is file-based and useful for local-first applications.",
                "source_text": "Notes collected from local-first architecture reading.",
            },
            {
                "source_ref": "knowledge-2",
                "title": "Health tracking reflection",
                "content": "Single-record threshold alerts should remain objective, while AI summaries should stay as supportive interpretation.",
                "source_text": "Personal design note about TraceFold health module.",
            },
        ]

        for row in knowledge_rows:
            capture = create_capture(
                db,
                source_ref=row["source_ref"],
                raw_text=row["content"],
            )
            entry = KnowledgeEntry(
                source_capture_id=capture.id,
                source_pending_id=None,
                title=row["title"],
                content=row["content"],
                source_text=row["source_text"],
            )
            db.add(entry)

        db.commit()
        print("Step 6 seed data inserted successfully.")
    except Exception:
        db.rollback()
        raise
    finally:
        db.close()


if __name__ == "__main__":
    main()