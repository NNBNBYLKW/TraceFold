from __future__ import annotations

from pathlib import Path

from media_api.processing.processors.mp4_m4a import Mp4M4aMergeProcessor
from media_api.processing.registry import ProcessorRegistry
from media_api.storage.contracts import FileSnapshot


def _snapshot(path: str) -> FileSnapshot:
    return FileSnapshot(path=Path(path).resolve(), size=10, modified_ns=20)


def test_detects_same_directory_same_stem_pair() -> None:
    processor = Mp4M4aMergeProcessor()

    detection = processor.detect(
        [_snapshot("paired.mp4"), _snapshot("paired.m4a")]
    )

    assert detection is not None
    assert detection.processor_name == "mp4_m4a_merge"
    assert detection.primary.path.name == "paired.mp4"
    assert detection.companions[0].path.name == "paired.m4a"
    assert detection.expected_output_name == "paired.mp4"


def test_does_not_pair_different_directories_or_stems(tmp_path: Path) -> None:
    processor = Mp4M4aMergeProcessor()

    assert processor.detect(
        [
            _snapshot(str(tmp_path / "a" / "paired.mp4")),
            _snapshot(str(tmp_path / "b" / "paired.m4a")),
        ]
    ) is None
    assert processor.detect(
        [_snapshot("clip.mp4"), _snapshot("audio.m4a")]
    ) is None


def test_detects_multiple_pairs_one_at_a_time_for_generic_registry_claiming(
    tmp_path: Path,
) -> None:
    processor = Mp4M4aMergeProcessor()
    sources = [
        _snapshot(str(tmp_path / "second.m4a")),
        _snapshot(str(tmp_path / "first.mp4")),
        _snapshot(str(tmp_path / "second.mp4")),
        _snapshot(str(tmp_path / "first.m4a")),
    ]

    first = processor.detect(sources)

    assert first is not None
    assert first.primary.path.name == "first.mp4"

    detections = ProcessorRegistry([processor]).claim_detections(sources)
    assert [detection.primary.path.name for detection in detections] == [
        "first.mp4",
        "second.mp4",
    ]
