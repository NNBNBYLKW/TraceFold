from __future__ import annotations

from hashlib import sha256 as new_sha256
from pathlib import Path


def sha256(path: Path, *, chunk_size: int = 1024 * 1024) -> str:
    digest = new_sha256()
    with path.open("rb") as source:
        while chunk := source.read(chunk_size):
            digest.update(chunk)
    return digest.hexdigest()
