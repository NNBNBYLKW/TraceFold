from pathlib import Path
from runpy import run_path

import pytest


_runner = run_path(
    str(Path(__file__).resolve().parents[2] / "scripts" / "run_e2e_api.py")
)
E2EPaths = _runner["E2EPaths"]
resolve_e2e_paths = _runner["resolve_e2e_paths"]
reset_e2e_data = _runner["reset_e2e_data"]


def test_resolves_only_the_dedicated_media_web_e2e_root(tmp_path: Path) -> None:
    web_dir = tmp_path / "workspace" / "apps" / "media-web"

    paths = resolve_e2e_paths(web_dir)

    assert paths == E2EPaths(
        root=(web_dir / ".e2e-data").resolve(),
        inbox=(web_dir / ".e2e-data" / "inbox").resolve(),
        library=(web_dir / ".e2e-data" / "library").resolve(),
        work=(web_dir / ".e2e-data" / "work").resolve(),
        database=(web_dir / ".e2e-data" / "media.sqlite3").resolve(),
    )


def test_rejects_cleanup_outside_the_named_e2e_root(tmp_path: Path) -> None:
    unsafe = E2EPaths(
        root=tmp_path.resolve(),
        inbox=(tmp_path / "inbox").resolve(),
        library=(tmp_path / "library").resolve(),
        work=(tmp_path / "work").resolve(),
        database=(tmp_path / "media.sqlite3").resolve(),
    )

    with pytest.raises(ValueError, match="apps/media-web/.e2e-data"):
        reset_e2e_data(unsafe)


def test_removes_only_runtime_children_and_preserves_unrelated_files(
    tmp_path: Path,
) -> None:
    paths = resolve_e2e_paths(tmp_path / "apps" / "media-web")
    paths.inbox.mkdir(parents=True)
    paths.library.mkdir()
    paths.work.mkdir()
    paths.database.write_bytes(b"sqlite")
    sentinel = paths.root / "keep-me.txt"
    sentinel.write_text("preserve", encoding="utf-8")

    reset_e2e_data(paths)

    assert not paths.inbox.exists()
    assert not paths.library.exists()
    assert not paths.work.exists()
    assert not paths.database.exists()
    assert sentinel.read_text(encoding="utf-8") == "preserve"
