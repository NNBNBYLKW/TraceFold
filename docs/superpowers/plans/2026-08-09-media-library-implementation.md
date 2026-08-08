# TraceFold Media Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build an isolated local media-library application that discovers downloaded images and videos, runs extensible preprocessing, captures manual metadata, and moves verified media into a searchable formal library without risking source-file loss.

**Architecture:** Add a new FastAPI/SQLite application under apps/media-api and a new React/Vite application under apps/media-web. The API owns the state machine, database transactions, processing worker, recovery journal, and every filesystem mutation; the web application renders server-provided state and submits explicit commands. Deliver the system as vertical slices so every task ends in independently testable behavior.

**Tech Stack:** Python 3.12+, FastAPI 0.115.12, Pydantic 2.12.5, pydantic-settings 2.8.1, SQLAlchemy 2.0.48, Alembic 1.18.4, Pillow 12.1.1, pytest 9.0.2, pytest-asyncio 1.4.0, React 19.2.8, TypeScript 5.9.3, Vite 8.2.1, TanStack Query 5.101.4, React Router 7.18.2, Vitest 4.1.10, Testing Library 16.3.2, MSW 2.15.0, Playwright 1.62.1, SQLite, FFmpeg/ffprobe.

## Global Constraints

- The approved design is docs/superpowers/specs/2026-08-01-media-library-refactor-design.md.
- New code lives only in apps/media-api and apps/media-web, except repository documentation and narrowly scoped ignore rules.
- New applications must not import, query, migrate, or mutate apps/api, apps/web, their databases, or their domain models.
- Start with a new empty SQLite database; do not migrate old business data.
- INBOX_DIR, LIBRARY_DIR, and WORK_DIR must be distinct and must not contain one another. DATABASE_PATH and WORK_DIR must not be inside INBOX_DIR.
- Physical media files are stored directly in the LIBRARY_DIR root. Metadata provides organization; author or series folders are out of scope.
- actual_filename is immutable through metadata APIs. display_name changes database presentation only and never renames a file.
- MediaAsset has zero-to-many authors, zero-or-one series, and zero-to-many tags.
- Only the storage module may mutate the filesystem. Processors may read declared inputs and write only inside WORK_DIR.
- Source files may be deleted only after the formal file and SQLite transaction have committed and been verified.
- MP4/M4A preprocessing must be implemented through the processor protocol and registry, not extension conditionals in the scanner.
- Background work is an in-process persistent worker. Do not add Redis, Celery, WebSocket infrastructure, cloud services, AI tagging, downloaders, authentication, or multi-user features.
- Every command endpoint that can create work uses an idempotency key. Repeating a request must return the existing operation or job.
- Tests use temporary directories and a temporary SQLite database. Never point automated tests at a user's real inbox or library.
- Before Task 1 execution, create an isolated worktree with superpowers:using-git-worktrees.

---

## Locked File Structure

    apps/media-api/
      pyproject.toml
      alembic.ini
      .env.example
      migrations/
        env.py
        script.py.mako
        versions/20260809_0001_media_library.py
      src/media_api/
        __init__.py
        main.py
        api/
          dependencies.py
          errors.py
          events.py
        infrastructure/
          config.py
          db.py
          lifecycle.py
        intake/
          models.py
          schemas.py
          state.py
          repository.py
          scanner.py
          service.py
          router.py
        storage/
          contracts.py
          local.py
          hashing.py
          preview.py
        taxonomy/
          models.py
          schemas.py
          repository.py
          service.py
          router.py
        processing/
          models.py
          contracts.py
          registry.py
          planner.py
          worker.py
          router.py
          processors/mp4_m4a.py
        catalog/
          models.py
          schemas.py
          repository.py
          conflicts.py
          ingest.py
          recovery.py
          service.py
          router.py
        settings/
          models.py
          schemas.py
          service.py
          router.py
      tests/
        conftest.py
        unit/
        integration/
        api/
        fixtures/media/
      scripts/
        run_e2e_api.py

    apps/media-web/
      package.json
      package-lock.json
      tsconfig.json
      vite.config.ts
      vitest.config.ts
      playwright.config.ts
      .env.example
      index.html
      src/
        main.tsx
        app/App.tsx
        app/router.tsx
        app/queryClient.ts
        shared/api/client.ts
        shared/api/types.ts
        shared/events/useMediaEvents.ts
        shared/components/MediaPreview.tsx
        shared/components/TaxonomyPicker.tsx
        inbox/InboxPage.tsx
        inbox/MediaGrid.tsx
        inbox/MediaCard.tsx
        inbox/MetadataDrawer.tsx
        inbox/BulkMetadataBar.tsx
        inbox/queries.ts
        conflicts/ConflictDialog.tsx
        library/LibraryPage.tsx
        library/LibraryFilters.tsx
        library/AssetDrawer.tsx
        settings/SettingsPage.tsx
        styles/tokens.css
        styles/app.css
      tests/
        setup.ts
        server.ts
        inbox/
        conflicts/
        library/
        settings/
      e2e/media-library.spec.ts

## Shared Interface Ledger

The following names are stable across tasks:

    class BootstrapSettings(BaseSettings):
        inbox_dir: Path
        library_dir: Path
        work_dir: Path
        database_path: Path
        scan_interval_seconds: int

    class InboxStatus(StrEnum):
        DISCOVERED = "discovered"
        ANALYZING = "analyzing"
        PROCESSING = "processing"
        READY_TO_TAG = "ready_to_tag"
        CONFLICT = "conflict"
        INGESTING = "ingesting"
        COMPLETED = "completed"
        FAILED = "failed"

    class InboxAction(StrEnum):
        PROCESS = "process"
        RETRY = "retry"
        EDIT_METADATA = "edit_metadata"
        INGEST = "ingest"
        RESOLVE_CONFLICT = "resolve_conflict"

    @dataclass(frozen=True)
    class FileSnapshot:
        path: Path
        size: int
        modified_ns: int

    @dataclass(frozen=True)
    class RuntimePaths:
        inbox_dir: Path
        library_dir: Path
        work_dir: Path

    @dataclass(frozen=True)
    class ScanResult:
        observed_candidates: int
        stable_candidates: int
        created_items: int

    @dataclass(frozen=True)
    class ProcessorDescriptor:
        name: str
        version: str
        priority: int
        depends_on: tuple[str, ...]
        auto_run_default: bool

    @dataclass(frozen=True)
    class Detection:
        processor_name: str
        primary: FileSnapshot
        companions: tuple[FileSnapshot, ...]

    @dataclass(frozen=True)
    class IngestResult:
        kind: Literal["completed", "conflict"]
        operation_id: UUID
        asset_id: UUID | None
        conflict_id: UUID | None
        final_path: Path | None
        cleanup_warning: str | None

    class Processor(Protocol):
        descriptor: ProcessorDescriptor
        def detect(self, sources: Sequence[FileSnapshot]) -> Detection | None: ...
        def execute(self, context: ProcessingContext) -> ProducedFile: ...
        def validate(self, output: ProducedFile) -> None: ...

    class IngestService:
        def ingest(self, item_id: UUID, idempotency_key: str) -> IngestResult: ...

    class ConflictResolution(StrEnum):
        SKIP = "skip"
        KEEP_BOTH = "keep_both"
        REPLACE = "replace"

API JSON uses snake_case field names. TypeScript types mirror the same names exactly; the client must not rename state or action values.

---

### Task 1: Scaffold the Isolated Media API and Validate Paths

**Files:**
- Create: apps/media-api/pyproject.toml
- Create: apps/media-api/.env.example
- Create: apps/media-api/src/media_api/__init__.py
- Create: apps/media-api/src/media_api/main.py
- Create: apps/media-api/src/media_api/infrastructure/config.py
- Create: apps/media-api/src/media_api/infrastructure/db.py
- Create: apps/media-api/tests/conftest.py
- Create: apps/media-api/tests/unit/test_config.py
- Create: apps/media-api/tests/api/test_health.py
- Modify: .gitignore

**Interfaces:**
- Consumes: none.
- Produces: BootstrapSettings, create_engine_and_session(settings), create_app(settings=None), and GET /health.

- [ ] **Step 1: Add the package manifest and test bootstrap**

    [build-system]
    requires = ["setuptools>=75"]
    build-backend = "setuptools.build_meta"

    [project]
    name = "tracefold-media-api"
    version = "0.1.0"
    requires-python = ">=3.12"
    dependencies = [
      "fastapi==0.115.12",
      "pydantic==2.12.5",
      "pydantic-settings==2.8.1",
      "sqlalchemy==2.0.48",
      "alembic==1.18.4",
      "uvicorn[standard]==0.34.0"
    ]

    [project.optional-dependencies]
    dev = [
      "httpx==0.28.1",
      "pytest==9.0.2",
      "pytest-asyncio==1.4.0"
    ]

    [tool.pytest.ini_options]
    testpaths = ["tests"]
    pythonpath = ["src"]
    asyncio_mode = "auto"

Add .superpowers/ and apps/media-web/.e2e-data/ to .gitignore. Do not modify an existing ignore rule.

- [ ] **Step 2: Write failing configuration tests**

    def test_rejects_nested_runtime_directories(tmp_path):
        with pytest.raises(ValueError, match="must not overlap"):
            BootstrapSettings(
                inbox_dir=tmp_path / "inbox",
                library_dir=tmp_path / "inbox" / "library",
                work_dir=tmp_path / "work",
                database_path=tmp_path / "media.sqlite3",
            )

    def test_rejects_database_inside_inbox(tmp_path):
        with pytest.raises(ValueError, match="database_path"):
            BootstrapSettings(
                inbox_dir=tmp_path / "inbox",
                library_dir=tmp_path / "library",
                work_dir=tmp_path / "work",
                database_path=tmp_path / "inbox" / "media.sqlite3",
            )

- [ ] **Step 3: Install the isolated package and verify the tests fail**

Run:

    python -m pip install -e "./apps/media-api[dev]"
    python -m pytest apps/media-api/tests/unit/test_config.py -q

Expected: FAIL because media_api.infrastructure.config does not exist.

- [ ] **Step 4: Implement validated bootstrap settings**

    class BootstrapSettings(BaseSettings):
        model_config = SettingsConfigDict(env_prefix="TRACEFOLD_MEDIA_", env_file=".env")
        inbox_dir: Path = Path("data/media/inbox")
        library_dir: Path = Path("data/media/library")
        work_dir: Path = Path("data/media/work")
        database_path: Path = Path("data/media/media.sqlite3")
        scan_interval_seconds: int = Field(default=10, ge=2, le=3600)

        @model_validator(mode="after")
        def validate_paths(self):
            inbox = self.inbox_dir.resolve()
            library = self.library_dir.resolve()
            work = self.work_dir.resolve()
            for left, right in combinations((inbox, library, work), 2):
                if left == right or left in right.parents or right in left.parents:
                    raise ValueError("runtime directories must not overlap")
            database = self.database_path.resolve()
            if database == inbox or inbox in database.parents:
                raise ValueError("database_path must not be inside inbox_dir")
            return self

create_engine_and_session must enable SQLite foreign keys on every connection and return a sessionmaker with expire_on_commit=False.

- [ ] **Step 5: Add the app factory and health test**

    def test_health_uses_media_api_only(client):
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"service": "tracefold-media-api", "status": "ok"}

    def create_app(settings: BootstrapSettings | None = None) -> FastAPI:
        resolved = settings or BootstrapSettings()
        app = FastAPI(title="TraceFold Media API")
        app.state.settings = resolved

        @app.get("/health")
        def health():
            return {"service": "tracefold-media-api", "status": "ok"}

        return app

- [ ] **Step 6: Run the foundation tests**

Run:

    python -m pytest apps/media-api/tests/unit/test_config.py apps/media-api/tests/api/test_health.py -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add .gitignore apps/media-api
    git commit -m "feat(media-api): scaffold isolated service"

---

### Task 2: Add the Media Schema, State Machine, and Initial Migration

**Files:**
- Create: apps/media-api/alembic.ini
- Create: apps/media-api/migrations/env.py
- Create: apps/media-api/migrations/script.py.mako
- Create: apps/media-api/migrations/versions/20260809_0001_media_library.py
- Create: apps/media-api/src/media_api/intake/models.py
- Create: apps/media-api/src/media_api/intake/state.py
- Create: apps/media-api/src/media_api/taxonomy/models.py
- Create: apps/media-api/src/media_api/processing/models.py
- Create: apps/media-api/src/media_api/catalog/models.py
- Create: apps/media-api/src/media_api/settings/models.py
- Create: apps/media-api/tests/unit/intake/test_state.py
- Create: apps/media-api/tests/integration/test_migration.py
- Modify: apps/media-api/src/media_api/infrastructure/db.py

**Interfaces:**
- Consumes: BootstrapSettings and SQLAlchemy session factory from Task 1.
- Produces: Base, InboxStatus, InboxAction, transition_status(), retry_target(), allowed_actions(), all persisted entities, and Alembic revision 20260809_0001.

- [ ] **Step 1: Write failing state-machine tests**

    def test_ready_item_can_edit_and_ingest():
        assert allowed_actions(InboxStatus.READY_TO_TAG) == {
            InboxAction.EDIT_METADATA,
            InboxAction.INGEST,
        }

    def test_rejects_invalid_transition():
        with pytest.raises(InvalidTransition):
            transition_status(InboxStatus.DISCOVERED, InboxStatus.COMPLETED)

    def test_failed_item_returns_to_recorded_retry_state():
        assert retry_target(InboxStatus.PROCESSING) is InboxStatus.PROCESSING

- [ ] **Step 2: Run the state tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/intake/test_state.py -q

Expected: FAIL because media_api.intake.state is missing.

- [ ] **Step 3: Implement the state machine**

    TRANSITIONS = {
        InboxStatus.DISCOVERED: {InboxStatus.ANALYZING, InboxStatus.FAILED},
        InboxStatus.ANALYZING: {
            InboxStatus.PROCESSING,
            InboxStatus.READY_TO_TAG,
            InboxStatus.FAILED,
        },
        InboxStatus.PROCESSING: {InboxStatus.READY_TO_TAG, InboxStatus.FAILED},
        InboxStatus.READY_TO_TAG: {
            InboxStatus.ANALYZING,
            InboxStatus.INGESTING,
            InboxStatus.CONFLICT,
            InboxStatus.FAILED,
        },
        InboxStatus.CONFLICT: {InboxStatus.READY_TO_TAG, InboxStatus.INGESTING},
        InboxStatus.INGESTING: {
            InboxStatus.COMPLETED,
            InboxStatus.CONFLICT,
            InboxStatus.FAILED,
        },
        InboxStatus.COMPLETED: set(),
        InboxStatus.FAILED: {
            InboxStatus.ANALYZING,
            InboxStatus.PROCESSING,
            InboxStatus.READY_TO_TAG,
        },
    }

    def transition_status(current: InboxStatus, target: InboxStatus) -> InboxStatus:
        if target not in TRANSITIONS[current]:
            raise InvalidTransition(current=current, target=target)
        return target

    def retry_target(failed_from_status: InboxStatus) -> InboxStatus:
        if failed_from_status not in {
            InboxStatus.ANALYZING,
            InboxStatus.PROCESSING,
            InboxStatus.INGESTING,
        }:
            raise InvalidRetryTarget(failed_from_status)
        if failed_from_status is InboxStatus.INGESTING:
            return InboxStatus.READY_TO_TAG
        return failed_from_status

Keep allowed_actions in the same module so the router and frontend receive one authoritative rule set.

- [ ] **Step 4: Define the database entities and constraints**

Use UUID strings, timezone-aware UTC timestamps, string enum columns, and named foreign keys. Create:

- scan_candidates with unique canonical_path.
- inbox_items, source_files, inbox_status_events.
- authors, series, tags, inbox_item_authors, inbox_item_tags.
- processing_plans, processing_jobs.
- media_assets, media_asset_authors, media_asset_tags.
- ingest_operations.
- app_settings and processor_settings.

The critical constraints are:

    UniqueConstraint("canonical_path", name="uq_scan_candidate_path")
    UniqueConstraint("inbox_item_id", "author_id", name="uq_inbox_author")
    UniqueConstraint("inbox_item_id", "tag_id", name="uq_inbox_tag")
    UniqueConstraint("media_asset_id", "author_id", name="uq_asset_author")
    UniqueConstraint("media_asset_id", "tag_id", name="uq_asset_tag")
    UniqueConstraint("idempotency_key", name="uq_processing_job_idempotency")
    UniqueConstraint("idempotency_key", name="uq_ingest_operation_idempotency")

InboxItem and MediaAsset each hold one nullable series_id. InboxItem also holds nullable display_name, immutable suggested_actual_filename, and nullable failed_from_status. SourceFile holds nullable content_sha256, populated during ingest preparation before cleanup can be scheduled.

IngestOperation state values are prepared, file_committed, db_committed, cleanup_pending, completed, and failed. Persist staged_path, final_relative_path, source_hash, resolution, replacement_asset_id, backup_path, and diagnostic_message.

- [ ] **Step 5: Write and run the migration test**

    def test_initial_migration_creates_only_media_tables(migrated_engine):
        tables = set(inspect(migrated_engine).get_table_names())
        assert {
            "inbox_items",
            "source_files",
            "media_assets",
            "processing_jobs",
            "ingest_operations",
        } <= tables
        assert "captures" not in tables
        assert "pending_items" not in tables

Run:

    python -m pytest apps/media-api/tests/integration/test_migration.py -q

Expected before migration implementation: FAIL because the Alembic configuration or revision is missing. Expected after implementation: PASS.

- [ ] **Step 6: Run all schema and state tests**

Run:

    python -m pytest apps/media-api/tests/unit/intake/test_state.py apps/media-api/tests/integration/test_migration.py -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): add media persistence model"

---

### Task 3: Build the Safe Local Storage Gateway

**Files:**
- Create: apps/media-api/src/media_api/storage/contracts.py
- Create: apps/media-api/src/media_api/storage/hashing.py
- Create: apps/media-api/src/media_api/storage/local.py
- Create: apps/media-api/tests/unit/storage/test_paths.py
- Create: apps/media-api/tests/integration/storage/test_local_storage.py

**Interfaces:**
- Consumes: BootstrapSettings from Task 1.
- Produces: FileSnapshot, LocalStorage.snapshot(), sha256(), provision_directories(), copy_to_stage(), commit_stage(), backup_existing(), restore_backup(), delete_verified(), and next_available_name().

- [ ] **Step 1: Write failing containment and staging tests**

    def test_rejects_target_outside_library(storage, tmp_path):
        with pytest.raises(UnsafePathError):
            storage.commit_stage(tmp_path / "stage.mp4", Path("../escape.mp4"))

    def test_staging_preserves_source(storage, inbox_file):
        staged = storage.copy_to_stage("op-1", inbox_file, "clip.mp4")
        assert inbox_file.exists()
        assert staged.exists()
        assert staged.parent.name == "op-1"

    def test_delete_requires_expected_hash(storage, inbox_file):
        with pytest.raises(FileChangedError):
            storage.delete_verified(inbox_file, expected_sha256="wrong")
        assert inbox_file.exists()

- [ ] **Step 2: Run the storage tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/storage apps/media-api/tests/integration/storage -q

Expected: FAIL because media_api.storage contracts do not exist.

- [ ] **Step 3: Define the storage contract**

    class StoragePort(Protocol):
        def snapshot(self, path: Path) -> FileSnapshot: ...
        def sha256(self, path: Path) -> str: ...
        def provision_directories(self, paths: RuntimePaths) -> None: ...
        def copy_to_stage(
            self, operation_id: str, source: Path, actual_filename: str
        ) -> Path: ...
        def commit_stage(self, staged: Path, relative_target: Path) -> Path: ...
        def backup_existing(self, operation_id: str, target: Path) -> Path: ...
        def restore_backup(self, backup: Path, target: Path) -> None: ...
        def delete_verified(self, path: Path, expected_sha256: str) -> None: ...
        def next_available_name(self, actual_filename: str) -> str: ...

FileSnapshot contains the resolved path, byte size, and st_mtime_ns. UnsafePathError and FileChangedError are stable domain errors later mapped by api/errors.py.

- [ ] **Step 4: Implement containment, hashing, and atomic finalization**

    def ensure_within(path: Path, root: Path) -> Path:
        resolved = path.resolve()
        resolved.relative_to(root.resolve())
        return resolved

    def commit_stage(self, staged: Path, relative_target: Path) -> Path:
        target = ensure_within(self.library_dir / relative_target, self.library_dir)
        target.parent.mkdir(parents=True, exist_ok=True)
        os.replace(staged, target)
        return target

provision_directories creates only the three already-validated RuntimePaths and the internal .staging, jobs, backups, and previews children. copy_to_stage must check free space, copy into LIBRARY_DIR/.staging/{operation_id}, fsync the file, and compare source and staged hashes. backup_existing stores files under WORK_DIR/backups/{operation_id}. delete_verified must return successfully if the path is already absent, but may delete an existing path only when its current hash matches expected_sha256.

- [ ] **Step 5: Implement Windows-style keep-both naming**

    @pytest.mark.parametrize(
        ("requested", "existing", "expected"),
        [
            ("clip.mp4", ["clip.mp4"], "clip (2).mp4"),
            ("clip.mp4", ["clip.mp4", "clip (2).mp4"], "clip (3).mp4"),
            ("archive.tar.mp4", ["archive.tar.mp4"], "archive.tar (2).mp4"),
        ],
    )
    def test_next_available_name(storage, requested, existing, expected):
        for name in existing:
            (storage.library_dir / name).write_bytes(b"x")
        assert storage.next_available_name(requested) == expected

- [ ] **Step 6: Run storage tests**

Run:

    python -m pytest apps/media-api/tests/unit/storage apps/media-api/tests/integration/storage -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-api/src/media_api/storage apps/media-api/tests
    git commit -m "feat(media-api): add safe local storage gateway"

---

### Task 4: Discover Stable Inbox Media Idempotently

**Files:**
- Create: apps/media-api/src/media_api/intake/schemas.py
- Create: apps/media-api/src/media_api/intake/repository.py
- Create: apps/media-api/src/media_api/intake/scanner.py
- Create: apps/media-api/src/media_api/intake/service.py
- Create: apps/media-api/src/media_api/intake/router.py
- Create: apps/media-api/tests/unit/intake/test_scanner.py
- Create: apps/media-api/tests/integration/intake/test_scan_service.py
- Create: apps/media-api/tests/api/test_inbox_api.py
- Modify: apps/media-api/src/media_api/main.py

**Interfaces:**
- Consumes: Session factory, ScanCandidate, InboxItem, SourceFile, state machine, and LocalStorage.
- Produces: ScanService.scan(now), InboxService.list_items(), InboxItemRead, POST /api/inbox/scan, GET /api/inbox/items, and GET /api/inbox/items/{id}.

- [ ] **Step 1: Write failing scanner policy tests**

    def test_ignores_download_temporary_files(scanner, inbox_dir):
        (inbox_dir / "clip.mp4.part").write_bytes(b"x")
        (inbox_dir / "image.jpg.crdownload").write_bytes(b"x")
        assert scanner.observe() == []

    def test_requires_two_unchanged_observations(scanner, inbox_dir, clock):
        path = inbox_dir / "photo.jpg"
        path.write_bytes(b"image")
        assert scanner.observe(now=clock.now()) == []
        stable = scanner.observe(now=clock.advance(seconds=2))
        assert [item.path.name for item in stable] == ["photo.jpg"]

    def test_changed_size_resets_stability(scanner, inbox_dir, clock):
        path = inbox_dir / "clip.mp4"
        path.write_bytes(b"a")
        scanner.observe(now=clock.now())
        path.write_bytes(b"ab")
        assert scanner.observe(now=clock.advance(seconds=2)) == []

- [ ] **Step 2: Run the scanner tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/intake/test_scanner.py -q

Expected: FAIL because InboxScanner is missing.

- [ ] **Step 3: Implement candidate observation**

    SUPPORTED_DIRECT_EXTENSIONS = {
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
        ".mp4", ".mov", ".mkv", ".webm",
    }
    IGNORED_SUFFIXES = {".part", ".crdownload", ".tmp"}

    @dataclass(frozen=True)
    class StableCandidate:
        path: Path
        media_type: Literal["image", "video"]
        snapshot: FileSnapshot

The scanner records every non-temporary candidate in scan_candidates. A candidate becomes stable only when two persisted observations have identical size and modified_ns. Standalone .m4a files remain stable unclaimed candidates; they never become direct MediaAsset items.

- [ ] **Step 4: Write the idempotent service test**

    def test_scan_creates_one_item_after_repeated_refreshes(scan_service, inbox_dir):
        (inbox_dir / "photo.jpg").write_bytes(b"image")
        scan_service.scan()
        second = scan_service.scan()
        third = scan_service.scan()
        assert second.created_items == 1
        assert third.created_items == 0
        assert scan_service.list_items()[0].status == "ready_to_tag"

- [ ] **Step 5: Implement direct-item analysis and API routes**

For each stable supported direct candidate not already linked to SourceFile:

    item = InboxItem(
        media_type=candidate.media_type,
        status=InboxStatus.READY_TO_TAG,
        suggested_actual_filename=candidate.path.name,
    )
    item.sources.append(
        SourceFile.from_snapshot(candidate.snapshot, is_primary=True)
    )

Persist InboxStatusEvent for discovered, analyzing, and ready_to_tag in the same transaction. POST /api/inbox/scan calls ScanService.scan(); list endpoints omit completed items by default and include status, allowed_actions, source summary, display_name, author_ids, series_id, and tag_ids.

- [ ] **Step 6: Run scanner, service, and API tests**

Run:

    python -m pytest apps/media-api/tests/unit/intake apps/media-api/tests/integration/intake apps/media-api/tests/api/test_inbox_api.py -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): discover stable inbox media"

---

### Task 5: Add Taxonomy and Editable Inbox Metadata Drafts

**Files:**
- Create: apps/media-api/src/media_api/taxonomy/schemas.py
- Create: apps/media-api/src/media_api/taxonomy/repository.py
- Create: apps/media-api/src/media_api/taxonomy/service.py
- Create: apps/media-api/src/media_api/taxonomy/router.py
- Create: apps/media-api/tests/unit/taxonomy/test_names.py
- Create: apps/media-api/tests/api/test_taxonomy_api.py
- Create: apps/media-api/tests/api/test_inbox_metadata_api.py
- Modify: apps/media-api/src/media_api/intake/schemas.py
- Modify: apps/media-api/src/media_api/intake/service.py
- Modify: apps/media-api/src/media_api/intake/router.py
- Modify: apps/media-api/src/media_api/main.py

**Interfaces:**
- Consumes: Author, Series, Tag, InboxItem, and InboxAction from Tasks 2 and 4.
- Produces: normalize_taxonomy_name(), taxonomy CRUD without delete, PATCH /api/inbox/items/{id}/metadata, and PATCH /api/inbox/items/bulk-metadata.

- [ ] **Step 1: Write failing normalization and uniqueness tests**

    @pytest.mark.parametrize(
        ("raw", "display", "normalized"),
        [
            ("  Alice  ", "Alice", "alice"),
            ("旅行　记录", "旅行 记录", "旅行 记录"),
        ],
    )
    def test_normalize_taxonomy_name(raw, display, normalized):
        value = normalize_taxonomy_name(raw)
        assert value.display == display
        assert value.normalized == normalized

    def test_duplicate_author_returns_existing(author_service):
        first = author_service.create("Alice")
        second = author_service.create(" alice ")
        assert second.id == first.id

- [ ] **Step 2: Run taxonomy tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/taxonomy/test_names.py -q

Expected: FAIL because taxonomy service is missing.

- [ ] **Step 3: Implement taxonomy services and routes**

    def normalize_taxonomy_name(raw: str) -> NormalizedName:
        display = " ".join(raw.strip().split())
        if not display:
            raise EmptyTaxonomyName()
        return NormalizedName(display=display, normalized=display.casefold())

Expose GET, POST, and PATCH for /api/authors, /api/series, and /api/tags. POST is create-or-return-existing by normalized_name. PATCH may change display name only after checking normalized uniqueness. Do not add deletion in the first release.

- [ ] **Step 4: Write failing draft metadata tests**

    def test_updates_multi_author_single_series_and_multi_tag(client, ready_item, taxonomy):
        response = client.patch(
            f"/api/inbox/items/{ready_item.id}/metadata",
            json={
                "display_name": "杭州夜游",
                "author_ids": taxonomy.author_ids[:2],
                "series_id": taxonomy.series_id,
                "tag_ids": taxonomy.tag_ids[:2],
            },
        )
        assert response.status_code == 200
        body = response.json()
        assert body["display_name"] == "杭州夜游"
        assert body["author_ids"] == taxonomy.author_ids[:2]
        assert body["series_id"] == taxonomy.series_id
        assert body["tag_ids"] == taxonomy.tag_ids[:2]

    def test_bulk_metadata_appends_without_erasing_existing(client, two_ready_items, taxonomy):
        response = client.patch(
            "/api/inbox/items/bulk-metadata",
            json={
                "item_ids": [str(item.id) for item in two_ready_items],
                "append_author_ids": [taxonomy.author_ids[0]],
                "append_tag_ids": [taxonomy.tag_ids[0]],
                "replace_series": True,
                "replace_series_id": taxonomy.series_id,
            },
        )
        assert response.status_code == 200
        assert response.json()["updated_count"] == 2

- [ ] **Step 5: Implement metadata commands in one transaction**

InboxMetadataPatch replaces the complete author_ids, series_id, and tag_ids of one item. BulkMetadataPatch appends authors and tags using set union and replaces series only when replace_series is true; an explicit null replace_series_id with replace_series true clears the series. Register the static /bulk-metadata route before the dynamic /{id}/metadata route. Validate all taxonomy IDs before changing any selected item so a bulk request is all-or-nothing.

- [ ] **Step 6: Run taxonomy and metadata tests**

Run:

    python -m pytest apps/media-api/tests/unit/taxonomy apps/media-api/tests/api/test_taxonomy_api.py apps/media-api/tests/api/test_inbox_metadata_api.py -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): add metadata drafts and taxonomy"

---

### Task 6: Complete the First Transactional Direct-Ingest Slice

**Files:**
- Create: apps/media-api/src/media_api/catalog/schemas.py
- Create: apps/media-api/src/media_api/catalog/repository.py
- Create: apps/media-api/src/media_api/catalog/ingest.py
- Create: apps/media-api/src/media_api/catalog/service.py
- Create: apps/media-api/src/media_api/catalog/router.py
- Create: apps/media-api/tests/integration/catalog/test_direct_ingest.py
- Create: apps/media-api/tests/api/test_ingest_api.py
- Create: apps/media-api/tests/api/test_library_api.py
- Modify: apps/media-api/src/media_api/main.py

**Interfaces:**
- Consumes: LocalStorage, InboxItem metadata draft, taxonomy entities, and IngestOperation.
- Produces: IngestService.ingest(), IngestResult, GET /api/library/assets, GET /api/library/assets/{id}, PATCH /api/library/assets/{id}/metadata, and POST /api/inbox/items/{id}/ingest.

- [ ] **Step 1: Write a failing prepare-phase test**

    def test_prepare_copies_verified_source_without_deleting_it(
        ingest_service, ready_video, source_path, storage
    ):
        operation = ingest_service.prepare(
            item_id=ready_video.id,
            idempotency_key="ingest-1",
        )
        assert operation.state == "prepared"
        assert source_path.exists()
        assert Path(operation.staged_path).exists()
        assert storage.sha256(Path(operation.staged_path)) == operation.source_hash

- [ ] **Step 2: Run the prepare test and verify failure**

Run:

    python -m pytest apps/media-api/tests/integration/catalog/test_direct_ingest.py::test_prepare_copies_verified_source_without_deleting_it -q

Expected: FAIL because IngestService is missing.

- [ ] **Step 3: Implement prepare with a durable journal**

    def prepare(self, item_id: UUID, idempotency_key: str) -> IngestOperation:
        existing = self.operations.by_idempotency_key(idempotency_key)
        if existing:
            return existing
        item = self.inbox.require_ready(item_id)
        source = self.inputs.final_input_for(item)
        current = self.storage.snapshot(source.path)
        if current != source.discovery_snapshot:
            raise SourceChanged(item_id)
        digest = self.storage.sha256(source.path)
        conflict = self.conflicts.detect(item.suggested_actual_filename, digest)
        if conflict:
            return self.operations.record_conflict(item, digest, conflict, idempotency_key)
        operation = self.operations.create_preparing(item, digest, idempotency_key)
        staged = self.storage.copy_to_stage(
            str(operation.id), source.path, item.suggested_actual_filename
        )
        return self.operations.mark_prepared(operation.id, staged)

Persist an operation before copying, then update it after staged-file hash validation. A conflict changes the InboxItem to conflict and returns IngestResult(kind="conflict", conflict_id=operation.id); it must not copy or delete the source.

- [ ] **Step 4: Write the failing commit-and-cleanup test**

    def test_ingest_moves_file_and_persists_metadata(
        ingest_service, ready_video, source_path, session
    ):
        result = ingest_service.ingest(ready_video.id, "ingest-1")
        asset = session.get(MediaAsset, result.asset_id)
        assert result.kind == "completed"
        assert asset.actual_filename == source_path.name
        assert asset.display_name == ready_video.display_name
        assert {author.id for author in asset.authors} == {
            author.id for author in ready_video.authors
        }
        assert asset.series_id == ready_video.series_id
        assert not source_path.exists()
        assert (ingest_service.storage.library_dir / source_path.name).exists()

- [ ] **Step 5: Implement commit and cleanup**

Before staging, calculate and persist content_sha256 for every SourceFile so later cleanup can verify each original independently. Then perform these exact checkpoints:

1. Transition InboxItem from ready_to_tag to ingesting.
2. Atomically move staged file to LIBRARY_DIR/actual_filename.
3. In one SQLite transaction create MediaAsset and its relations, mark InboxItem completed, and mark IngestOperation db_committed.
4. Set operation cleanup_pending.
5. Delete every SourceFile with delete_verified using its persisted full hash.
6. Delete temporary processing output and empty operation staging directory.
7. Mark operation completed.

If cleanup raises, return a completed asset plus cleanup_warning and leave the operation cleanup_pending. Never roll back the asset or final file because cleanup failed.

- [ ] **Step 6: Add API idempotency and library metadata tests**

    def test_repeated_ingest_key_returns_same_asset(client, ready_item):
        headers = {"Idempotency-Key": "same-command"}
        first = client.post(f"/api/inbox/items/{ready_item.id}/ingest", headers=headers)
        second = client.post(f"/api/inbox/items/{ready_item.id}/ingest", headers=headers)
        assert first.status_code == 200
        assert second.status_code == 200
        assert second.json()["asset_id"] == first.json()["asset_id"]

    def test_metadata_patch_cannot_rename_physical_file(client, asset):
        response = client.patch(
            f"/api/library/assets/{asset.id}/metadata",
            json={"display_name": "New title", "actual_filename": "renamed.mp4"},
        )
        assert response.status_code == 422

The library query supports search over display_name and actual_filename. The metadata patch uses the same author, series, and tag constraints as the inbox draft and never accepts actual_filename, relative_path, or sha256.

- [ ] **Step 7: Run the direct-ingest slice**

Run:

    python -m pytest apps/media-api/tests/integration/catalog/test_direct_ingest.py apps/media-api/tests/api/test_ingest_api.py apps/media-api/tests/api/test_library_api.py -q

Expected: PASS.

- [ ] **Step 8: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): add transactional direct ingest"

---

### Task 7: Recover Interrupted Ingests and Retry Cleanup

**Files:**
- Create: apps/media-api/src/media_api/catalog/recovery.py
- Create: apps/media-api/tests/integration/catalog/test_ingest_recovery.py
- Create: apps/media-api/tests/integration/catalog/test_cleanup_retry.py
- Modify: apps/media-api/src/media_api/catalog/ingest.py
- Modify: apps/media-api/src/media_api/storage/contracts.py

**Interfaces:**
- Consumes: IngestOperation checkpoints and StoragePort from Tasks 3 and 6.
- Produces: IngestRecoveryService.recover_all(), recover(operation_id), and retry_cleanup(operation_id).

- [ ] **Step 1: Add a filesystem fault-injection fake**

    class FaultingStorage(StoragePort):
        def __init__(self, delegate: StoragePort, fail_after: str):
            self.delegate = delegate
            self.fail_after = fail_after

        def commit_stage(self, staged, relative_target):
            result = self.delegate.commit_stage(staged, relative_target)
            if self.fail_after == "file_commit":
                raise SimulatedCrash("file_commit")
            return result

        def delete_verified(self, path, expected_sha256):
            if self.fail_after == "cleanup":
                raise SimulatedCrash("cleanup")
            return self.delegate.delete_verified(path, expected_sha256)

- [ ] **Step 2: Write failing recovery tests for every checkpoint**

    @pytest.mark.parametrize(
        "checkpoint",
        ["prepared", "file_committed", "db_committed", "cleanup_pending"],
    )
    def test_restart_recovers_without_loss_or_duplicate(
        checkpoint, interrupted_operation, recovery, session
    ):
        interrupted_operation.stop_at(checkpoint)
        recovery.recover_all()
        assert session.query(MediaAsset).count() == 1
        assert interrupted_operation.source_deleted_only_after_db_commit()
        assert interrupted_operation.final_hash_matches_source()

    def test_unknown_final_file_is_never_overwritten(operation, recovery):
        operation.place_unexpected_final_bytes()
        with pytest.raises(RecoveryRequiresAttention):
            recovery.recover(operation.id)
        assert operation.source_path.exists()
        assert operation.unexpected_final_path.exists()

- [ ] **Step 3: Run the recovery tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/integration/catalog/test_ingest_recovery.py -q

Expected: FAIL because IngestRecoveryService is missing.

- [ ] **Step 4: Implement hash-driven recovery**

    def recover(self, operation_id: UUID) -> RecoveryResult:
        operation = self.operations.require(operation_id)
        staged = operation.staged_path and Path(operation.staged_path)
        final = self.storage.library_dir / operation.final_relative_path
        if operation.state == "prepared":
            if final.exists():
                self._require_hash(final, operation.source_hash)
                return self._finish_database_commit(operation)
            self._require_hash(staged, operation.source_hash)
            return self._commit_from_stage(operation)
        if operation.state in {"file_committed", "db_committed"}:
            self._require_hash(final, operation.source_hash)
            self._finish_database_commit(operation)
            return self.retry_cleanup(operation.id)
        if operation.state == "cleanup_pending":
            return self.retry_cleanup(operation.id)
        return RecoveryResult.noop(operation.id)

Recovery must never infer identity from a filename. A missing source is acceptable only after db_committed; otherwise report RecoveryRequiresAttention and preserve all remaining files.

- [ ] **Step 5: Write and implement cleanup retry behavior**

    def test_cleanup_failure_keeps_completed_asset_and_retries(
        committed_operation, recovery, session
    ):
        first = recovery.retry_cleanup(committed_operation.id)
        assert first.warning_code == "CLEANUP_PENDING"
        assert session.get(MediaAsset, committed_operation.asset_id) is not None
        committed_operation.disable_fault()
        second = recovery.retry_cleanup(committed_operation.id)
        assert second.state == "completed"
        assert not committed_operation.source_path.exists()

- [ ] **Step 6: Run recovery and existing ingest tests**

Run:

    python -m pytest apps/media-api/tests/integration/catalog -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): recover interrupted ingests"

---

### Task 8: Add the Extensible Persistent Processing Pipeline

**Files:**
- Create: apps/media-api/src/media_api/processing/contracts.py
- Create: apps/media-api/src/media_api/processing/registry.py
- Create: apps/media-api/src/media_api/processing/planner.py
- Create: apps/media-api/src/media_api/processing/worker.py
- Create: apps/media-api/src/media_api/processing/router.py
- Create: apps/media-api/tests/unit/processing/test_registry.py
- Create: apps/media-api/tests/unit/processing/test_planner.py
- Create: apps/media-api/tests/integration/processing/test_worker.py
- Create: apps/media-api/tests/api/test_processing_api.py
- Modify: apps/media-api/src/media_api/main.py

**Interfaces:**
- Consumes: ProcessingPlan, ProcessingJob, SourceFile, InboxStatus, and WORK_DIR.
- Produces: ProcessorDescriptor, Detection, ProcessingContext, ProducedFile, ProcessorRegistry, ProcessingPlanner, PersistentWorker.run_once(), and POST /api/inbox/items/{id}/process.

- [ ] **Step 1: Write failing registry and planning tests**

    def test_registry_rejects_duplicate_names():
        registry = ProcessorRegistry()
        registry.register(FakeProcessor(name="convert", version="1"))
        with pytest.raises(DuplicateProcessorName):
            registry.register(FakeProcessor(name="convert", version="2"))

    def test_planner_orders_explicit_dependencies():
        registry = ProcessorRegistry(
            [
                FakeProcessor(name="cleanup", depends_on=("merge",), priority=20),
                FakeProcessor(name="merge", depends_on=(), priority=10),
            ]
        )
        plan = ProcessingPlanner(registry).plan(SOURCES)
        assert [step.processor_name for step in plan.steps] == ["merge", "cleanup"]

    def test_planner_rejects_dependency_cycle():
        with pytest.raises(ProcessorDependencyCycle):
            ProcessingPlanner(cyclic_registry).plan(SOURCES)

- [ ] **Step 2: Run the planner tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/processing -q

Expected: FAIL because processing contracts and registry are missing.

- [ ] **Step 3: Define processor contracts**

    @dataclass(frozen=True)
    class ProcessorDescriptor:
        name: str
        version: str
        priority: int
        depends_on: tuple[str, ...]
        auto_run_default: bool

    @dataclass(frozen=True)
    class ProcessingContext:
        job_id: UUID
        input_files: tuple[Path, ...]
        job_work_dir: Path

    @dataclass(frozen=True)
    class ProducedFile:
        path: Path
        actual_filename: str
        media_type: Literal["image", "video"]

ProcessorRegistry returns a stable name-sorted snapshot. ProcessingPlanner persists processor name, version, ordered steps, declared inputs, and expected output name as JSON in ProcessingPlan.

- [ ] **Step 4: Write the persistent worker test**

    def test_worker_marks_only_failed_item_and_continues(
        worker, queued_success_job, queued_failure_job, session
    ):
        worker.run_once()
        worker.run_once()
        session.refresh(queued_success_job)
        session.refresh(queued_failure_job)
        assert queued_success_job.status == "completed"
        assert queued_failure_job.status == "failed"
        assert queued_failure_job.retryable is True

    def test_worker_rejects_output_outside_job_work_dir(worker, escaping_processor):
        result = worker.execute(escaping_processor.job)
        assert result.error_code == "PROCESSOR_OUTPUT_OUTSIDE_WORK_DIR"

- [ ] **Step 5: Implement job claiming and execution**

PersistentWorker.run_once performs one compare-and-set claim from queued to running in SQLite, creates WORK_DIR/jobs/{job_id}, executes steps, validates every output remains under that directory, and updates current_step after each successful step. A completed job stores output_path and transitions the InboxItem to ready_to_tag. A failed job stores error_code, user_message, diagnostic_message, and retryable, then transitions only that InboxItem to failed.

On process command, require Idempotency-Key. Return an existing job for a duplicate key. Auto-run is allowed only when every selected processor is enabled and auto_run_default is true.

- [ ] **Step 6: Implement interrupted-job recovery**

At startup, running jobs become queued with attempt_count incremented when retryable is true; non-retryable running jobs become failed. Delete only their own WORK_DIR/jobs/{job_id} outputs after verifying containment. Never delete SourceFile paths.

- [ ] **Step 7: Run processing tests**

Run:

    python -m pytest apps/media-api/tests/unit/processing apps/media-api/tests/integration/processing apps/media-api/tests/api/test_processing_api.py -q

Expected: PASS.

- [ ] **Step 8: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): add persistent processing pipeline"

---

### Task 9: Implement the MP4/M4A Merge Processor

**Files:**
- Create: apps/media-api/src/media_api/processing/processors/__init__.py
- Create: apps/media-api/src/media_api/processing/processors/mp4_m4a.py
- Create: apps/media-api/tests/unit/processing/test_mp4_m4a_detection.py
- Create: apps/media-api/tests/integration/processing/test_mp4_m4a_processor.py
- Create: apps/media-api/tests/integration/processing/test_late_audio_pairing.py
- Create: apps/media-api/tests/fixtures/media/paired.mp4
- Create: apps/media-api/tests/fixtures/media/paired.m4a
- Modify: apps/media-api/src/media_api/intake/service.py
- Modify: apps/media-api/src/media_api/processing/registry.py
- Modify: apps/media-api/tests/conftest.py

**Interfaces:**
- Consumes: Processor protocol, ScanCandidate, existing ready MP4 InboxItem, and PersistentWorker.
- Produces: Mp4M4aMergeProcessor and analyzer pairing that owns two SourceFiles in one InboxItem.

- [ ] **Step 1: Generate tiny, deterministic media fixtures**

Run from the repository root:

    New-Item -ItemType Directory -Force apps/media-api/tests/fixtures/media
    ffmpeg -y -f lavfi -i testsrc=size=64x64:rate=5 -t 1 -an -c:v mpeg4 -q:v 10 apps/media-api/tests/fixtures/media/paired.mp4
    ffmpeg -y -f lavfi -i sine=frequency=1000:sample_rate=44100 -t 1 -vn -c:a aac apps/media-api/tests/fixtures/media/paired.m4a

Expected: both files are created locally without network access. Keep each below 100 KB; if the encoder produces larger files, reduce duration to 0.5 seconds.

- [ ] **Step 2: Write failing detection tests**

    def test_detects_same_directory_same_stem_pair(processor, snapshots):
        detection = processor.detect(
            [snapshots("paired.mp4"), snapshots("paired.m4a")]
        )
        assert detection.processor_name == "mp4_m4a_merge"
        assert detection.primary.name == "paired.mp4"
        assert detection.companions[0].name == "paired.m4a"

    def test_does_not_pair_different_directories_or_stems(processor, snapshots):
        assert processor.detect(
            [snapshots("a/paired.mp4"), snapshots("b/paired.m4a")]
        ) is None
        assert processor.detect(
            [snapshots("clip.mp4"), snapshots("audio.m4a")]
        ) is None

- [ ] **Step 3: Run detection tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/processing/test_mp4_m4a_detection.py -q

Expected: FAIL because Mp4M4aMergeProcessor is missing.

- [ ] **Step 4: Implement command execution and ffprobe validation**

    command = [
        self.ffmpeg_path,
        "-y",
        "-i", str(video_path),
        "-i", str(audio_path),
        "-map", "0:v:0",
        "-map", "1:a:0",
        "-c", "copy",
        str(output_path),
    ]

Run subprocesses with shell=False, captured stderr, and a configured timeout. Validation runs ffprobe with JSON output and requires at least one stream with codec_type video and one with codec_type audio. Map timeout, missing executable, invalid input, and missing streams to separate stable error codes. The output actual_filename is the input MP4 filename.

- [ ] **Step 5: Handle simultaneous and late-arriving pairs**

Before direct analysis, the intake analyzer passes all stable unclaimed candidates to the processor registry. If paired MP4 and M4A are both unclaimed, create one InboxItem with two SourceFiles and queue the automatic plan.

If a stable M4A appears later, find an InboxItem in ready_to_tag whose primary MP4 has the same canonical parent and stem and which has no active IngestOperation. Attach the M4A as a companion SourceFile and transition the item through analyzing to processing. If ingest has already started or the asset is completed, leave the M4A as an unclaimed candidate and never alter the active operation or completed asset.

    def test_late_m4a_upgrades_uningested_mp4(scan_service, worker, inbox_dir):
        copy_fixture("paired.mp4", inbox_dir)
        scan_twice(scan_service)
        item_id = scan_service.list_items()[0].id
        copy_fixture("paired.m4a", inbox_dir)
        scan_twice(scan_service)
        item = scan_service.get(item_id)
        assert len(item.sources) == 2
        assert item.status == "processing"

- [ ] **Step 6: Verify merge and source cleanup through formal ingest**

    def test_merged_output_has_audio_video_and_sources_delete_after_ingest(
        paired_item, worker, ingest_service, ffprobe
    ):
        worker.run_once()
        output = paired_item.processing_output
        assert ffprobe.stream_types(output) == {"audio", "video"}
        result = ingest_service.ingest(paired_item.id, "merge-ingest")
        assert result.kind == "completed"
        assert not paired_item.mp4_source.exists()
        assert not paired_item.m4a_source.exists()
        assert result.final_path.name == paired_item.mp4_source.name

- [ ] **Step 7: Run all merge tests**

Run:

    python -m pytest apps/media-api/tests/unit/processing/test_mp4_m4a_detection.py apps/media-api/tests/integration/processing/test_mp4_m4a_processor.py apps/media-api/tests/integration/processing/test_late_audio_pairing.py -q

Expected: PASS when ffmpeg and ffprobe are available. When unavailable, only the real-media integration test may skip with a documented FFmpeg missing reason; detection tests must still pass.

- [ ] **Step 8: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): merge split audio video pairs"

---

### Task 10: Resolve Name and Content Conflicts Safely

**Files:**
- Create: apps/media-api/src/media_api/catalog/conflicts.py
- Create: apps/media-api/tests/unit/catalog/test_conflict_naming.py
- Create: apps/media-api/tests/integration/catalog/test_conflict_resolution.py
- Create: apps/media-api/tests/integration/catalog/test_replace_recovery.py
- Create: apps/media-api/tests/api/test_conflict_api.py
- Modify: apps/media-api/src/media_api/catalog/schemas.py
- Modify: apps/media-api/src/media_api/catalog/ingest.py
- Modify: apps/media-api/src/media_api/catalog/recovery.py
- Modify: apps/media-api/src/media_api/catalog/router.py

**Interfaces:**
- Consumes: IngestOperation, MediaAsset, LocalStorage backups, and ConflictResolution.
- Produces: ConflictService.detect(), resolve(), POST /api/conflicts/{operation_id}/resolve, and ConflictRead with both preview targets.

- [ ] **Step 1: Write failing conflict-detection tests**

    def test_detects_name_and_hash_conflicts(conflict_service, existing_asset):
        result = conflict_service.detect(
            actual_filename=existing_asset.actual_filename,
            sha256=existing_asset.sha256,
        )
        assert result.kinds == {"name", "content"}
        assert result.existing_asset_id == existing_asset.id

    def test_identical_hash_still_requires_user_choice(ingest_service, duplicate_item):
        result = ingest_service.ingest(duplicate_item.id, "duplicate")
        assert result.kind == "conflict"
        assert duplicate_item.source_path.exists()

- [ ] **Step 2: Run conflict tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/catalog/test_conflict_naming.py apps/media-api/tests/integration/catalog/test_conflict_resolution.py -q

Expected: FAIL because ConflictService is missing.

- [ ] **Step 3: Implement skip and keep-both**

    def resolve(self, operation_id: UUID, resolution: ConflictResolution):
        operation = self.operations.require_conflict(operation_id)
        self._require_unchanged_source_hash(operation)
        if resolution is ConflictResolution.SKIP:
            self.operations.close_as_skipped(operation.id)
            self.inbox.transition(operation.item_id, InboxStatus.READY_TO_TAG)
            return ConflictResult.skipped(operation.id)
        if resolution is ConflictResolution.KEEP_BOTH:
            name = self.storage.next_available_name(operation.requested_filename)
            self.operations.select_target(operation.id, name, resolution)
            return self.ingest.resume(operation.id)

SKIP retains all sources. KEEP_BOTH changes only actual_filename to the next available suffix; it does not change display_name.

- [ ] **Step 4: Write failing replacement tests**

    def test_replace_retains_asset_id_and_uses_incoming_metadata(
        conflict_service, existing_asset, incoming_item, session
    ):
        result = conflict_service.resolve(
            incoming_item.conflict_id, ConflictResolution.REPLACE
        )
        session.refresh(existing_asset)
        assert result.asset_id == existing_asset.id
        assert existing_asset.display_name == incoming_item.display_name
        assert existing_asset.sha256 == incoming_item.sha256
        assert session.query(MediaAsset).count() == 1

    def test_changed_source_invalidates_old_resolution(conflict_service, conflict):
        conflict.source_path.write_bytes(b"changed")
        with pytest.raises(ConflictChoiceExpired):
            conflict_service.resolve(conflict.id, ConflictResolution.REPLACE)

- [ ] **Step 5: Implement recoverable replacement**

Replacement must:

1. Snapshot old MediaAsset scalar fields and taxonomy IDs into IngestOperation.
2. Move the old formal file to WORK_DIR/backups/{operation_id}.
3. Commit the new staged file to the same actual filename.
4. Update the existing MediaAsset row in place with incoming hash, size, timestamps, display name, authors, series, and tags.
5. Mark db_committed in the same SQLite transaction.
6. Delete the temporary backup only during cleanup.

Recovery restores both the backup file and old metadata when the new final hash cannot be verified before db_committed. After db_committed, recovery completes cleanup rather than restoring the old version.

- [ ] **Step 6: Add the conflict API contract**

    def test_conflict_response_has_side_by_side_metadata(client, conflict):
        response = client.post(
            f"/api/conflicts/{conflict.id}/resolve",
            headers={"Idempotency-Key": "resolve-1"},
            json={"resolution": "keep_both"},
        )
        assert response.status_code == 200
        assert response.json()["resolution"] == "keep_both"

ConflictRead includes operation_id, conflict_kinds, incoming item metadata, existing asset metadata, incoming_preview_url, existing_preview_url, source_hash, and requested_actual_filename. A repeated resolution returns the first result; a different resolution with the same key returns 409 IDEMPOTENCY_KEY_REUSED.

- [ ] **Step 7: Run conflict and recovery tests**

Run:

    python -m pytest apps/media-api/tests/unit/catalog apps/media-api/tests/integration/catalog apps/media-api/tests/api/test_conflict_api.py -q

Expected: PASS.

- [ ] **Step 8: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): resolve ingest conflicts safely"

---

### Task 11: Add Library Filters, Controlled Previews, and Reveal-in-Folder

**Files:**
- Create: apps/media-api/src/media_api/storage/preview.py
- Create: apps/media-api/tests/unit/storage/test_preview.py
- Create: apps/media-api/tests/api/test_preview_api.py
- Create: apps/media-api/tests/api/test_library_filters.py
- Create: apps/media-api/tests/unit/catalog/test_reveal.py
- Create: apps/media-api/tests/fixtures/media/photo.jpg
- Modify: apps/media-api/pyproject.toml
- Modify: apps/media-api/src/media_api/catalog/schemas.py
- Modify: apps/media-api/src/media_api/catalog/repository.py
- Modify: apps/media-api/src/media_api/catalog/service.py
- Modify: apps/media-api/src/media_api/catalog/router.py
- Modify: apps/media-api/src/media_api/intake/router.py

**Interfaces:**
- Consumes: MediaAsset, InboxItem, source hashes, and WORK_DIR.
- Produces: PreviewService.get_or_create(), filtered GET /api/library/assets, inbox and asset preview endpoints, and POST /api/library/assets/{id}/reveal.

- [ ] **Step 1: Add Pillow and write failing preview-cache tests**

Add Pillow 12.1.1 to project dependencies. Generate the fixed image fixture without network access:

    python -c "from PIL import Image; Image.new('RGB', (64, 64), '#315375').save('apps/media-api/tests/fixtures/media/photo.jpg', quality=80)"
    python -m pip install -e "./apps/media-api[dev]"

    def test_image_preview_is_cached_by_content_hash(preview_service, image_path):
        first = preview_service.get_or_create(image_path, "image", "abc123")
        second = preview_service.get_or_create(image_path, "image", "abc123")
        assert first == second
        assert first.parent.name == "previews"
        assert first.suffix == ".jpg"

    def test_preview_rejects_path_outside_registered_input(preview_service, tmp_path):
        with pytest.raises(UnregisteredMediaPath):
            preview_service.get_or_create(tmp_path / "unknown.jpg", "image", "x")

- [ ] **Step 2: Run preview tests and verify failure**

Run:

    python -m pytest apps/media-api/tests/unit/storage/test_preview.py -q

Expected: FAIL because PreviewService is missing.

- [ ] **Step 3: Implement regenerable previews**

For images, open with Pillow, apply EXIF transpose, convert to RGB, thumbnail to at most 640 by 360, and save JPEG under WORK_DIR/previews/{sha256}.jpg using a temporary file plus os.replace.

For videos, run:

    [
        ffmpeg_path,
        "-y",
        "-ss", "00:00:01",
        "-i", str(media_path),
        "-frames:v", "1",
        "-vf", "scale=640:-2",
        str(temporary_preview),
    ]

If a video is shorter than one second, retry at 00:00:00. Preview failures return a typed placeholder response and never change InboxItem status. Preview files are caches and may be regenerated after deletion.

- [ ] **Step 4: Write and implement filter tests**

    def test_combines_name_taxonomy_and_media_filters(client, seeded_assets):
        response = client.get(
            "/api/library/assets",
            params={
                "search": "杭州",
                "author_id": seeded_assets.author_id,
                "series_id": seeded_assets.series_id,
                "tag_id": seeded_assets.tag_id,
                "media_type": "video",
            },
        )
        assert response.status_code == 200
        assert [item["id"] for item in response.json()["items"]] == [
            str(seeded_assets.matching_id)
        ]

Use SQL EXISTS clauses for author and tag filters, a direct series_id predicate, and case-insensitive matching over display_name and actual_filename. Return deterministic order by ingested_at descending then id.

- [ ] **Step 5: Add controlled preview and reveal routes**

Add:

    GET /api/inbox/items/{id}/preview
    GET /api/library/assets/{id}/preview
    POST /api/library/assets/{id}/reveal

The service resolves paths from registered database records. It never accepts a path parameter. On Windows, reveal invokes explorer.exe with /select,{absolute_file}; on other platforms use the supported file-manager command behind a RevealPort. Tests inject a fake port and assert the registered final path is passed.

- [ ] **Step 6: Run preview, filter, and reveal tests**

Run:

    python -m pytest apps/media-api/tests/unit/storage/test_preview.py apps/media-api/tests/unit/catalog/test_reveal.py apps/media-api/tests/api/test_preview_api.py apps/media-api/tests/api/test_library_filters.py -q

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): add previews and library filters"

---

### Task 12: Add Settings, SSE Events, Automatic Scanning, and Startup Recovery

**Files:**
- Create: apps/media-api/src/media_api/api/dependencies.py
- Create: apps/media-api/src/media_api/api/errors.py
- Create: apps/media-api/src/media_api/api/events.py
- Create: apps/media-api/src/media_api/infrastructure/lifecycle.py
- Create: apps/media-api/src/media_api/settings/schemas.py
- Create: apps/media-api/src/media_api/settings/service.py
- Create: apps/media-api/src/media_api/settings/router.py
- Create: apps/media-api/tests/unit/settings/test_settings_service.py
- Create: apps/media-api/tests/api/test_error_contract.py
- Create: apps/media-api/tests/api/test_settings_api.py
- Create: apps/media-api/tests/api/test_events_api.py
- Create: apps/media-api/tests/integration/test_lifecycle.py
- Modify: apps/media-api/src/media_api/main.py

**Interfaces:**
- Consumes: ScanService, PersistentWorker, IngestRecoveryService, BootstrapSettings, app_settings, and processor_settings.
- Produces: SettingsService, EventBus.publish(), GET /api/events, GET/PATCH /api/settings, GET /api/system/status, and RuntimeCoordinator.

- [ ] **Step 1: Write failing settings rules**

    def test_rejects_path_change_while_operation_active(settings_service, active_operation):
        with pytest.raises(SettingsBusy):
            settings_service.update(
                RuntimeSettingsPatch(inbox_dir=active_operation.other_inbox)
            )

    def test_applies_valid_paths_and_creates_directories(settings_service, tmp_path):
        updated = settings_service.update(
            RuntimeSettingsPatch(
                inbox_dir=tmp_path / "inbox",
                library_dir=tmp_path / "library",
                work_dir=tmp_path / "work",
            )
        )
        assert updated.inbox_dir.exists()
        assert updated.library_dir.exists()
        assert updated.work_dir.exists()

The database path is visible but read-only. Runtime path changes are allowed only when there are no running processing jobs and no operations in prepared, file_committed, db_committed, or cleanup_pending.

- [ ] **Step 2: Implement persisted runtime settings and diagnostics**

Persist only overrides for inbox_dir, library_dir, work_dir, scan_interval_seconds, automatic_scan_enabled, and processor enabled/auto_run flags. Reuse BootstrapSettings path validation before writing, then call StoragePort.provision_directories(); the settings module must not create or move media directories directly. SystemStatus returns:

    class SystemStatusRead(BaseModel):
        directories_writable: bool
        ffmpeg_available: bool
        ffprobe_available: bool
        last_scan_at: datetime | None
        queued_jobs: int
        recoverable_operations: int
        recovery_in_progress: bool
        ingest_enabled: bool

- [ ] **Step 3: Write and implement the stable error contract**

    def test_domain_error_has_stable_shape(client, changed_source_item):
        response = client.post(
            f"/api/inbox/items/{changed_source_item.id}/ingest",
            headers={"Idempotency-Key": "changed"},
        )
        assert response.status_code == 409
        assert response.json() == {
            "error": {
                "code": "SOURCE_CHANGED",
                "message": "源文件已发生变化，请重新扫描。",
                "details": {"item_id": str(changed_source_item.id)},
            }
        }

Map domain exceptions in one api/errors.py registry. Do not expose tracebacks, subprocess command lines, or absolute filesystem paths in user responses. Log diagnostic detail with an operation correlation_id.

- [ ] **Step 4: Write and implement the SSE stream**

    async def test_event_stream_emits_item_change(async_client, event_bus):
        async with async_client.stream("GET", "/api/events") as response:
            await event_bus.publish(
                MediaEvent(type="inbox.updated", entity_id="item-1")
            )
            assert "event: inbox.updated" in await response.aiter_text().__anext__()

EventBus uses bounded per-subscriber asyncio.Queue instances. Slow subscribers drop invalidation events rather than blocking ingestion; the client refetches current state. Send a heartbeat comment every 15 seconds. Events contain event type, entity ID, status, and timestamp, never physical paths.

- [ ] **Step 5: Write the lifecycle ordering test**

    async def test_recovery_finishes_before_ingest_is_enabled(coordinator):
        await coordinator.start()
        assert coordinator.calls[:3] == [
            "disable_ingest",
            "recover_ingests",
            "recover_processing_jobs",
        ]
        assert coordinator.ingest_enabled is True

- [ ] **Step 6: Implement RuntimeCoordinator**

Startup order:

1. Mark recovery_in_progress and disable new ingest commands.
2. Run IngestRecoveryService.recover_all().
3. Requeue interrupted processing jobs.
4. Start one persistent worker loop.
5. Start the automatic scanner loop when enabled.
6. Enable ingest and publish system.ready.

Shutdown stops accepting work, cancels loops, waits for the current atomic checkpoint, closes event subscribers, and disposes the engine. Use FastAPI lifespan; do not start threads at import time.

- [ ] **Step 7: Add CORS only for the configured local web origin**

Allow http://127.0.0.1:3100 and http://localhost:3100 by default. Do not use wildcard origins. Make the allowed origin configurable through TRACEFOLD_MEDIA_WEB_ORIGIN.

- [ ] **Step 8: Run the complete API test suite**

Run:

    python -m pytest apps/media-api/tests -q

Expected: PASS. Real FFmpeg tests may skip only when the diagnostic reports FFmpeg unavailable.

- [ ] **Step 9: Commit**

    git add apps/media-api
    git commit -m "feat(media-api): add runtime lifecycle and settings"

---

### Task 13: Scaffold the Media Web App and Lock the API Contract

**Files:**
- Create: apps/media-web/package.json
- Create: apps/media-web/package-lock.json
- Create: apps/media-web/tsconfig.json
- Create: apps/media-web/vite.config.ts
- Create: apps/media-web/vitest.config.ts
- Create: apps/media-web/.env.example
- Create: apps/media-web/index.html
- Create: apps/media-web/src/main.tsx
- Create: apps/media-web/src/app/App.tsx
- Create: apps/media-web/src/app/router.tsx
- Create: apps/media-web/src/app/queryClient.ts
- Create: apps/media-web/src/shared/api/types.ts
- Create: apps/media-web/src/shared/api/client.ts
- Create: apps/media-web/src/styles/tokens.css
- Create: apps/media-web/src/styles/app.css
- Create: apps/media-web/tests/setup.ts
- Create: apps/media-web/tests/server.ts
- Create: apps/media-web/tests/app-shell.test.tsx
- Create: apps/media-web/tests/api-client.test.ts

**Interfaces:**
- Consumes: API JSON contracts and snake_case enum values from Tasks 4 through 12.
- Produces: apiClient, ApiError, QueryClient, App routes for /inbox, /library, and /settings, and shared TypeScript DTOs.

- [ ] **Step 1: Create the package manifest and install exact dependencies**

    {
      "name": "tracefold-media-web",
      "private": true,
      "version": "0.1.0",
      "type": "module",
      "scripts": {
        "dev": "vite --port 3100",
        "build": "tsc --noEmit && vite build",
        "test": "vitest run",
        "test:watch": "vitest",
        "e2e": "playwright test"
      },
      "dependencies": {
        "@tanstack/react-query": "5.101.4",
        "react": "19.2.8",
        "react-dom": "19.2.8",
        "react-router-dom": "7.18.2"
      },
      "devDependencies": {
        "@playwright/test": "1.62.1",
        "@testing-library/jest-dom": "7.0.0",
        "@testing-library/react": "16.3.2",
        "@testing-library/user-event": "14.6.3",
        "@types/react": "19.2.18",
        "@types/react-dom": "19.2.4",
        "@vitejs/plugin-react": "6.0.5",
        "jsdom": "30.0.1",
        "msw": "2.15.0",
        "typescript": "5.9.3",
        "vite": "8.2.1",
        "vitest": "4.1.10"
      }
    }

Run:

    npm --prefix apps/media-web install

Expected: package-lock.json is generated with no unresolved peer dependency.

- [ ] **Step 2: Configure TypeScript, Vite, and Vitest**

tsconfig.json must enable strict mode, noUncheckedIndexedAccess, JSX react-jsx, ES2022, bundler module resolution, and noEmit. Configure Vite with the React plugin. Configure Vitest with:

    export default defineConfig({
      plugins: [react()],
      test: {
        environment: "jsdom",
        globals: true,
        setupFiles: ["./tests/setup.ts"],
        restoreMocks: true
      }
    });

tests/setup.ts imports @testing-library/jest-dom/vitest and the shared MSW lifecycle.

- [ ] **Step 3: Write failing shell and API error tests**

Create the shared MSW server:

    export const server = setupServer();

    beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
    afterEach(() => server.resetHandlers());
    afterAll(() => server.close());

    test("shows only the three product destinations", () => {
      render(<App />, { wrapper: testRouter("/inbox") });
      expect(screen.getByRole("link", { name: /待入库/ })).toBeVisible();
      expect(screen.getByRole("link", { name: /文件库/ })).toBeVisible();
      expect(screen.getByRole("link", { name: /设置/ })).toBeVisible();
      expect(screen.queryByText(/Workbench|Capture|Knowledge/)).not.toBeInTheDocument();
    });

    test("preserves stable API errors", async () => {
      server.use(errorResponse("SOURCE_CHANGED", 409));
      await expect(apiClient.post("/inbox/items/1/ingest")).rejects.toMatchObject({
        code: "SOURCE_CHANGED",
        status: 409,
      });
    });

- [ ] **Step 4: Run tests and verify failure**

Run:

    npm --prefix apps/media-web test -- app-shell api-client

Expected: FAIL because App and apiClient do not exist.

- [ ] **Step 5: Define DTOs and the API client**

    export type InboxStatus =
      | "discovered"
      | "analyzing"
      | "processing"
      | "ready_to_tag"
      | "conflict"
      | "ingesting"
      | "completed"
      | "failed";

    export type InboxAction =
      | "process"
      | "retry"
      | "edit_metadata"
      | "ingest"
      | "resolve_conflict";

    export interface InboxItemRead {
      id: string;
      media_type: "image" | "video";
      status: InboxStatus;
      allowed_actions: InboxAction[];
      actual_filename: string;
      display_name: string | null;
      author_ids: string[];
      series_id: string | null;
      tag_ids: string[];
      preview_url: string;
      progress_percent: number | null;
      error: { code: string; message: string } | null;
    }

apiClient prefixes VITE_API_BASE_URL, sends JSON, parses the stable error envelope, and adds caller-provided Idempotency-Key. It never accepts filesystem paths.

- [ ] **Step 6: Implement the shell and visual tokens**

Use React Router routes /inbox, /library, and /settings with / redirecting to /inbox. App renders a compact top navigation, automatic-scan status placeholder, and an Outlet. Use CSS custom properties for canvas, surface, text, muted text, border, primary, success, warning, and danger colors. Do not port styles or markup from apps/web/src/main.ts.

- [ ] **Step 7: Run tests and production build**

Run:

    npm --prefix apps/media-web test
    npm --prefix apps/media-web run build

Expected: PASS and Vite creates apps/media-web/dist.

- [ ] **Step 8: Commit**

    git add apps/media-web
    git commit -m "feat(media-web): scaffold focused media interface"

---

### Task 14: Build the Inbox Gallery, Metadata Drawer, and Bulk Editing

**Files:**
- Create: apps/media-web/src/inbox/InboxPage.tsx
- Create: apps/media-web/src/inbox/MediaGrid.tsx
- Create: apps/media-web/src/inbox/MediaCard.tsx
- Create: apps/media-web/src/inbox/MetadataDrawer.tsx
- Create: apps/media-web/src/inbox/BulkMetadataBar.tsx
- Create: apps/media-web/src/inbox/queries.ts
- Create: apps/media-web/src/shared/components/MediaPreview.tsx
- Create: apps/media-web/src/shared/components/TaxonomyPicker.tsx
- Create: apps/media-web/tests/inbox/inbox-page.test.tsx
- Create: apps/media-web/tests/inbox/metadata-drawer.test.tsx
- Create: apps/media-web/tests/inbox/bulk-metadata.test.tsx
- Modify: apps/media-web/src/app/router.tsx
- Modify: apps/media-web/src/styles/app.css

**Interfaces:**
- Consumes: Inbox, scan, metadata, taxonomy, process, and ingest endpoints.
- Produces: InboxPage, selected item state, status cards, MetadataDrawer, and BulkMetadataBar.

- [ ] **Step 1: Write failing gallery behavior tests**

    test("refreshes and renders server-owned statuses", async () => {
      renderInbox();
      await user.click(screen.getByRole("button", { name: "手动刷新" }));
      expect(await screen.findByText("旅行记录_杭州_01.mp4")).toBeVisible();
      expect(screen.getByText("待标注")).toBeVisible();
      expect(screen.getByText("合并中 68%")).toBeVisible();
      expect(screen.getByText("处理失败")).toBeVisible();
    });

    test("does not show ingest when server disallows it", async () => {
      renderInbox({ selected: processingItem });
      expect(screen.queryByRole("button", { name: "正式入库" })).not.toBeInTheDocument();
    });

- [ ] **Step 2: Run inbox tests and verify failure**

Run:

    npm --prefix apps/media-web test -- inbox-page

Expected: FAIL because InboxPage is missing.

- [ ] **Step 3: Implement list queries and gallery selection**

InboxPage owns search, status filter, media type filter, selected IDs, and selected primary ID. queries.ts defines query keys:

    export const inboxKeys = {
      all: ["inbox"] as const,
      list: (filters: InboxFilters) => ["inbox", "list", filters] as const,
      item: (id: string) => ["inbox", "item", id] as const,
    };

Manual refresh first POSTs /inbox/scan, then invalidates inbox list queries. Cards render preview, actual filename, state label, duration when present, progress, and concise error. Card actions are derived only from allowed_actions.

- [ ] **Step 4: Write failing metadata drawer tests**

    test("edits display metadata but keeps actual filename read only", async () => {
      renderDrawer(readyItem);
      expect(screen.getByLabelText("实际文件名")).toHaveAttribute("readonly");
      await user.clear(screen.getByLabelText("显示名称"));
      await user.type(screen.getByLabelText("显示名称"), "杭州夜游");
      await user.selectOptions(screen.getByLabelText("系列"), "series-1");
      await user.click(screen.getByRole("button", { name: "保存资料" }));
      expect(lastMetadataPatch()).toEqual({
        display_name: "杭州夜游",
        author_ids: readyItem.author_ids,
        series_id: "series-1",
        tag_ids: readyItem.tag_ids,
      });
    });

- [ ] **Step 5: Implement TaxonomyPicker and MetadataDrawer**

TaxonomyPicker supports search, create-or-select, multiple values for authors and tags, and one nullable value for series. MetadataDrawer shows preview, actual filename read-only, display name, authors, series, tags, process summary, error and retry when allowed, and formal-ingest action when allowed. Generate one crypto.randomUUID idempotency key per user command and keep it stable while the mutation retries.

If ingest returns kind conflict, store conflict_id in route state for Task 15; do not guess a conflict resolution.

- [ ] **Step 6: Write and implement bulk metadata tests**

    test("bulk edit appends authors and tags and explicitly replaces series", async () => {
      renderBulkBar(["item-1", "item-2"]);
      await chooseAuthor("author-1");
      await chooseTag("tag-1");
      await chooseSeries("series-1");
      await user.click(screen.getByRole("button", { name: "应用到 2 项" }));
      expect(lastBulkPatch()).toEqual({
        item_ids: ["item-1", "item-2"],
        append_author_ids: ["author-1"],
        append_tag_ids: ["tag-1"],
        replace_series: true,
        replace_series_id: "series-1",
      });
    });

The bulk bar displays “系列将替换所选文件的现有系列” before submission. Disable bulk submit when any selected item lacks edit_metadata in allowed_actions.

- [ ] **Step 7: Run inbox tests and build**

Run:

    npm --prefix apps/media-web test -- inbox
    npm --prefix apps/media-web run build

Expected: PASS.

- [ ] **Step 8: Commit**

    git add apps/media-web
    git commit -m "feat(media-web): add inbox gallery and tagging"

---

### Task 15: Add the Windows-Style Conflict Dialog

**Files:**
- Create: apps/media-web/src/conflicts/ConflictDialog.tsx
- Create: apps/media-web/tests/conflicts/conflict-dialog.test.tsx
- Modify: apps/media-web/src/inbox/MetadataDrawer.tsx
- Modify: apps/media-web/src/shared/api/types.ts
- Modify: apps/media-web/src/styles/app.css

**Interfaces:**
- Consumes: ConflictRead and POST /api/conflicts/{id}/resolve.
- Produces: ConflictDialog and the skip, keep_both, and replace flows.

- [ ] **Step 1: Write failing side-by-side and choice tests**

    test("shows both files before offering choices", async () => {
      renderConflict(nameAndHashConflict);
      expect(screen.getByRole("img", { name: "文件库中的文件" })).toBeVisible();
      expect(screen.getByRole("img", { name: "本次待入库文件" })).toBeVisible();
      expect(screen.getByText(nameAndHashConflict.existing.actual_filename)).toBeVisible();
      expect(screen.getByText(nameAndHashConflict.incoming.actual_filename)).toBeVisible();
      expect(screen.getByRole("button", { name: "跳过" })).toBeVisible();
      expect(screen.getByRole("button", { name: "两者都保留" })).toBeVisible();
      expect(screen.getByRole("button", { name: "替换原文件" })).toBeVisible();
    });

    test.each([
      ["跳过", "skip"],
      ["两者都保留", "keep_both"],
      ["替换原文件", "replace"],
    ])("submits %s as %s", async (label, resolution) => {
      renderConflict(nameConflict);
      await user.click(screen.getByRole("button", { name: label }));
      if (resolution === "replace") {
        await user.click(screen.getByRole("button", { name: "确认替换" }));
      }
      expect(lastResolution()).toBe(resolution);
    });

- [ ] **Step 2: Run conflict UI tests and verify failure**

Run:

    npm --prefix apps/media-web test -- conflict-dialog

Expected: FAIL because ConflictDialog is missing.

- [ ] **Step 3: Implement the dialog**

Render two equal comparison panels containing preview, actual filename, size, modified time, media information, display name, authors, series, and tags. Show conflict kinds as plain Chinese descriptions. Replace requires a second confirmation explaining that the existing file and metadata will be replaced; skip and keep-both require one click.

After success:

- skip closes the dialog and keeps the item selected in ready_to_tag.
- keep_both or replace closes the drawer, removes the completed inbox item from the list, and invalidates library queries.
- CONFLICT_CHOICE_EXPIRED closes the dialog, refetches the item, and shows “源文件已变化，请重新选择。”

- [ ] **Step 4: Verify keyboard and focus behavior**

Add tests that focus enters the dialog, Tab stays within it, Escape closes only before a mutation starts, and focus returns to the triggering button. Give previews useful alt text and buttons visible focus styles.

- [ ] **Step 5: Run conflict tests and build**

Run:

    npm --prefix apps/media-web test -- conflicts
    npm --prefix apps/media-web run build

Expected: PASS.

- [ ] **Step 6: Commit**

    git add apps/media-web
    git commit -m "feat(media-web): add conflict comparison flow"

---

### Task 16: Build the Formal Library, Settings, and Live Invalidation

**Files:**
- Create: apps/media-web/src/library/LibraryPage.tsx
- Create: apps/media-web/src/library/LibraryFilters.tsx
- Create: apps/media-web/src/library/AssetDrawer.tsx
- Create: apps/media-web/src/settings/SettingsPage.tsx
- Create: apps/media-web/src/shared/events/useMediaEvents.ts
- Create: apps/media-web/tests/library/library-page.test.tsx
- Create: apps/media-web/tests/library/asset-drawer.test.tsx
- Create: apps/media-web/tests/settings/settings-page.test.tsx
- Create: apps/media-web/tests/events.test.tsx
- Modify: apps/media-web/src/app/App.tsx
- Modify: apps/media-web/src/app/router.tsx
- Modify: apps/media-web/src/shared/api/types.ts
- Modify: apps/media-web/src/styles/app.css

**Interfaces:**
- Consumes: filtered library API, asset metadata/reveal endpoints, settings/system status, and GET /api/events.
- Produces: LibraryPage, AssetDrawer, SettingsPage, and useMediaEvents.

- [ ] **Step 1: Write failing library filter tests**

    test("combines search and taxonomy filters", async () => {
      renderLibrary();
      await user.type(screen.getByRole("searchbox"), "杭州");
      await chooseFilter("作者", "林同学");
      await chooseFilter("系列", "城市漫游");
      await chooseFilter("标签", "夜景");
      await chooseFilter("类型", "视频");
      expect(lastLibraryQuery()).toEqual({
        search: "杭州",
        author_id: "author-1",
        series_id: "series-1",
        tag_id: "tag-1",
        media_type: "video",
      });
    });

    test("falls back to actual filename when display name is empty", async () => {
      renderLibrary({ assets: [assetWithoutDisplayName] });
      expect(await screen.findByText(assetWithoutDisplayName.actual_filename)).toBeVisible();
    });

- [ ] **Step 2: Implement library gallery and asset drawer**

LibraryPage uses a gallery matching InboxPage but has no workflow statuses. AssetDrawer allows display_name, authors, one series, and tags. actual_filename and relative path are read-only. “打开所在目录” calls the reveal endpoint; it does not receive or construct a path. Invalidate the item and list query after metadata save.

- [ ] **Step 3: Write failing settings tests**

    test("shows diagnostics and saves valid runtime paths", async () => {
      renderSettings();
      expect(await screen.findByText("FFmpeg 可用")).toBeVisible();
      await user.clear(screen.getByLabelText("待入库目录"));
      await user.type(screen.getByLabelText("待入库目录"), "T:\\Media\\Inbox");
      await user.click(screen.getByRole("button", { name: "保存设置" }));
      expect(lastSettingsPatch().inbox_dir).toBe("T:\\Media\\Inbox");
    });

    test("explains why path changes are temporarily blocked", async () => {
      server.use(settingsBusyResponse());
      renderSettings();
      await user.click(screen.getByRole("button", { name: "保存设置" }));
      expect(await screen.findByText(/存在正在处理或待恢复的文件/)).toBeVisible();
    });

- [ ] **Step 4: Implement settings and system status**

Render inbox, library, and work directory inputs; database path read-only; automatic scan toggle and interval; processor enabled and auto-run toggles; FFmpeg/ffprobe status; last scan; queued jobs; recoverable operations; and recovery banner. Disable “正式入库” globally while ingest_enabled is false, but keep library browsing available.

- [ ] **Step 5: Write and implement live invalidation**

    test("refetches affected queries after an SSE event", async () => {
      renderApp();
      emitEvent({ type: "inbox.updated", entity_id: "item-1" });
      expect(invalidatedQueries()).toContainEqual(["inbox", "item", "item-1"]);
      emitEvent({ type: "library.updated", entity_id: "asset-1" });
      expect(invalidatedQueries()).toContainEqual(["library"]);
    });

useMediaEvents creates one EventSource at the application shell. It invalidates queries by event type, shows disconnected status without blocking manual refresh, and reconnects using browser EventSource behavior. Do not copy server state into a second client store.

- [ ] **Step 6: Run all frontend tests and build**

Run:

    npm --prefix apps/media-web test
    npm --prefix apps/media-web run build

Expected: PASS.

- [ ] **Step 7: Commit**

    git add apps/media-web
    git commit -m "feat(media-web): add library settings and live updates"

---

### Task 17: Add End-to-End Acceptance, Operating Docs, and Cutover Gate

**Files:**
- Create: apps/media-api/scripts/run_e2e_api.py
- Create: apps/media-web/playwright.config.ts
- Create: apps/media-web/e2e/media-library.spec.ts
- Create: apps/media-api/README.md
- Create: apps/media-web/README.md
- Create: docs/media-library-operations.md
- Modify: apps/media-web/package.json

**Interfaces:**
- Consumes: complete media API and web application.
- Produces: isolated E2E runner, repeatable acceptance command, operator instructions, and an explicit old-system cutover checklist.

- [ ] **Step 1: Create the safe E2E API runner**

run_e2e_api.py resolves apps/media-web/.e2e-data, asserts that the resolved path ends with apps/media-web/.e2e-data, then removes only its inbox, library, work, and media.sqlite3 children. It runs Alembic against that database and starts the app on 127.0.0.1:8101 with automatic scanning enabled at two seconds.

Never use HOME, a workspace root, a glob, or an unresolved environment variable as a cleanup target.

- [ ] **Step 2: Configure Playwright web servers**

    export default defineConfig({
      testDir: "./e2e",
      use: { baseURL: "http://127.0.0.1:3101" },
      webServer: [
        {
          command: "python ../media-api/scripts/run_e2e_api.py",
          url: "http://127.0.0.1:8101/health",
          reuseExistingServer: false,
          env: {
            TRACEFOLD_MEDIA_WEB_ORIGIN: "http://127.0.0.1:3101"
          }
        },
        {
          command: "npm run dev -- --port 3101",
          url: "http://127.0.0.1:3101",
          reuseExistingServer: false,
          env: {
            VITE_API_BASE_URL: "http://127.0.0.1:8101/api"
          }
        }
      ]
    });

Set VITE_API_BASE_URL=http://127.0.0.1:8101/api for the E2E web server and TRACEFOLD_MEDIA_WEB_ORIGIN=http://127.0.0.1:3101 for the API runner.

- [ ] **Step 3: Write the failing ordinary-media acceptance test**

    test("scans, tags, ingests, and finds an image", async ({ page }) => {
      await copyFixtureToInbox("photo.jpg");
      await page.goto("/inbox");
      await page.getByRole("button", { name: "手动刷新" }).click();
      await page.getByRole("button", { name: "手动刷新" }).click();
      await page.getByText("photo.jpg").click();
      await page.getByLabel("显示名称").fill("杭州夜景");
      await selectTaxonomy(page, "作者", "林同学");
      await selectTaxonomy(page, "系列", "城市漫游");
      await selectTaxonomy(page, "内容标签", "夜景");
      await page.getByRole("button", { name: "正式入库" }).click();
      await expect(page.getByText("photo.jpg")).not.toBeVisible();
      await page.getByRole("link", { name: /文件库/ }).click();
      await expect(page.getByText("杭州夜景")).toBeVisible();
      await expectSourceAbsent("photo.jpg");
      await expectLibraryFile("photo.jpg");
    });

Run:

    npm --prefix apps/media-web run e2e

Expected before runner and fixtures are complete: FAIL. Do not weaken assertions to make it pass.

- [ ] **Step 4: Add preprocessing and conflict acceptance tests**

Add scenarios that:

- copy paired.mp4 and paired.m4a, wait for processing completion, ingest, verify only one formal MP4, and verify both sources are gone.
- preseed a formal file with the same name, trigger conflict, verify both previews, choose keep both, and assert the physical result uses the (2) suffix.
- trigger another conflict, choose skip, and assert the inbox source remains.
- trigger replacement, confirm twice, and assert the library shows incoming metadata with one asset.
- make a processor input invalid, verify only that card shows failed and a separate image still ingests.

- [ ] **Step 5: Run the full verification matrix**

Run:

    python -m pytest apps/media-api/tests -q
    npm --prefix apps/media-web test
    npm --prefix apps/media-web run build
    npm --prefix apps/media-web run e2e

Expected: all commands PASS. FFmpeg-dependent backend and E2E cases are required for release acceptance; a local skip is not sufficient for cutover.

- [ ] **Step 6: Write operating documentation**

apps/media-api/README.md documents environment creation, editable install, Alembic upgrade, API start command, FFmpeg requirement, test command, and the four runtime paths.

apps/media-web/README.md documents install, development server, build, unit tests, E2E tests, and VITE_API_BASE_URL.

docs/media-library-operations.md documents:

1. Backup or use copies of real media for acceptance.
2. Configure and validate non-overlapping directories.
3. Start API and web app.
4. Verify image, ordinary video, paired MP4/M4A, all conflict choices, restart recovery, and library search.
5. Confirm old apps still start and were not modified.
6. Freeze old applications only after every acceptance item passes.
7. Handle old-code deletion, archive cleanup, and entry-point switching in a separate reviewed plan and commit.

- [ ] **Step 7: Commit**

    git add apps/media-api apps/media-web docs/media-library-operations.md
    git commit -m "test(media): add end-to-end acceptance gate"

---

## Spec Coverage Matrix

| Approved requirement | Implemented by |
| --- | --- |
| Isolated apps and new empty database | Tasks 1–2 |
| Validated inbox, library, work, and database paths | Tasks 1 and 12 |
| Shared image/video metadata, multi-author, one series, multi-tag | Tasks 2 and 5 |
| Stable idempotent scanning and temporary-download filtering | Task 4 |
| Extensible processors and persistent worker | Task 8 |
| MP4/M4A detection, merge, validation, and cleanup | Task 9 |
| Immutable actual filename and editable display name | Tasks 5, 6, 14, and 16 |
| Three-stage ingest and source deletion after commit | Tasks 6–7 |
| Startup recovery and cleanup retry | Tasks 7 and 12 |
| Name/hash conflicts and three user choices | Tasks 10 and 15 |
| Controlled previews and reveal-in-folder | Task 11 |
| Search and taxonomy filtering | Tasks 11 and 16 |
| Server-owned state, allowed actions, SSE invalidation | Tasks 2, 12, 14, and 16 |
| Gallery plus right-side metadata drawer | Task 14 |
| Settings and diagnostics | Tasks 12 and 16 |
| Unit, integration, API, component, and E2E coverage | Tasks 1–17 |
| Old system remains untouched until separate cutover | Global Constraints and Task 17 |

## Execution Checkpoints

- After Task 6: a direct image or video can complete the full API-only inbox-to-library flow.
- After Task 9: paired MP4/M4A files complete the same flow through the processor registry.
- After Task 12: the API is recoverable, observable, and usable as a standalone local service.
- After Task 14: the primary user workflow is usable in the new web interface.
- After Task 16: the complete approved interface is present.
- After Task 17: the release acceptance gate decides whether old applications may be frozen.

Do not delete or archive the old applications while executing this plan. That work begins only after Task 17 passes and receives a separate design and implementation plan.
