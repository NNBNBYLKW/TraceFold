# TraceFold Windows Offline Test Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and verify `TraceFold-Test-0.1.0-win-x64.zip`, an offline-installable Windows 10/11 x64 test release that uses Python 3.12 x64, stores data beside the extracted package, and opens the UI in an Edge app window.

**Architecture:** FastAPI optionally serves the prebuilt React application from an explicit deployment directory, so the release needs one Python process and one port. Standard-library Python bootstrap scripts validate prerequisites, install a hash-locked wheelhouse with `--no-index`, run migrations, own the API child process, and open Edge with a dedicated profile. A repository build script assembles only allowlisted runtime files in a validated staging root, generates manifests and hashes, and proves the ZIP in a fresh extraction.

**Tech Stack:** Python 3.12, FastAPI, Starlette `StaticFiles`/`FileResponse`, Alembic, PowerShell/CMD launch shims, npm/Vite, pip wheels, ZIP/SHA-256, pytest, Vitest, Playwright.

## Global Constraints

- Target only Windows 10/11 x64 with CPython 3.12 x64.
- The release must install and run without network access; runtime pip commands always use `--no-index` and `--require-hashes`.
- Do not bundle Python, Node.js, FFmpeg, FFprobe, Edge, Playwright browsers, databases, media, previews, logs, caches, `.env` files, Git files, or E2E data.
- Require FFmpeg, FFprobe, and Microsoft Edge on the test computer; missing prerequisites stop with Chinese remediation text and no browser fallback.
- Store `.venv`, `data`, Edge profile, and logs inside the extracted package root.
- Listen only on `127.0.0.1:8100`; never kill a process that the launcher did not create.
- Use a single FastAPI process for API and frontend static hosting; the test computer must not need Node.js.
- Preserve development behavior when no frontend distribution directory is configured.
- Build cleanup may recursively remove only the exact validated `build/windows-test-package` staging root.
- Do not delete, archive, migrate, or switch the entry points of legacy TraceFold applications.
- The test release version and artifact basename are exactly `0.1.0` and `TraceFold-Test-0.1.0-win-x64`.

---

## File Responsibility Map

- `apps/media-api/src/media_api/infrastructure/config.py`: accepts and validates the optional deployed frontend directory.
- `apps/media-api/src/media_api/web_distribution.py`: validates the frontend build, mounts immutable assets, and provides the guarded SPA fallback.
- `apps/media-api/src/media_api/main.py`: installs distribution routes after every API router.
- `apps/media-api/tests/api/test_web_distribution.py`: proves single-origin hosting and route precedence.
- `packaging/windows-test/common.py`: standard-library-only package path, prerequisite, process, logging, and environment helpers shared by installer and launcher.
- `packaging/windows-test/install_test_env.py`: idempotent offline venv installation and Alembic migration.
- `packaging/windows-test/launch_test_app.py`: port check, owned API process, health wait, Edge app lifecycle, and cleanup.
- `packaging/windows-test/安装测试环境.cmd`: locates Python 3.12 and invokes the installer.
- `packaging/windows-test/启动 TraceFold.cmd`: invokes the installed launcher.
- `packaging/windows-test/测试版使用说明.txt`: tester prerequisites, installation, launch, data, logs, upgrade, and checksum instructions.
- `apps/media-api/tests/unit/distribution/test_installer.py`: tests offline install command construction and idempotent paths.
- `apps/media-api/tests/unit/distribution/test_launcher.py`: tests port/health/owned-process behavior without starting Edge.
- `scripts/build_windows_test_package.py`: safe deterministic staging, wheel download, lock/manifest creation, ZIP, checksum, and smoke verification.
- `apps/media-api/tests/unit/distribution/test_package_builder.py`: tests cleanup boundaries, allowlisted copy behavior, wheel lock generation, and data exclusion.
- `docs/media-library-test-release.md`: source-tree build and release acceptance instructions.
- `dist/TraceFold-Test-0.1.0-win-x64.zip`: ignored generated release artifact.
- `dist/TraceFold-Test-0.1.0-win-x64.zip.sha256`: ignored generated artifact checksum.

---

### Task 1: Serve the Built Frontend from FastAPI

**Files:**
- Create: `apps/media-api/src/media_api/web_distribution.py`
- Modify: `apps/media-api/src/media_api/infrastructure/config.py`
- Modify: `apps/media-api/src/media_api/main.py`
- Modify: `apps/media-api/.env.example`
- Create: `apps/media-api/tests/api/test_web_distribution.py`
- Modify: `apps/media-api/tests/unit/test_config.py`

**Interfaces:**
- Consumes: `BootstrapSettings` and the fully configured `FastAPI` instance from `create_app()`.
- Produces: `BootstrapSettings.web_dist_dir: Path | None` and `install_web_distribution(app: FastAPI, web_dist_dir: Path | None) -> None`.

- [ ] **Step 1: Write failing configuration tests**

Add tests that prove the deployment directory is optional and resolves to an absolute path only when supplied:

```python
def test_web_distribution_is_disabled_by_default(tmp_path: Path) -> None:
    settings = BootstrapSettings(
        inbox_dir=tmp_path / "inbox",
        library_dir=tmp_path / "library",
        work_dir=tmp_path / "work",
        database_path=tmp_path / "media.sqlite3",
    )
    assert settings.web_dist_dir is None


def test_web_distribution_path_is_resolved(tmp_path: Path) -> None:
    web = tmp_path / "web"
    settings = BootstrapSettings(
        inbox_dir=tmp_path / "inbox",
        library_dir=tmp_path / "library",
        work_dir=tmp_path / "work",
        database_path=tmp_path / "media.sqlite3",
        web_dist_dir=web,
    )
    assert settings.web_dist_dir == web.resolve()
```

- [ ] **Step 2: Run the configuration tests and confirm RED**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/test_config.py -q
```

Expected: FAIL because `BootstrapSettings` has no `web_dist_dir` field.

- [ ] **Step 3: Add the optional deployment setting**

Add the exact field and resolver to `BootstrapSettings`:

```python
web_dist_dir: Path | None = None

@field_validator("web_dist_dir")
@classmethod
def resolve_web_dist_dir(cls, value: Path | None) -> Path | None:
    return value.resolve() if value is not None else None
```

Add this documented variable to `.env.example` without enabling it:

```dotenv
# TRACEFOLD_MEDIA_WEB_DIST_DIR=C:/TraceFold-Test/app/web
```

- [ ] **Step 4: Run configuration tests and confirm GREEN**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/test_config.py -q
```

Expected: all configuration tests PASS.

- [ ] **Step 5: Write failing API tests for static hosting and route precedence**

Create a fixture that writes `index.html` and `assets/app.js`, then assert:

```python
def test_serves_frontend_and_spa_routes(client_with_web: TestClient) -> None:
    assert client_with_web.get("/").text == "<main>TraceFold Test</main>"
    assert client_with_web.get("/library").text == "<main>TraceFold Test</main>"
    assert client_with_web.get("/assets/app.js").text == "window.TRACEFOLD = true;"


def test_api_and_health_routes_win_over_spa(client_with_web: TestClient) -> None:
    assert client_with_web.get("/health").json()["status"] == "ok"
    response = client_with_web.get("/api/not-a-real-route")
    assert response.status_code == 404
    assert "TraceFold Test" not in response.text


def test_non_get_request_is_never_spa_fallback(client_with_web: TestClient) -> None:
    response = client_with_web.post("/library")
    assert response.status_code == 405
    assert "TraceFold Test" not in response.text


def test_invalid_web_distribution_fails_at_startup(settings: BootstrapSettings) -> None:
    settings.web_dist_dir = settings.work_dir / "missing-web"
    with pytest.raises(ValueError, match="index.html"):
        create_app(settings)
```

- [ ] **Step 6: Run the new API tests and confirm RED**

Run:

```powershell
python -m pytest apps/media-api/tests/api/test_web_distribution.py -q
```

Expected: FAIL because the API does not serve the frontend.

- [ ] **Step 7: Implement guarded distribution routes**

Create `web_distribution.py` with these public and private interfaces:

```python
RESERVED_PREFIXES = frozenset({"api", "health", "docs", "redoc", "openapi.json"})


def install_web_distribution(app: FastAPI, web_dist_dir: Path | None) -> None:
    if web_dist_dir is None:
        return
    root = web_dist_dir.resolve()
    index = root / "index.html"
    assets = root / "assets"
    if not index.is_file():
        raise ValueError(f"web distribution must contain index.html: {root}")
    if not assets.is_dir():
        raise ValueError(f"web distribution must contain assets/: {root}")
    app.mount("/assets", StaticFiles(directory=assets, check_dir=True), name="web-assets")

    @app.get("/", include_in_schema=False)
    def web_index() -> FileResponse:
        return FileResponse(index)

    @app.get("/{requested_path:path}", include_in_schema=False)
    def web_spa(requested_path: str) -> FileResponse:
        first = requested_path.partition("/")[0]
        if first in RESERVED_PREFIXES:
            raise HTTPException(status_code=404, detail="Not Found")
        return FileResponse(index)
```

Call `install_web_distribution(app, resolved.web_dist_dir)` only after all API routers are included in `main.py`. This ordering and the reserved-prefix guard are both required.

- [ ] **Step 8: Run targeted and full backend tests**

Run:

```powershell
python -m pytest apps/media-api/tests/api/test_web_distribution.py apps/media-api/tests/unit/test_config.py -q
python -m pytest apps/media-api/tests -q
```

Expected: targeted tests and the complete backend suite PASS.

- [ ] **Step 9: Commit Task 1**

```powershell
git add apps/media-api/.env.example apps/media-api/src/media_api/infrastructure/config.py apps/media-api/src/media_api/main.py apps/media-api/src/media_api/web_distribution.py apps/media-api/tests/api/test_web_distribution.py apps/media-api/tests/unit/test_config.py
git commit -m "feat(media-api): serve packaged web interface"
```

---

### Task 2: Add the Standard-Library Offline Installer

**Files:**
- Create: `packaging/windows-test/common.py`
- Create: `packaging/windows-test/install_test_env.py`
- Create: `packaging/windows-test/安装测试环境.cmd`
- Create: `apps/media-api/tests/unit/distribution/test_installer.py`

**Interfaces:**
- Consumes: release root containing `app/requirements-win-py312.lock`, `app/alembic.ini`, and `wheelhouse/`.
- Produces: `PackagePaths.from_script(script_file: Path) -> PackagePaths`, `validate_python(version: tuple[int, int, int], pointer_bits: int, machine: str) -> None`, `find_edge(environment: Mapping[str, str], which: Callable[[str], str | None]) -> Path | None`, `offline_install_command(paths: PackagePaths) -> list[str]`, and `install(paths: PackagePaths) -> None`.

- [ ] **Step 1: Write failing tests for package-root resolution and offline pip arguments**

Use `importlib.util.spec_from_file_location` to import the scripts directly, then add:

```python
def test_package_paths_are_rooted_beside_the_extracted_package(tmp_path: Path) -> None:
    script = tmp_path / "TraceFold-Test-0.1.0" / "app" / "install_test_env.py"
    paths = PackagePaths.from_script(script)
    assert paths.root == script.parents[1].resolve()
    assert paths.venv == paths.root / ".venv"
    assert paths.database == paths.root / "data" / "media.sqlite3"


def test_install_command_is_strictly_offline(package_paths: PackagePaths) -> None:
    command = offline_install_command(package_paths)
    assert command[:4] == [str(package_paths.venv_python), "-m", "pip", "install"]
    assert "--no-index" in command
    assert "--require-hashes" in command
    assert command[command.index("--find-links") + 1] == str(package_paths.wheelhouse)
    assert "-r" in command
    assert not any("http://" in part or "https://" in part for part in command)
```

- [ ] **Step 2: Write failing prerequisite validation tests**

Inject version, pointer width, architecture, command lookup, and environment mappings rather than reading global state in tests:

```python
def test_rejects_non_312_or_32_bit_python() -> None:
    with pytest.raises(PrerequisiteError, match="Python 3.12 x64"):
        validate_python((3, 11, 9), pointer_bits=64, machine="AMD64")
    with pytest.raises(PrerequisiteError, match="Python 3.12 x64"):
        validate_python((3, 12, 4), pointer_bits=32, machine="AMD64")


def test_edge_search_uses_known_windows_locations(tmp_path: Path) -> None:
    edge = tmp_path / "Microsoft" / "Edge" / "Application" / "msedge.exe"
    edge.parent.mkdir(parents=True)
    edge.write_bytes(b"")
    result = find_edge({"ProgramFiles": str(tmp_path)}, which=lambda _: None)
    assert result == edge.resolve()
```

- [ ] **Step 3: Run installer tests and confirm RED**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/distribution/test_installer.py -q
```

Expected: FAIL because the packaging installer modules do not exist.

- [ ] **Step 4: Implement focused common helpers**

Implement these dataclasses and functions in `common.py` using only the Python standard library:

```python
@dataclass(frozen=True)
class PackagePaths:
    root: Path
    app: Path
    wheelhouse: Path
    venv: Path
    data: Path
    runtime: Path
    logs: Path

    @property
    def venv_python(self) -> Path:
        return self.venv / "Scripts" / "python.exe"

    @classmethod
    def from_script(cls, script_file: Path) -> "PackagePaths":
        root = script_file.resolve().parents[1]
        return cls(
            root=root,
            app=root / "app",
            wheelhouse=root / "wheelhouse",
            venv=root / ".venv",
            data=root / "data",
            runtime=root / "runtime",
            logs=root / "runtime" / "logs",
        )


class PrerequisiteError(RuntimeError):
    pass


def validate_python(
    version: tuple[int, int, int],
    *,
    pointer_bits: int,
    machine: str,
) -> None:
    if version[:2] != (3, 12) or pointer_bits != 64 or machine.casefold() not in {"amd64", "x86_64"}:
        raise PrerequisiteError("测试版需要 Python 3.12 x64。")


def find_edge(
    environment: Mapping[str, str],
    *,
    which: Callable[[str], str | None] = shutil.which,
) -> Path | None:
    located = which("msedge.exe")
    candidates = [Path(located)] if located else []
    candidates.extend(
        Path(environment[key]) / "Microsoft" / "Edge" / "Application" / "msedge.exe"
        for key in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA")
        if environment.get(key)
    )
    return next((path.resolve() for path in candidates if path.is_file()), None)


def required_command(
    name: str,
    *,
    which: Callable[[str], str | None] = shutil.which,
) -> Path:
    located = which(name)
    if located is None:
        raise PrerequisiteError(f"未找到 {name}，请安装并加入 PATH。")
    return Path(located).resolve()


def runtime_environment(paths: PackagePaths) -> dict[str, str]:
    return {
        **os.environ,
        "TRACEFOLD_MEDIA_INBOX_DIR": str((paths.data / "inbox").resolve()),
        "TRACEFOLD_MEDIA_LIBRARY_DIR": str((paths.data / "library").resolve()),
        "TRACEFOLD_MEDIA_WORK_DIR": str((paths.data / "work").resolve()),
        "TRACEFOLD_MEDIA_DATABASE_PATH": str((paths.data / "media.sqlite3").resolve()),
        "TRACEFOLD_MEDIA_WEB_DIST_DIR": str((paths.app / "web").resolve()),
    }
```

`runtime_environment()` must set absolute `TRACEFOLD_MEDIA_INBOX_DIR`, `LIBRARY_DIR`, `WORK_DIR`, `DATABASE_PATH`, and `WEB_DIST_DIR` values rooted below `paths.root`.

- [ ] **Step 5: Implement idempotent offline installation**

`install_test_env.py` must expose pure command builders plus `install()`:

```python
def offline_install_command(paths: PackagePaths) -> list[str]:
    return [
        str(paths.venv_python), "-m", "pip", "install",
        "--no-index",
        "--find-links", str(paths.wheelhouse),
        "--require-hashes",
        "-r", str(paths.app / "requirements-win-py312.lock"),
    ]


def migration_command(paths: PackagePaths) -> list[str]:
    return [
        str(paths.venv_python), "-m", "alembic",
        "-c", str(paths.app / "alembic.ini"),
        "upgrade", "head",
    ]
```

`install()` validates Python, `ffmpeg`, `ffprobe`, and Edge; creates the venv only if `venv_python` is absent; creates the exact data/runtime directories; runs offline pip with a build-generated lock; then runs Alembic with `runtime_environment(paths)`. It never deletes `.venv` or `data`.

- [ ] **Step 6: Add the UTF-8 CMD shim**

`安装测试环境.cmd` must contain a deterministic Python lookup and preserve the installer exit code:

```bat
@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
where py >nul 2>nul
if %errorlevel%==0 (
  py -3.12 "%ROOT%app\install_test_env.py"
) else (
  python "%ROOT%app\install_test_env.py"
)
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
```

- [ ] **Step 7: Run installer tests and static safety assertions**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/distribution/test_installer.py -q
rg -n -- "--no-index|--require-hashes" packaging/windows-test/install_test_env.py
```

Expected: tests PASS and both mandatory offline flags are present.

- [ ] **Step 8: Commit Task 2**

```powershell
git add packaging/windows-test/common.py packaging/windows-test/install_test_env.py "packaging/windows-test/安装测试环境.cmd" apps/media-api/tests/unit/distribution/test_installer.py
git commit -m "feat(media-dist): add offline test environment installer"
```

---

### Task 3: Add the Owned API and Edge App Launcher

**Files:**
- Modify: `packaging/windows-test/common.py`
- Create: `packaging/windows-test/launch_test_app.py`
- Create: `packaging/windows-test/启动 TraceFold.cmd`
- Create: `apps/media-api/tests/unit/distribution/test_launcher.py`

**Interfaces:**
- Consumes: `PackagePaths`, `find_edge()`, and `runtime_environment()` from Task 2.
- Produces: `OwnedProcess` protocol, `assert_port_available(host: str, port: int) -> None`, `wait_for_health(url: str, process: OwnedProcess, timeout_seconds: float, open_url: Callable = urllib.request.urlopen) -> None`, `stop_owned_process(process: OwnedProcess, timeout_seconds: float = 10) -> None`, and `launch(paths: PackagePaths) -> int`.

- [ ] **Step 1: Write failing port and health tests**

Use a real temporary socket for ownership and a fake process/URL opener for health behavior:

```python
def test_port_check_never_terminates_the_existing_owner() -> None:
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        host, port = listener.getsockname()
        with pytest.raises(LaunchError, match="端口"):
            assert_port_available(host, port)


def test_health_wait_stops_when_owned_server_exits() -> None:
    process = FakeProcess(returncode=3)
    with pytest.raises(LaunchError, match="启动失败"):
        wait_for_health(
            "http://127.0.0.1:8100/health",
            process,
            timeout_seconds=1,
            open_url=lambda *_: (_ for _ in ()).throw(OSError()),
        )
```

- [ ] **Step 2: Write failing owned-process cleanup tests**

The fake process records only calls made to that exact object:

```python
def test_cleanup_terminates_only_the_owned_process() -> None:
    owned = FakeProcess(returncode=None)
    stop_owned_process(owned, timeout_seconds=0)
    assert owned.terminate_calls == 1
    assert owned.kill_calls == 1


def test_cleanup_is_noop_after_owned_process_exits() -> None:
    owned = FakeProcess(returncode=0)
    stop_owned_process(owned)
    assert owned.terminate_calls == 0
    assert owned.kill_calls == 0
```

- [ ] **Step 3: Run launcher tests and confirm RED**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/distribution/test_launcher.py -q
```

Expected: FAIL because launcher interfaces do not exist.

- [ ] **Step 4: Implement the launcher primitives**

Implement:

```python
class OwnedProcess(Protocol):
    def poll(self) -> int | None:
        raise NotImplementedError

    def terminate(self) -> None:
        raise NotImplementedError

    def kill(self) -> None:
        raise NotImplementedError

    def wait(self, timeout: float | None = None) -> int:
        raise NotImplementedError


class LaunchError(RuntimeError):
    pass


def server_command(paths: PackagePaths) -> list[str]:
    return [
        str(paths.venv_python), "-m", "uvicorn",
        "media_api.main:create_app", "--factory",
        "--host", "127.0.0.1", "--port", "8100",
    ]


def edge_command(edge: Path, paths: PackagePaths) -> list[str]:
    return [
        str(edge),
        "--app=http://127.0.0.1:8100",
        f"--user-data-dir={paths.runtime / 'edge-profile'}",
        "--no-first-run",
        "--no-default-browser-check",
    ]
```

`assert_port_available()` binds a new socket and immediately closes it. `wait_for_health()` accepts only HTTP 200 JSON with `status == "ok"`, observes early process exit, and stops after a fixed timeout. `stop_owned_process()` calls `terminate()`, waits, and calls `kill()` only on timeout.

- [ ] **Step 5: Implement end-to-end launch lifecycle**

`launch()` must:

1. Validate `.venv`, required commands, Edge, writable directories, and port 8100.
2. Create a timestamped `runtime/logs/tracefold-YYYYMMDD-HHMMSS.log`.
3. Start uvicorn with `CREATE_NEW_PROCESS_GROUP`, `runtime_environment(paths)`, and stdout/stderr redirected to that file.
4. Wait up to 30 seconds for `/health`.
5. Start exactly one Edge process using the dedicated profile and wait for that `Popen` object.
6. In `finally`, call `stop_owned_process(server)` and confirm port release for up to 10 seconds.
7. Return zero only when Edge and server cleanup succeed.

Never use `taskkill /IM`, `Get-Process python`, `Stop-Process -Name`, or equivalent process-name cleanup.

- [ ] **Step 6: Add the launcher CMD shim**

`启动 TraceFold.cmd` must invoke only the package-local venv:

```bat
@echo off
setlocal
chcp 65001 >nul
set "ROOT=%~dp0"
if not exist "%ROOT%.venv\Scripts\python.exe" (
  echo 尚未安装测试环境，请先运行“安装测试环境.cmd”。
  pause
  exit /b 1
)
"%ROOT%.venv\Scripts\python.exe" "%ROOT%app\launch_test_app.py"
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
```

- [ ] **Step 7: Run launcher tests and scan for unsafe process cleanup**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/distribution/test_launcher.py -q
rg -n "taskkill|Stop-Process|-Name +python|Get-Process +python" packaging/windows-test
```

Expected: tests PASS; `rg` returns no matches.

- [ ] **Step 8: Commit Task 3**

```powershell
git add packaging/windows-test/common.py packaging/windows-test/launch_test_app.py "packaging/windows-test/启动 TraceFold.cmd" apps/media-api/tests/unit/distribution/test_launcher.py
git commit -m "feat(media-dist): launch owned API and Edge window"
```

---

### Task 4: Build the Safe, Hash-Locked Windows ZIP

**Files:**
- Create: `scripts/build_windows_test_package.py`
- Create: `apps/media-api/tests/unit/distribution/test_package_builder.py`
- Modify: `.gitignore`
- Modify: `apps/media-api/pyproject.toml`
- Create: `packaging/windows-test/测试版使用说明.txt`

**Interfaces:**
- Consumes: frontend `dist/`, backend source/migrations, packaging templates from Tasks 2-3, and exact runtime dependencies from `apps/media-api/pyproject.toml`.
- Produces: `BuildLayout.for_repo(repo_root: Path, version: str) -> BuildLayout`, `reset_staging(layout: BuildLayout) -> None`, `assemble(layout: BuildLayout) -> Path`, `write_hashed_lock(wheelhouse: Path, destination: Path) -> None`, `write_manifest(package_root: Path) -> Path`, and `build_release(repo_root: Path, version: str = "0.1.0") -> Path`.

- [ ] **Step 1: Write failing staging safety tests**

```python
def test_reset_staging_rejects_any_root_outside_named_build_directory(tmp_path: Path) -> None:
    unsafe = BuildLayout(
        repo_root=tmp_path,
        staging_root=tmp_path / "dist",
        package_root=tmp_path / "dist" / "TraceFold-Test-0.1.0",
        artifact=tmp_path / "artifact.zip",
    )
    with pytest.raises(ValueError, match="build/windows-test-package"):
        reset_staging(unsafe)


def test_reset_staging_preserves_unrelated_build_files(tmp_path: Path) -> None:
    layout = BuildLayout.for_repo(tmp_path, "0.1.0")
    layout.staging_root.mkdir(parents=True)
    (layout.staging_root / "old.txt").write_text("old")
    unrelated = tmp_path / "build" / "keep.txt"
    unrelated.write_text("keep")
    reset_staging(layout)
    assert unrelated.read_text() == "keep"
```

- [ ] **Step 2: Write failing assembly and exclusion tests**

Create a minimal fake repository and assert the staged package contains required runtime paths but excludes dangerous inputs:

```python
def test_assembly_contains_runtime_only(fake_repo: Path) -> None:
    layout = BuildLayout.for_repo(fake_repo, "0.1.0")
    package = assemble(layout)
    assert (package / "app" / "media_api").is_dir()
    assert (package / "app" / "migrations").is_dir()
    assert (package / "app" / "web" / "index.html").is_file()
    assert (package / "安装测试环境.cmd").is_file()
    assert not (package / "app" / "tests").exists()
    assert not list(package.rglob("*.sqlite3"))
    assert not list(package.rglob("node_modules"))
    assert not list(package.rglob(".env"))
```

- [ ] **Step 3: Write failing wheel-lock and manifest tests**

Generate two tiny valid wheel ZIPs containing `.dist-info/METADATA`, then assert:

```python
def test_hashed_lock_uses_wheel_metadata_and_sha256(tmp_path: Path) -> None:
    wheel = make_wheel(tmp_path, name="sample-package", version="1.2.3")
    lock = tmp_path / "requirements.lock"
    write_hashed_lock(tmp_path, lock)
    digest = hashlib.sha256(wheel.read_bytes()).hexdigest()
    assert lock.read_text(encoding="utf-8") == (
        f"sample-package==1.2.3 --hash=sha256:{digest}\n"
    )


def test_manifest_excludes_mutable_data_contents(tmp_path: Path) -> None:
    package = make_package_tree(tmp_path)
    (package / "data" / "inbox" / "private.jpg").write_bytes(b"private")
    with pytest.raises(ValueError, match="mutable data"):
        write_manifest(package)
```

- [ ] **Step 4: Run package-builder tests and confirm RED**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/distribution/test_package_builder.py -q
```

Expected: FAIL because the builder does not exist.

- [ ] **Step 5: Implement validated layout and allowlisted assembly**

`BuildLayout.for_repo()` must resolve exactly:

```python
staging_root = repo_root.resolve() / "build" / "windows-test-package"
package_root = staging_root / f"TraceFold-Test-{version}"
artifact = repo_root.resolve() / "dist" / f"TraceFold-Test-{version}-win-x64.zip"
```

`reset_staging()` verifies resolved parent/child relationships and the exact final components `build/windows-test-package` before `shutil.rmtree(staging_root)`.

`assemble()` copies only:

- `apps/media-api/src/media_api` to `app/media_api`;
- `apps/media-api/migrations` and `alembic.ini` to `app/`;
- `apps/media-web/dist` to `app/web`;
- `common.py`, installer, and launcher to `app/`;
- both CMD shims and the usage guide to package root;
- `.gitkeep` files into the three data directories and two runtime directories.

Use explicit sources and destinations. Do not copy repository roots, globs, tests, or `.env` files and then filter them later.

- [ ] **Step 6: Implement Windows x64 wheel acquisition and API wheel creation**

Read the exact `[project].dependencies` array with `tomllib`. Run pip download with these fixed target arguments:

```python
download = [
    sys.executable, "-m", "pip", "download",
    "--dest", str(layout.wheelhouse),
    "--only-binary=:all:",
    "--platform", "win_amd64",
    "--python-version", "312",
    "--implementation", "cp",
    "--abi", "cp312",
    *runtime_dependencies,
]
```

Build the pure Python API wheel separately:

```python
wheel = [
    sys.executable, "-m", "pip", "wheel",
    "--no-deps", "--wheel-dir", str(layout.wheelhouse),
    str(layout.repo_root / "apps" / "media-api"),
]
```

Reject `.tar.gz`, `.zip`, or other source distributions in `wheelhouse`. Add `TRACEFOLD_TEST_RELEASE_VERSION = "0.1.0"` as the single builder constant and verify it matches the Python project version.

- [ ] **Step 7: Implement deterministic lock, manifest, ZIP, and checksum**

For each wheel, open its `.dist-info/METADATA`, read canonical `Name` and `Version`, calculate SHA-256, and write one sorted line:

```text
name==version --hash=sha256:<64 lowercase hex characters>
```

Fail on duplicate normalized package names. The lock includes the `tracefold-media-api` wheel. Write it to `app/requirements-win-py312.lock`.

`write_manifest()` rejects any non-`.gitkeep` file under `data/` or `runtime/`, then records every included file as `<sha256>  <forward/slash/path>` in `MANIFEST.sha256`. Create the ZIP from the package-root parent so all contents have exactly one top-level `TraceFold-Test-0.1.0/` directory. Write the archive digest to `TraceFold-Test-0.1.0-win-x64.zip.sha256`.

- [ ] **Step 8: Add usage documentation and ignored build outputs**

The Chinese usage guide must state:

- prerequisites: Windows 10/11 x64, Python 3.12 x64, FFmpeg/FFprobe, Edge;
- verify ZIP checksum before extraction;
- run installation once, then use the launch CMD;
- `data/inbox`, `data/library`, `data/work`, and database purposes;
- logs live under `runtime/logs`;
- upgrades use a new extraction directory and never overwrite `data` automatically;
- no network is used during installation;
- this is a test build, not a production backup target.

Add only these ignore entries:

```gitignore
build/windows-test-package/
dist/TraceFold-Test-*-win-x64.zip
dist/TraceFold-Test-*-win-x64.zip.sha256
```

- [ ] **Step 9: Run builder tests and verify diff safety**

Run:

```powershell
python -m pytest apps/media-api/tests/unit/distribution/test_package_builder.py -q
git diff --check
```

Expected: package-builder tests PASS and no whitespace errors.

- [ ] **Step 10: Commit Task 4**

```powershell
git add .gitignore apps/media-api/pyproject.toml apps/media-api/tests/unit/distribution/test_package_builder.py packaging/windows-test/测试版使用说明.txt scripts/build_windows_test_package.py
git commit -m "build(media): assemble offline Windows test package"
```

---

### Task 5: Prove the Extracted Offline Release and Deliver the ZIP

**Files:**
- Modify: `scripts/build_windows_test_package.py`
- Create: `apps/media-api/tests/integration/distribution/test_release_smoke.py`
- Create: `docs/media-library-test-release.md`
- Generate: `dist/TraceFold-Test-0.1.0-win-x64.zip`
- Generate: `dist/TraceFold-Test-0.1.0-win-x64.zip.sha256`

**Interfaces:**
- Consumes: `build_release()` and artifact layout from Task 4.
- Produces: `ReleaseVerification` and `verify_extracted_release(archive: Path, python312: Path, *, launch_edge: bool = False) -> ReleaseVerification`, release evidence, ZIP, and checksum.

- [ ] **Step 1: Write the failing extracted-release integration test**

Use a test fixture archive with local wheels and inject process execution so the test does not require Edge:

```python
def test_release_smoke_uses_no_index_and_preserves_data(
    release_fixture: Path,
    python312: Path,
) -> None:
    result = verify_extracted_release(
        release_fixture,
        python312,
        launch_edge=False,
    )
    assert result.install_returncode == 0
    assert result.health_payload == {
        "service": "tracefold-media-api",
        "status": "ok",
    }
    assert result.frontend_status == 200
    assert result.api_status == 200
    assert result.port_released is True
    assert result.install_log_contains_no_index is True
```

- [ ] **Step 2: Run the release-smoke test and confirm RED**

Run:

```powershell
python -m pytest apps/media-api/tests/integration/distribution/test_release_smoke.py -q
```

Expected: FAIL because `verify_extracted_release()` is not implemented.

- [ ] **Step 3: Implement release verification with an explicit no-UI mode**

Add `verify_extracted_release()` to the builder. It must:

1. Create a fresh temporary extraction directory outside the repository.
2. Validate the external `.sha256` file before extraction.
3. Reject ZIP members that are absolute or contain `..`.
4. Extract the one expected top-level directory.
5. Invoke the installer using the supplied Python 3.12 path with `PIP_NO_INDEX=1` and a deliberately invalid `PIP_INDEX_URL`.
6. Assert the install log includes `--no-index` and does not contain `http://` or `https://` download lines.
7. Start the package API without Edge, verify `/health`, `/`, and `/api/settings`.
8. Write a sentinel file under `data/inbox`, stop only the owned server, verify port release, restart, and assert the sentinel and database remain.
9. Return an immutable `ReleaseVerification` dataclass with observed values.

The production launcher has no user-facing no-UI switch; no-UI is only a build verifier path that invokes the same `server_command()` and environment helpers.

- [ ] **Step 4: Run the integration smoke test and confirm GREEN**

Run:

```powershell
python -m pytest apps/media-api/tests/integration/distribution/test_release_smoke.py -q
```

Expected: PASS on a machine with Python 3.12 x64.

- [ ] **Step 5: Write the build and tester acceptance guide**

`docs/media-library-test-release.md` must contain exact source-tree commands:

```powershell
python scripts/build_windows_test_package.py --version 0.1.0
python scripts/build_windows_test_package.py --verify-only dist/TraceFold-Test-0.1.0-win-x64.zip
Get-FileHash dist/TraceFold-Test-0.1.0-win-x64.zip -Algorithm SHA256
```

It must also contain the manual acceptance sequence:

1. Disconnect network and extract to a new directory.
2. Run `安装测试环境.cmd` and inspect the log for `--no-index`.
3. Run `启动 TraceFold.cmd`, confirm an Edge app window without tabs/address bar.
4. Ingest an image and an MP4/M4A pair.
5. Exercise skip, keep-both, and replacement conflicts.
6. Close Edge and confirm `127.0.0.1:8100` is released.
7. Restart and confirm data persists.

- [ ] **Step 6: Run the complete pre-release verification matrix**

Run from repository root:

```powershell
python -m pytest apps/media-api/tests -q
npm --prefix apps/media-web test
npm --prefix apps/media-web run build
npm --prefix apps/media-web run e2e
```

Expected: every command PASS; FFmpeg cases are not skipped.

- [ ] **Step 7: Build the real Windows test artifact**

Run:

```powershell
python scripts/build_windows_test_package.py --version 0.1.0
```

Expected outputs:

```text
dist/TraceFold-Test-0.1.0-win-x64.zip
dist/TraceFold-Test-0.1.0-win-x64.zip.sha256
```

The builder must print artifact size, SHA-256, wheel count, manifest file count, and build log path.

- [ ] **Step 8: Verify the real archive in a fresh extraction**

Run:

```powershell
py -3.12 scripts/build_windows_test_package.py --verify-only dist/TraceFold-Test-0.1.0-win-x64.zip
```

Expected: checksum, safe extraction, offline install, migration, health, frontend, API, restart persistence, and port-release checks all PASS.

- [ ] **Step 9: Perform the Edge-window acceptance**

In the fresh extraction, run:

```powershell
& ".\安装测试环境.cmd"
& ".\启动 TraceFold.cmd"
```

Confirm Edge opens in app mode, closing it stops only the package API, and `Test-NetConnection 127.0.0.1 -Port 8100` reports `TcpTestSucceeded : False` after exit.

- [ ] **Step 10: Inspect the archive for forbidden content**

Run:

```powershell
tar -tf dist/TraceFold-Test-0.1.0-win-x64.zip | Select-String -Pattern "node_modules|\.git/|\.env$|\.sqlite3$|test-results|playwright-report|\.e2e-data|data/.+\.(jpg|jpeg|png|mp4|m4a)$"
```

Expected: no matches.

- [ ] **Step 11: Commit Task 5 source changes**

Do not add ignored ZIP artifacts to Git. Commit source, tests, and release documentation only:

```powershell
git add scripts/build_windows_test_package.py apps/media-api/tests/integration/distribution/test_release_smoke.py docs/media-library-test-release.md
git commit -m "test(media): verify offline Windows release"
```

- [ ] **Step 12: Report artifact paths and checksum**

Report clickable absolute paths to the ZIP, `.sha256`, release guide, and build log. State the exact verification counts from Step 6 and the release verification result from Step 8. Do not claim that the artifact is production-ready; label it as an offline test build.

---

## Spec Coverage Matrix

| Approved requirement | Implemented by |
| --- | --- |
| Windows 10/11 x64 + Python 3.12 x64 | Tasks 2, 4, 5 |
| Fully offline wheels, no runtime network | Tasks 2, 4, 5 |
| FFmpeg/FFprobe and Edge are prerequisites | Tasks 2-3 |
| Package-local data, venv, profiles, and logs | Tasks 2-3 |
| Single FastAPI process serves API and frontend | Task 1 |
| Edge app-mode independent window | Task 3 and Task 5 manual acceptance |
| Closing Edge stops only the owned API | Task 3 and Task 5 |
| Safe staging cleanup and no real data in ZIP | Task 4 |
| Hash-locked wheelhouse, manifest, ZIP checksum | Task 4 |
| Fresh-extraction offline install and restart proof | Task 5 |
| Existing unit/build/E2E gate remains green | Task 5 |
| No legacy-app deletion or entry-point switch | Global Constraints |
