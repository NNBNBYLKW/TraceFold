from __future__ import annotations

from collections.abc import Iterable

from media_api.processing.contracts import Processor


class ProcessorRegistryError(ValueError):
    pass


class DuplicateProcessorName(ProcessorRegistryError):
    pass


class ProcessorNotRegistered(ProcessorRegistryError):
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
