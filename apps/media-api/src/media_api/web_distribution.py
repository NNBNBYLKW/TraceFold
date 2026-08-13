from __future__ import annotations

from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles


RESERVED_PREFIXES = frozenset(
    {"api", "health", "docs", "redoc", "openapi.json"}
)


def install_web_distribution(
    app: FastAPI,
    web_dist_dir: Path | None,
) -> None:
    if web_dist_dir is None:
        return

    root = web_dist_dir.resolve()
    index = root / "index.html"
    assets = root / "assets"
    if not index.is_file():
        raise ValueError(f"web distribution must contain index.html: {root}")
    if not assets.is_dir():
        raise ValueError(f"web distribution must contain assets/: {root}")

    app.mount(
        "/assets",
        StaticFiles(directory=assets, check_dir=True),
        name="web-assets",
    )

    @app.get("/", include_in_schema=False)
    def web_index() -> FileResponse:
        return FileResponse(index)

    @app.get("/{requested_path:path}", include_in_schema=False)
    def web_spa(requested_path: str) -> FileResponse:
        first = requested_path.partition("/")[0]
        if first in RESERVED_PREFIXES:
            raise HTTPException(status_code=404, detail="Not Found")
        return FileResponse(index)
