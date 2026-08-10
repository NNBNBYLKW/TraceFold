from __future__ import annotations

from collections.abc import Callable, Iterable

from media_api.processing.contracts import Detection, Processor
from media_api.storage.contracts import FileSnapshot


class ProcessorRegistryError(ValueError):
    pass


class DuplicateProcessorName(ProcessorRegistryError):
    pass


class ProcessorNotRegistered(ProcessorRegistryError):
    pass


class InvalidDetectionInputs(ProcessorRegistryError):
    pass


class ProcessorRegistry:
    def __init__(self, processors: Iterable[Processor] = ()) -> None:
        self._processors: dict[str, Processor] = {}
        for processor in processors:
            self.register(processor)

    def register(self, processor: Processor) -> None:
        name = processor.descriptor.name.strip()
        if not name:
            raise ProcessorRegistryError("processor name must not be empty")
        if name in self._processors:
            raise DuplicateProcessorName(name)
        if processor.descriptor.name != name:
            raise ProcessorRegistryError("processor name must not contain outer whitespace")
        self._processors[name] = processor

    def get(self, name: str) -> Processor | None:
        return self._processors.get(name)

    def require(self, name: str) -> Processor:
        processor = self.get(name)
        if processor is None:
            raise ProcessorNotRegistered(name)
        return processor

    def snapshot(self) -> tuple[Processor, ...]:
        return tuple(self._processors[name] for name in sorted(self._processors))

    def claim_detections(
        self,
        sources: Iterable[FileSnapshot],
        *,
        include: Callable[[Processor], bool] | None = None,
    ) -> tuple[Detection, ...]:
        remaining = {_path_key(source): source for source in sources}
        claimed: list[Detection] = []
        while remaining:
            applicable: list[tuple[int, str, Detection]] = []
            for processor in self.snapshot():
                if include is not None and not include(processor):
                    continue
                detection = processor.detect(tuple(remaining.values()))
                if detection is None:
                    continue
                self._validate_detection(processor.descriptor.name, detection, remaining)
                applicable.append(
                    (
                        processor.descriptor.priority,
                        processor.descriptor.name,
                        detection,
                    )
                )
            if not applicable:
                break
            _, _, selected = min(applicable, key=lambda value: (value[0], value[1]))
            claimed.append(selected)
            for source in selected.inputs:
                remaining.pop(_path_key(source))
        return tuple(claimed)

    def find_upgrade(
        self,
        existing_sources: Iterable[FileSnapshot],
        candidates: Iterable[FileSnapshot],
        *,
        include: Callable[[Processor], bool] | None = None,
    ) -> Detection | None:
        existing = tuple(existing_sources)
        new_candidates = tuple(candidates)
        existing_keys = {_path_key(source) for source in existing}
        candidate_keys = {_path_key(source) for source in new_candidates}
        combined = (*existing, *new_candidates)
        applicable: list[tuple[int, str, Detection]] = []
        for processor in self.snapshot():
            if include is not None and not include(processor):
                continue
            detection = processor.detect(combined)
            if detection is None:
                continue
            self._validate_detection(
                processor.descriptor.name,
                detection,
                {_path_key(source): source for source in combined},
            )
            input_keys = {_path_key(source) for source in detection.inputs}
            if input_keys & existing_keys and input_keys & candidate_keys:
                applicable.append(
                    (
                        processor.descriptor.priority,
                        processor.descriptor.name,
                        detection,
                    )
                )
        if not applicable:
            return None
        return min(applicable, key=lambda value: (value[0], value[1]))[2]

    @staticmethod
    def _validate_detection(
        processor_name: str,
        detection: Detection,
        available: dict[str, FileSnapshot],
    ) -> None:
        if detection.processor_name != processor_name:
            raise InvalidDetectionInputs(
                f"{processor_name} returned a detection for {detection.processor_name}"
            )
        input_keys = {_path_key(source) for source in detection.inputs}
        if not input_keys or not input_keys.issubset(available):
            raise InvalidDetectionInputs(
                f"{processor_name} returned inputs outside the candidate set"
            )


def _path_key(snapshot: FileSnapshot) -> str:
    return str(snapshot.path.resolve()).casefold()
