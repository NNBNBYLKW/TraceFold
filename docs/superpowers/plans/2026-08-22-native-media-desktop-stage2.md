# Native Media Desktop Stage 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first usable native media-library slice: discover stable ordinary images and videos, edit optional metadata, safely ingest non-conflicting files, and browse/edit the formal library.

**Architecture:** Keep the existing Desktop → Application → Domain and Desktop → Infrastructure dependency direction. Domain owns state and metadata invariants, Application owns use cases and ports, Infrastructure owns SQLite and controlled filesystem operations, and WinUI ViewModels only invoke Application services.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, xUnit v3.

**Spec:** docs/superpowers/specs/2026-08-22-native-media-desktop-stage2-design.md

## Global Constraints

- Execute in an isolated codex/native-media-desktop-stage2 worktree created with superpowers:using-git-worktrees.
- Target Windows 11 x64; keep TraceFold.Media.Desktop as an unpackaged, self-contained WinUI executable.
- Do not add Python, FastAPI, React, Node.js, a browser host, FFmpeg, FFprobe, network access, or external plugin loading.
- Do not run git pull, browse, or download dependencies during execution. Restore only from already available local NuGet/tool caches with failed remote sources ignored; if a pinned package is absent locally, stop and report it instead of fetching it.
- Scan only direct children of the configured inbox. Ignore directories, reparse points, .part, .crdownload, .tmp, and unsupported extensions without modifying them.
- Direct image extensions are .jpg, .jpeg, .png, .webp, and .gif; direct video extensions are .mp4, .mov, .mkv, and .webm; matching is case-insensitive.
- Automatic scanning defaults off. When enabled, it uses the same single-flight scan use case every 5 seconds.
- Metadata is optional: many authors, zero or one series, many tags, and an optional display name that never changes the physical filename.
- MP4/M4A pairs become RequiresProcessing and cannot ingest in Stage 2.
- Name or registered-content conflicts become ConflictBlocked. Do not auto-rename, replace, skip, or delete either file.
- Never delete the only source before a verified formal file and committed database asset exist.
- All automated filesystem tests use test-owned temporary roots. Never read, move, rename, or delete a user-configured media directory.
- Every task follows red-green-refactor, ends with focused tests, and makes one reviewable commit.

## Before Task 1

- [ ] Confirm the execution checkout is the isolated feature worktree and starts clean:

~~~powershell
git status --short --branch
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
~~~

- [ ] Restore from available local caches and prove the Stage 1 baseline before changing code:

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: Release build reports 0 warnings and 0 errors; the existing 32 tests pass. If a pinned dependency is missing locally or the baseline fails, stop and report the exact failure before editing.

## File and responsibility map

- Domain/Inbox: state machine, allowed actions, source fingerprints.
- Domain/Metadata: normalized taxonomy names and metadata cardinality.
- Domain/Ingest: durable ordinary-ingest state transitions.
- Application/Intake: candidate grouping, scan reconciliation, stability commands, queries.
- Application/Metadata: draft/formal metadata and taxonomy use cases.
- Application/Ingest: ordinary ingest orchestration and startup reconciliation.
- Application/Preview: ID-based controlled preview descriptors.
- Infrastructure/Persistence: EF rows, migrations, and repository implementations.
- Infrastructure/Filesystem: direct-child enumeration, hashes, staging, commit, and guarded cleanup.
- Infrastructure/Workers: candidate stability and optional automatic scanning.
- Desktop/Pages and Desktop/Controls: WinUI presentation and ViewModels only.

---

### Task 1: Add the domain state and metadata invariants

**Files:**
- Create: apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj
- Create: apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Inbox/InboxItemTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Metadata/MetadataSelectionTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Ingest/IngestOperationTests.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Inbox/FileFingerprint.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxAction.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItemState.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItem.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Metadata/TaxonomyName.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Metadata/MetadataSelection.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Ingest/IngestOperationStage.cs
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Ingest/IngestOperation.cs
- Modify: apps/media-desktop/TraceFold.MediaDesktop.slnx

**Interfaces:**
- Produces: InboxItem.Discover(Guid), InboxItem.MarkAnalyzing(), MarkReady(), MarkRequiresProcessing(string), BeginIngest(), BlockConflict(string), MarkCleanupPending(), Complete(), Fail(string, bool), MarkSourceMissing(), and ResetForSourceChange().
- Produces: InboxItem.State, InboxItem.RequiredCapability, InboxItem.ErrorCode, InboxItem.IsRetryable, and InboxItem.AllowedActions.
- Produces: FileFingerprint(long Size, DateTimeOffset LastWriteTimeUtc).
- Produces: TaxonomyName.Create(string) with Display and Normalized.
- Produces: MetadataSelection.Create(string?, IEnumerable<Guid>, Guid?, IEnumerable<Guid>).
- Produces: IngestOperation(Guid id, Guid inboxItemId) with AdvanceTo(IngestOperationStage).

- [ ] **Step 1: Add the domain test project and failing state-machine tests**

Add the project to the tests folder in the solution and reference only TraceFold.Media.Domain:

~~~xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/TraceFold.Media.Domain/TraceFold.Media.Domain.csproj" />
  </ItemGroup>
</Project>
~~~

~~~csharp
[Fact]
public void Ready_item_allows_metadata_preview_and_ingest()
{
    InboxItem item = InboxItem.Discover(Guid.NewGuid());
    item.MarkAnalyzing();
    item.MarkReady();

    Assert.Equal(InboxItemState.ReadyToTag, item.State);
    Assert.True(item.AllowedActions.HasFlag(InboxAction.EditMetadata));
    Assert.True(item.AllowedActions.HasFlag(InboxAction.Preview));
    Assert.True(item.AllowedActions.HasFlag(InboxAction.Ingest));
}

[Fact]
public void Requires_processing_never_allows_ingest()
{
    InboxItem item = InboxItem.Discover(Guid.NewGuid());
    item.MarkAnalyzing();
    item.MarkRequiresProcessing("mp4_m4a_merge");

    Assert.Equal(InboxItemState.RequiresProcessing, item.State);
    Assert.Equal("mp4_m4a_merge", item.RequiredCapability);
    Assert.False(item.AllowedActions.HasFlag(InboxAction.Ingest));
}

[Fact]
public void Source_change_preserves_identity_but_returns_to_waiting()
{
    InboxItem item = InboxItem.Discover(Guid.NewGuid());
    item.MarkAnalyzing();
    item.MarkReady();

    item.ResetForSourceChange();

    Assert.Equal(InboxItemState.WaitingForStability, item.State);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release
~~~

Expected: FAIL because the Domain/Inbox types do not exist.

- [ ] **Step 2: Add failing metadata and ingest-operation tests**

~~~csharp
[Fact]
public void Metadata_is_optional_and_deduplicates_relations()
{
    Guid author = Guid.NewGuid();
    Guid tag = Guid.NewGuid();

    MetadataSelection selection = MetadataSelection.Create(
        "  ",
        [author, author],
        null,
        [tag, tag]);

    Assert.Null(selection.DisplayName);
    Assert.Equal([author], selection.AuthorIds);
    Assert.Null(selection.SeriesId);
    Assert.Equal([tag], selection.TagIds);
}

[Theory]
[InlineData(" Alice ", "Alice", "ALICE")]
[InlineData("Ａlice", "Ａlice", "ALICE")]
public void Taxonomy_name_uses_trimmed_nfkc_casefolded_key(
    string input,
    string display,
    string normalized)
{
    TaxonomyName value = TaxonomyName.Create(input);
    Assert.Equal(display, value.Display);
    Assert.Equal(normalized, value.Normalized);
}

[Fact]
public void Ingest_operation_rejects_skipping_durable_stages()
{
    var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());
    Assert.Throws<InvalidOperationException>(
        () => operation.AdvanceTo(IngestOperationStage.DatabaseCommitted));
}
~~~

Run the Domain test project again.

Expected: FAIL because MetadataSelection, TaxonomyName, and IngestOperation do not exist.

- [ ] **Step 3: Implement the domain types with explicit transitions**

Use flags for UI-independent allowed actions:

~~~csharp
[Flags]
public enum InboxAction
{
    None = 0,
    Preview = 1,
    EditMetadata = 2,
    Ingest = 4,
    Retry = 8,
    ClearMissing = 16,
    OpenFolder = 32,
    RetryCleanup = 64
}
~~~

InboxItemState must contain exactly:

~~~csharp
public enum InboxItemState
{
    WaitingForStability,
    Analyzing,
    ReadyToTag,
    RequiresProcessing,
    Ingesting,
    ConflictBlocked,
    CleanupPending,
    Completed,
    Failed,
    SourceMissing
}
~~~

Implement InboxItem methods through one private transition guard. Required action mapping:

~~~csharp
public InboxAction AllowedActions => State switch
{
    InboxItemState.WaitingForStability => InboxAction.Preview,
    InboxItemState.ReadyToTag =>
        InboxAction.Preview | InboxAction.EditMetadata |
        InboxAction.Ingest | InboxAction.OpenFolder,
    InboxItemState.RequiresProcessing =>
        InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder,
    InboxItemState.ConflictBlocked =>
        InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder,
    InboxItemState.CleanupPending =>
        InboxAction.Preview | InboxAction.RetryCleanup | InboxAction.OpenFolder,
    InboxItemState.Failed when IsRetryable =>
        InboxAction.Preview | InboxAction.Retry | InboxAction.OpenFolder,
    InboxItemState.SourceMissing => InboxAction.ClearMissing,
    _ => InboxAction.None
};
~~~

TaxonomyName.Create must trim the display value, reject empty input, normalize with NormalizationForm.FormKC, and call ToUpperInvariant for the unique key. MetadataSelection.Create must trim display name to null and preserve first-occurrence order while removing duplicate IDs.

IngestOperation.AdvanceTo must allow only:

~~~text
Created -> Staged -> FileCommitted -> DatabaseCommitted
DatabaseCommitted -> CleanupPending -> Completed
DatabaseCommitted -> Completed
Any non-Completed state -> Failed
~~~

- [ ] **Step 4: Run domain and architecture tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release
~~~

Expected: all tests PASS and Domain still references no other TraceFold project.

- [ ] **Step 5: Commit**

~~~powershell
git add apps/media-desktop/TraceFold.MediaDesktop.slnx apps/media-desktop/src/TraceFold.Media.Domain apps/media-desktop/tests/TraceFold.Media.Domain.Tests
git commit -m "feat(media-desktop): define stage-two domain states"
~~~

---

### Task 2: Define extensible candidate grouping

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/MediaKind.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/ObservedFile.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/CandidateGroup.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/IInboxGroupingRule.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/CandidateGroupingService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/DirectMediaGroupingRule.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/Mp4M4aGroupingRule.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/CandidateGroupingServiceTests.cs

**Interfaces:**
- Consumes: FileFingerprint from Task 1.
- Produces: MediaKind.Image and MediaKind.Video.
- Produces: CandidateDisposition.WaitingForCompanion, Direct, and RequiresProcessing.
- Produces: ObservedFile(string FullPath, string Filename, string Extension, FileFingerprint Fingerprint, DateTimeOffset FirstObservedAtUtc).
- Produces: CandidateGroup(string RuleName, string GroupKey, MediaKind MediaKind, CandidateDisposition Disposition, IReadOnlyList<ObservedFile> Sources, string? RequiredCapability).
- Produces: IInboxGroupingRule.Name, Priority, ObservedExtensions, and Group(IReadOnlyList<ObservedFile>, DateTimeOffset).
- Produces: CandidateGroupingService.ObservedExtensions and Group(IReadOnlyList<ObservedFile> files, DateTimeOffset observedAtUtc).

- [ ] **Step 1: Write failing grouping tests**

~~~csharp
[Fact]
public void Existing_mp4_m4a_pair_is_one_processing_item()
{
    DateTimeOffset now = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
    ObservedFile mp4 = File("C:/inbox/clip.mp4", now);
    ObservedFile m4a = File("C:/inbox/CLIP.M4A", now);
    var service = CreateService();

    CandidateGroup group = Assert.Single(service.Group([mp4, m4a], now));

    Assert.Equal(CandidateDisposition.RequiresProcessing, group.Disposition);
    Assert.Equal("mp4_m4a_merge", group.RequiredCapability);
    Assert.Equal(2, group.Sources.Count);
}

[Fact]
public void Lone_mp4_waits_for_companion_for_ten_seconds()
{
    DateTimeOffset firstSeen = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
    var service = CreateService();

    CandidateGroup group = Assert.Single(
        service.Group([File("C:/inbox/clip.mp4", firstSeen)], firstSeen.AddSeconds(9)));

    Assert.Equal(CandidateDisposition.WaitingForCompanion, group.Disposition);
}

[Fact]
public void Lone_mp4_becomes_direct_after_companion_window()
{
    DateTimeOffset firstSeen = DateTimeOffset.Parse("2026-08-22T10:00:00Z");
    var service = CreateService();

    CandidateGroup group = Assert.Single(
        service.Group([File("C:/inbox/clip.mp4", firstSeen)], firstSeen.AddSeconds(10)));

    Assert.Equal(CandidateDisposition.Direct, group.Disposition);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~CandidateGroupingServiceTests
~~~

Expected: FAIL because the intake grouping types do not exist.

- [ ] **Step 2: Implement the grouping contracts**

Use exact extension sets:

~~~csharp
public static readonly IReadOnlySet<string> ImageExtensions =
    new HashSet<string>([".jpg", ".jpeg", ".png", ".webp", ".gif"],
        StringComparer.OrdinalIgnoreCase);

public static readonly IReadOnlySet<string> VideoExtensions =
    new HashSet<string>([".mp4", ".mov", ".mkv", ".webm"],
        StringComparer.OrdinalIgnoreCase);
~~~

CandidateGroupingService sorts rules by descending Priority, passes only unclaimed files to each rule, and rejects a rule result that claims the same full path twice. Register Mp4M4aGroupingRule at priority 100 and DirectMediaGroupingRule at priority 0.

Mp4M4aGroupingRule behavior:

~~~text
same case-insensitive basename MP4 + M4A -> RequiresProcessing
lone MP4 before FirstObservedAtUtc + 10 seconds -> WaitingForCompanion
lone MP4 at or after FirstObservedAtUtc + 10 seconds -> Direct
lone M4A -> no group
~~~

DirectMediaGroupingRule creates one Direct group per unclaimed supported image or video. It never recognizes M4A.

- [ ] **Step 3: Add edge-case tests and run them**

Add tests proving:

- uppercase supported extensions are accepted;
- .part, .crdownload, and .tmp are not declared observed extensions;
- a lone M4A creates no item;
- two files with different base names are not paired;
- a pair is claimed before the direct-media rule.

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~CandidateGroupingServiceTests
~~~

Expected: all grouping tests PASS.

- [ ] **Step 4: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Intake apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake
git commit -m "feat(media-desktop): classify inbox candidate groups"
~~~

---

### Task 3: Add guarded top-level filesystem discovery

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/IInboxFileCatalog.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledPathGuard.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/LocalInboxFileCatalog.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledPathGuardTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/LocalInboxFileCatalogTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Consumes: FileFingerprint and CandidateGroupingService.ObservedExtensions.
- Produces: IInboxFileCatalog.EnumerateTopLevelAsync(string inboxDirectory, IReadOnlySet<string> observedExtensions, CancellationToken).
- Produces: ControlledPathGuard.RequireDirectChild(string root, string candidate) and RequireDescendant(string root, string candidate), each returning a canonical path or throwing ControlledPathException.

- [ ] **Step 1: Write failing non-recursive and ignore tests**

~~~csharp
[Fact]
public async Task EnumerateTopLevelAsync_returns_only_supported_direct_children()
{
    using var temp = TestDirectory.Create();
    string inbox = Directory.CreateDirectory(Path.Combine(temp.Path, "inbox")).FullName;
    File.WriteAllBytes(Path.Combine(inbox, "photo.JPG"), [1]);
    File.WriteAllBytes(Path.Combine(inbox, "clip.mp4.part"), [2]);
    File.WriteAllBytes(Path.Combine(inbox, "notes.txt"), [3]);
    string nested = Directory.CreateDirectory(Path.Combine(inbox, "nested")).FullName;
    File.WriteAllBytes(Path.Combine(nested, "hidden.png"), [4]);
    var catalog = new LocalInboxFileCatalog();

    IReadOnlyList<FileSnapshot> result = await catalog.EnumerateTopLevelAsync(
        inbox,
        new HashSet<string>([".jpg", ".png", ".mp4"], StringComparer.OrdinalIgnoreCase),
        CancellationToken.None);

    FileSnapshot file = Assert.Single(result);
    Assert.Equal("photo.JPG", file.Filename);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~LocalInboxFileCatalogTests
~~~

Expected: FAIL because IInboxFileCatalog and LocalInboxFileCatalog do not exist.

- [ ] **Step 2: Write failing direct-child guard tests**

~~~csharp
[Fact]
public void RequireDirectChild_rejects_nested_and_outside_paths()
{
    string root = Path.GetFullPath("C:/media/inbox");

    Assert.Throws<ControlledPathException>(() =>
        ControlledPathGuard.RequireDirectChild(root, "C:/media/inbox/nested/photo.jpg"));
    Assert.Throws<ControlledPathException>(() =>
        ControlledPathGuard.RequireDirectChild(root, "C:/media/outside.jpg"));
}
~~~

Also test that a direct child with different Windows path casing is accepted and canonicalized.

Add RequireDescendant tests proving that WorkDirectory/.tracefold/ingest/{id}/payload is accepted while sibling and .. paths are rejected.

- [ ] **Step 3: Implement guarded enumeration**

IInboxFileCatalog returns this application record:

~~~csharp
public sealed record FileSnapshot(
    string FullPath,
    string Filename,
    string Extension,
    FileFingerprint Fingerprint);
~~~

LocalInboxFileCatalog must:

1. Canonicalize the configured inbox once.
2. Use Directory.EnumerateFiles with SearchOption.TopDirectoryOnly.
3. Call File.GetAttributes and skip FileAttributes.ReparsePoint.
4. Skip names ending in .part, .crdownload, or .tmp, case-insensitively.
5. Skip extensions outside observedExtensions.
6. Call ControlledPathGuard.RequireDirectChild before reading FileInfo.
7. Return snapshots ordered by Filename with StringComparer.OrdinalIgnoreCase.
8. Observe cancellation between entries.

Do not catch UnauthorizedAccessException per file and pretend the scan succeeded. Convert filesystem exceptions into an IntakeFileAccessException carrying a stable error code and the configured root.

- [ ] **Step 4: Register and run filesystem tests**

Register IInboxFileCatalog as LocalInboxFileCatalog in AddMediaInfrastructure. Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~LocalInboxFileCatalogTests|FullyQualifiedName~ControlledPathGuardTests"
~~~

Expected: all focused tests PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Intake/IInboxFileCatalog.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem
git commit -m "feat(media-desktop): enumerate guarded inbox files"
~~~

---

### Task 4: Create the Stage 2 SQLite schema

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/InboxItemRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/SourceFileRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MetadataDraftRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/TaxonomyTermRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/InboxDraftAuthorRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/InboxDraftTagRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaAssetRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaAssetAuthorRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaAssetTagRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/IngestOperationRow.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260822090000_Stage2MediaLibrary.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260822090000_Stage2MediaLibrary.Designer.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/Stage2MigrationTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/AppSettingsRow.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/MediaDesktopDbContextModelSnapshot.cs

**Interfaces:**
- Consumes: all Task 1 enum values as strings at the persistence boundary.
- Produces: DbSets for AppSettings, InboxItems, SourceFiles, MetadataDrafts, TaxonomyTerms, MediaAssets, and IngestOperations.
- Produces: a migration from 20260821121109_InitialDesktopSettings that preserves the existing app_settings row.

- [ ] **Step 1: Write a failing migration-preservation test**

Use IMigrator to migrate only to 20260821121109_InitialDesktopSettings, insert the three saved directories, then migrate to latest:

~~~csharp
[Fact]
public async Task Stage2_migration_preserves_stage1_directories_and_defaults_auto_scan_off()
{
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await database.MigrateAsync("20260821121109_InitialDesktopSettings");
    await database.InsertStage1SettingsAsync("C:/inbox", "C:/library", "C:/work");

    await database.MigrateAsync();

    SavedSettings row = await database.ReadSettingsAsync();
    Assert.Equal("C:/inbox", row.InboxDirectory);
    Assert.False(row.AutoScanEnabled);
    Assert.Null(row.LastScanAtUtc);
}
~~~

Add a second test that asserts the expected table names through sqlite_master.

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~Stage2MigrationTests
~~~

Expected: FAIL because the Stage 2 schema does not exist.

- [ ] **Step 2: Add persistence rows with focused responsibilities**

Use Guid primary keys stored as TEXT. Required columns:

~~~text
inbox_items:
  id, state, media_kind, actual_filename, grouping_rule,
  required_capability, error_code, is_retryable,
  created_at_utc, updated_at_utc

source_files:
  id, inbox_item_id, normalized_path, active_path_key, filename,
  extension, size, last_write_time_utc, first_observed_at_utc,
  stable_observation_count, sha256, is_present, is_primary

metadata_drafts:
  inbox_item_id, display_name, series_id, version, updated_at_utc

taxonomy_terms:
  id, kind, display_name, normalized_name, created_at_utc, updated_at_utc

media_assets:
  id, media_kind, actual_filename, relative_path,
  normalized_relative_path, display_name, size, sha256,
  series_id, metadata_version, created_at_utc, updated_at_utc

ingest_operations:
  id, inbox_item_id, active_inbox_item_id, stage,
  source_path, source_size, source_last_write_time_utc, source_sha256,
  target_filename, staged_path, target_temporary_path,
  metadata_snapshot_json, error_code, created_at_utc, updated_at_utc
~~~

The four join rows use composite keys:

~~~text
inbox_draft_authors(inbox_item_id, author_id, ordinal)
inbox_draft_tags(inbox_item_id, tag_id, ordinal)
media_asset_authors(media_asset_id, author_id, ordinal)
media_asset_tags(media_asset_id, tag_id, ordinal)
~~~

AppSettingsRow gains AutoScanEnabled with default false, LastScanAtUtc nullable, and LastScanSummary nullable.

- [ ] **Step 3: Configure keys, relationships, delete behavior, and unique indexes**

Required uniqueness:

~~~csharp
source.HasIndex(item => item.ActivePathKey).IsUnique();
term.HasIndex(item => new { item.Kind, item.NormalizedName }).IsUnique();
asset.HasIndex(item => item.NormalizedRelativePath).IsUnique();
asset.HasIndex(item => item.Sha256).IsUnique();
operation.HasIndex(item => item.ActiveInboxItemId).IsUnique();
~~~

ActivePathKey and ActiveInboxItemId are nullable and set to null only when their activity is released, allowing historical rows. Configure restrictive deletes for taxonomy references. Cascade only from InboxItem to its SourceFiles, MetadataDraft, and draft joins, and from MediaAsset to its asset joins.

Map enums to stable lowercase strings with explicit value converters; do not rely on integer ordinals.

- [ ] **Step 4: Generate and inspect the migration**

~~~powershell
dotnet tool restore --ignore-failed-sources
dotnet ef migrations add Stage2MediaLibrary --project apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj --startup-project apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj --context MediaDesktopDbContext --output-dir Persistence/Migrations
~~~

Normalize the generated filenames to the exact migration ID used by this plan. In one PowerShell process, resolve the migration directory, require exactly one newly generated main file and one designer file whose names end in _Stage2MediaLibrary, verify both resolved paths remain direct children of that directory, and move them to:

~~~text
20260822090000_Stage2MediaLibrary.cs
20260822090000_Stage2MediaLibrary.Designer.cs
~~~

Use apply_patch on the renamed designer file to set its Migration attribute to 20260822090000_Stage2MediaLibrary. Run dotnet ef migrations list and require that exact ID.

Inspect the generated Up method and confirm:

- existing app_settings values are not overwritten;
- AutoScanEnabled has a false default;
- every foreign key and unique index from Step 3 exists;
- Down drops only Stage 2 additions and columns.

- [ ] **Step 5: Run migration, database initialization, and architecture tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~Stage2MigrationTests|FullyQualifiedName~DatabaseInitializationTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release
~~~

Expected: all focused tests PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs
git commit -m "feat(media-desktop): persist stage-two media records"
~~~

---

### Task 5: Implement single-flight scan reconciliation and stability monitoring

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanTrigger.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanResult.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxScanContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanInboxService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxActivityNotifier.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Startup/IApplicationLifetimeToken.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxScanStore.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/CandidateStabilityWorker.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/HostApplicationLifetimeToken.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/ScanInboxServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Workers/CandidateStabilityWorkerTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Consumes: IInboxFileCatalog, CandidateGroupingService, DirectorySettingsService, and TimeProvider.
- Produces: IInboxScanStore.ReconcileAsync(ScanReconciliation, CancellationToken), HasWaitingCandidatesAsync(CancellationToken), and ClearMissingAsync(Guid, CancellationToken).
- Produces: ScanInboxService.ScanAsync(ScanTrigger, CancellationToken).
- Produces: ScanResult(ScanOutcome Outcome, int ObservedFiles, int CreatedItems, int UpdatedItems, DateTimeOffset CompletedAtUtc, string Message).
- Produces: IInboxActivityNotifier.Changed, Publish(InboxActivityKind), and WaitForCandidateAsync(CancellationToken).
- Produces: IApplicationLifetimeToken.ApplicationStopping.

- [ ] **Step 1: Write failing idempotency and single-flight tests**

~~~csharp
[Fact]
public async Task Repeated_refresh_creates_one_active_item()
{
    var store = new InMemoryInboxScanStore();
    var service = CreateService(
        store,
        snapshots: [Snapshot("C:/inbox/photo.jpg", 10, Instant)]);

    await service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
    await service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
    await service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);

    Assert.Single(store.ActiveItems);
    Assert.Equal(InboxItemState.ReadyToTag, store.ActiveItems[0].State);
}

[Fact]
public async Task Concurrent_triggers_share_one_active_scan()
{
    var catalog = new BlockingInboxFileCatalog();
    ScanInboxService service = CreateService(new InMemoryInboxScanStore(), catalog);

    Task<ScanResult> first = service.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
    await catalog.Entered;
    Task<ScanResult> second = service.ScanAsync(ScanTrigger.Automatic, CancellationToken.None);
    catalog.Release();

    Assert.Equal(await first, await second);
    Assert.Equal(1, catalog.CallCount);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~ScanInboxServiceTests
~~~

Expected: FAIL because ScanInboxService and IInboxScanStore do not exist.

- [ ] **Step 2: Define scan reconciliation records and the store port**

Use immutable application records:

~~~csharp
public sealed record ScanReconciliation(
    string InboxDirectory,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<CandidateGroup> Groups,
    IReadOnlySet<string> ObservedPaths);

public sealed record ScanPersistenceResult(
    int CreatedItems,
    int UpdatedItems,
    bool HasWaitingCandidates);

public interface IInboxScanStore
{
    Task<IReadOnlyDictionary<string, DateTimeOffset>> LoadFirstObservedAsync(
        IReadOnlySet<string> normalizedPaths,
        CancellationToken cancellationToken);

    Task<ScanPersistenceResult> ReconcileAsync(
        ScanReconciliation reconciliation,
        CancellationToken cancellationToken);

    Task<bool> HasWaitingCandidatesAsync(CancellationToken cancellationToken);

    Task ClearMissingAsync(Guid inboxItemId, CancellationToken cancellationToken);
}
~~~

EfInboxScanStore applies these rules in one SQLite transaction:

- assign FirstObservedAtUtc on first sight and reuse it on later observations;
- create one InboxItem for a new CandidateGroup;
- increment stable count only when size and last-write time match the previous sample;
- reset stable count and clear SHA-256 when the fingerprint changes;
- promote a Direct group to Analyzing then ReadyToTag after two unchanged observations;
- promote RequiresProcessing only after all sources are stable;
- keep WaitingForCompanion in WaitingForStability;
- set unseen active sources to not present and their item to SourceMissing;
- set ActivePathKey to null only when a missing record is explicitly cleared or an item completes.

- [ ] **Step 3: Implement ScanInboxService as a shared single-flight operation**

Use a lock-protected Task field, not only a SemaphoreSlim:

~~~csharp
public Task<ScanResult> ScanAsync(
    ScanTrigger trigger,
    CancellationToken cancellationToken)
{
    lock (_sync)
    {
        return _activeScan ??= RunAndReleaseAsync(trigger, cancellationToken);
    }
}
~~~

RunAndReleaseAsync must:

1. Load and validate LibraryFolders.
2. Return ScanOutcome.NotConfigured or InvalidConfiguration without enumeration.
3. Enumerate the union of grouping-rule extensions.
4. Load persisted FirstObservedAtUtc values.
5. Build ObservedFile values and candidate groups.
6. Reconcile in one store call.
7. Publish InboxActivityKind.ScanCompleted.
8. Clear _activeScan in finally.

Cancellation from one caller must not cancel a scan already shared with another caller. The underlying operation uses IApplicationLifetimeToken.ApplicationStopping; each caller awaits the shared task with sharedTask.WaitAsync(callerToken). HostApplicationLifetimeToken adapts IHostApplicationLifetime.ApplicationStopping in Infrastructure.

- [ ] **Step 4: Implement the short-lived candidate worker**

CandidateStabilityWorker is a BackgroundService. It does not scan continuously:

~~~csharp
while (!stoppingToken.IsCancellationRequested)
{
    if (!await store.HasWaitingCandidatesAsync(stoppingToken))
    {
        await activity.WaitForCandidateAsync(stoppingToken);
        continue;
    }

    await Task.Delay(TimeSpan.FromSeconds(2), timeProvider, stoppingToken);
    await scanner.ScanAsync(ScanTrigger.StabilityCheck, stoppingToken);
}
~~~

IInboxActivityNotifier is:

~~~csharp
public sealed class InboxActivityEventArgs(InboxActivityKind kind) : EventArgs
{
    public InboxActivityKind Kind { get; } = kind;
}

public interface IInboxActivityNotifier
{
    event EventHandler<InboxActivityEventArgs>? Changed;
    void Publish(InboxActivityKind kind);
    Task WaitForCandidateAsync(CancellationToken cancellationToken);
}
~~~

ScanInboxService publishes CandidateWaiting whenever ScanPersistenceResult.HasWaitingCandidates is true, then publishes ScanCompleted. InboxActivityNotifier wakes the worker on CandidateWaiting and notifies UI listeners on every kind. Coalesce worker wakeups with a bounded Channel containing at most one signal.

Add Microsoft.Extensions.Hosting to TraceFold.Media.Infrastructure.csproj for BackgroundService and IHostApplicationLifetime.

- [ ] **Step 5: Test source change, missing source, and late companion**

Add tests proving:

- a changed size resets stability and keeps the existing item ID;
- an existing draft version is untouched by source change;
- a missing source becomes SourceMissing and normal refresh does not delete it;
- ClearMissingAsync removes only a never-ingested missing record;
- M4A appearing during the 10-second window turns the MP4 item into RequiresProcessing;
- one catalog failure returns Failed without modifying other active items.

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~ScanInboxServiceTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~EfInboxScanStore|FullyQualifiedName~CandidateStabilityWorkerTests"
~~~

Expected: all focused tests PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Intake apps/media-desktop/src/TraceFold.Media.Application/Startup/IApplicationLifetimeToken.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxScanStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/TraceFold.Media.Infrastructure.csproj apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Workers
git commit -m "feat(media-desktop): reconcile stable inbox candidates"
~~~

---

### Task 6: Persist scan preferences and add optional automatic scanning

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Settings/ScanPreferences.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Settings/ScanPreferencesContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Settings/ScanPreferencesService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfScanPreferencesStore.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/AutomaticInboxScanWorker.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Settings/ScanPreferencesServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Workers/AutomaticInboxScanWorkerTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanInboxService.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Produces: ScanPreferences(bool AutoScanEnabled, DateTimeOffset? LastScanAtUtc, string? LastScanSummary).
- Produces: IScanPreferencesStore.LoadAsync, SaveAutoScanEnabledAsync, and SaveLastScanAsync.
- Produces: ScanPreferencesService.LoadAsync and SetAutoScanEnabledAsync.
- Consumes: ScanInboxService.ScanAsync(ScanTrigger.Automatic, CancellationToken).

- [ ] **Step 1: Write failing default and persistence tests**

~~~csharp
[Fact]
public async Task Missing_settings_default_auto_scan_off()
{
    var service = new ScanPreferencesService(new InMemoryScanPreferencesStore());
    ScanPreferences value = await service.LoadAsync(CancellationToken.None);
    Assert.False(value.AutoScanEnabled);
}

[Fact]
public async Task Enable_is_persisted_without_changing_directories()
{
    var store = new InMemoryScanPreferencesStore();
    var service = new ScanPreferencesService(store);

    await service.SetAutoScanEnabledAsync(true, CancellationToken.None);

    Assert.True((await store.LoadAsync(CancellationToken.None)).AutoScanEnabled);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~ScanPreferencesServiceTests
~~~

Expected: FAIL because the scan-preference types do not exist.

- [ ] **Step 2: Implement the settings service and EF store**

EfScanPreferencesStore updates only AutoScanEnabled, LastScanAtUtc, or LastScanSummary on the existing row. If the directory row does not exist, LoadAsync returns the safe default and SetAutoScanEnabledAsync returns a ScanPreferenceResult with code settings.directories_required; it must not create an invalid app_settings row with blank required paths.

Task 6 modifies ScanInboxService so every completed scan calls IScanPreferencesStore.SaveLastScanAsync with the exact completion timestamp and summary; failed or invalid scans also persist their user-facing summary when an app_settings row exists.

ScanInboxService writes one of these exact summaries:

~~~text
扫描完成：发现 {ObservedFiles} 个文件，新增 {CreatedItems} 项。
目录尚未配置。
目录配置无效。
扫描失败，请查看日志。
~~~

- [ ] **Step 3: Write a failing worker test**

Use a fake TimeProvider or an explicit RunCycleAsync method so the test never sleeps:

~~~csharp
[Fact]
public async Task RunCycleAsync_scans_only_when_enabled()
{
    var scanner = new RecordingScanner();
    var preferences = new MutablePreferences(autoScanEnabled: false);
    var worker = new AutomaticInboxScanWorker(scanner, preferences, TimeProvider.System);

    await worker.RunCycleAsync(CancellationToken.None);
    preferences.AutoScanEnabled = true;
    await worker.RunCycleAsync(CancellationToken.None);

    Assert.Equal(1, scanner.CallCount);
    Assert.Equal(ScanTrigger.Automatic, scanner.LastTrigger);
}
~~~

- [ ] **Step 4: Implement the 5-second hosted worker**

AutomaticInboxScanWorker must:

- wait 5 seconds between cycles with TimeProvider;
- call the same ScanInboxService used by manual refresh;
- perform no enumeration while disabled;
- log and continue after non-cancellation failures;
- stop promptly on host cancellation;
- never start a second scan because ScanInboxService is single-flight.

Register TimeProvider.System, CandidateStabilityWorker, and AutomaticInboxScanWorker exactly once.

- [ ] **Step 5: Run focused tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~ScanPreferencesServiceTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~EfScanPreferencesStore|FullyQualifiedName~AutomaticInboxScanWorkerTests"
~~~

Expected: all focused tests PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Settings apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanInboxService.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfScanPreferencesStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Settings apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Workers
git commit -m "feat(media-desktop): schedule optional inbox scanning"
~~~

---

### Task 7: Implement metadata, taxonomy, and library query services

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Domain/Metadata/TaxonomyKind.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Metadata/MetadataOwner.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Metadata/MetadataDocument.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Metadata/MetadataContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Metadata/MetadataEditorService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryQueryService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfMetadataStore.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyStore.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibraryQueryStore.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/MetadataEditorServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/MetadataPersistenceTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Produces: MetadataOwner(MetadataOwnerKind Kind, Guid Id), where Kind is InboxItem or MediaAsset.
- Produces: MetadataDocument(MetadataOwner Owner, string? DisplayName, IReadOnlyList<TaxonomyTermDto> Authors, TaxonomyTermDto? Series, IReadOnlyList<TaxonomyTermDto> Tags, long Version).
- Produces: MetadataUpdate(string? DisplayName, IReadOnlyList<Guid> AuthorIds, Guid? SeriesId, IReadOnlyList<Guid> TagIds, long ExpectedVersion).
- Produces: MetadataEditorService.LoadAsync and SaveAsync.
- Produces: TaxonomyService.SearchAsync, CreateAsync, RenameAsync, and DeleteAsync.
- Produces: LibraryQueryService.SearchAsync(string? query, CancellationToken) and GetDetailsAsync(Guid, CancellationToken).
- Produces: ITaxonomyLookup.ValidateAsync(MetadataSelection, CancellationToken).

Library DTOs are:

~~~csharp
public sealed record LibraryAssetDto(
    Guid Id,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    DateTimeOffset CreatedAtUtc);

public sealed record LibraryAssetDetailsDto(
    LibraryAssetDto Card,
    MetadataDocument Metadata,
    long Size,
    string Sha256);
~~~

- [ ] **Step 1: Write failing version and cardinality tests**

~~~csharp
[Fact]
public async Task SaveAsync_rejects_stale_version_without_overwrite()
{
    var store = new InMemoryMetadataStore(Version: 4);
    var service = new MetadataEditorService(store);
    var update = new MetadataUpdate("new", [], null, [], ExpectedVersion: 3);

    MetadataSaveResult result = await service.SaveAsync(
        new MetadataOwner(MetadataOwnerKind.InboxItem, Guid.NewGuid()),
        update,
        CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Equal("metadata.version_conflict", result.ErrorCode);
    Assert.Equal(4, store.Version);
}

[Fact]
public async Task SaveAsync_accepts_many_authors_one_series_many_tags()
{
    var store = new InMemoryMetadataStore(Version: 0);
    var service = new MetadataEditorService(store);
    Guid series = Guid.NewGuid();

    MetadataSaveResult result = await service.SaveAsync(
        Owner,
        new MetadataUpdate(null, [Author1, Author2], series, [Tag1, Tag2], 0),
        CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.Equal(1, result.CurrentVersion);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~MetadataEditorServiceTests
~~~

Expected: FAIL because the metadata application service does not exist.

- [ ] **Step 2: Implement metadata contracts and service**

IMetadataStore must expose one atomic compare-and-swap save:

~~~csharp
public interface IMetadataStore
{
    Task<MetadataDocument?> LoadAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken);

    Task<MetadataSaveResult> TrySaveAsync(
        MetadataOwner owner,
        MetadataSelection selection,
        long expectedVersion,
        CancellationToken cancellationToken);
}
~~~

MetadataEditorService validates that every referenced taxonomy ID exists and has the correct kind before calling TrySaveAsync. A series ID cannot refer to an author or tag. Saving an inbox draft and saving a formal asset use the same method but different MetadataOwnerKind values.

Use this lookup port:

~~~csharp
public interface ITaxonomyLookup
{
    Task<TaxonomyValidationResult> ValidateAsync(
        MetadataSelection selection,
        CancellationToken cancellationToken);
}
~~~

TaxonomyValidationResult returns Succeeded false with metadata.taxonomy_missing or metadata.taxonomy_kind_mismatch. EfTaxonomyStore implements both ITaxonomyStore and ITaxonomyLookup.

- [ ] **Step 3: Write failing taxonomy tests**

~~~csharp
[Fact]
public async Task CreateAsync_reuses_existing_normalized_term()
{
    var store = new InMemoryTaxonomyStore();
    var service = new TaxonomyService(store);

    TaxonomyTermDto first = await service.CreateAsync(
        TaxonomyKind.Author, "Alice", CancellationToken.None);
    TaxonomyTermDto second = await service.CreateAsync(
        TaxonomyKind.Author, " ＡLICE ", CancellationToken.None);

    Assert.Equal(first.Id, second.Id);
}

[Fact]
public async Task DeleteAsync_refuses_referenced_term()
{
    var store = new InMemoryTaxonomyStore(referenceCount: 2);
    var service = new TaxonomyService(store);

    TaxonomyMutationResult result =
        await service.DeleteAsync(store.TermId, CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Equal("taxonomy.in_use", result.ErrorCode);
}
~~~

RenameAsync must reject a normalized-name collision with taxonomy.name_conflict. Search is case-insensitive and ordered by display name.

- [ ] **Step 4: Implement EF metadata, taxonomy, and library stores**

EfMetadataStore:

- loads ordered join rows;
- performs version compare-and-swap in a SQLite transaction;
- replaces author/tag joins and the optional series only after all IDs validate;
- increments Version exactly once;
- updates only logical asset fields for MediaAsset owners.

EfTaxonomyStore:

- catches only the expected unique-index violation and reloads the existing normalized term;
- counts references across inbox drafts and formal assets;
- refuses referenced deletes;
- preserves the first display form on CreateAsync.

EfLibraryQueryStore:

- returns only committed MediaAsset rows;
- searches ActualFilename and DisplayName with escaped LIKE terms;
- orders newest assets first, then ActualFilename;
- returns an ID-based details DTO, not a caller-supplied path.

- [ ] **Step 5: Run application and persistence tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter "FullyQualifiedName~MetadataEditorServiceTests|FullyQualifiedName~TaxonomyServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~MetadataPersistenceTests
~~~

Expected: all focused tests PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Domain/Metadata apps/media-desktop/src/TraceFold.Media.Application/Metadata apps/media-desktop/src/TraceFold.Media.Application/Library apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfMetadataStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibraryQueryStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/MetadataPersistenceTests.cs
git commit -m "feat(media-desktop): persist media metadata and taxonomy"
~~~

---

### Task 8: Add controlled hashing, staging, commit, cleanup, and preview resolution

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Storage/FileStorageContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Preview/PreviewContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Preview/MediaPreviewResolver.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/Sha256FileHasher.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledMediaStorage.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfPreviewPathStore.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/Sha256FileHasherTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledMediaStorageTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/PreviewPathStoreTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaFolders.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Produces: IFileHasher.ComputeSha256Async(string, CancellationToken).
- Produces: IControlledMediaStorage.StageAsync, CommitAsync, DeleteSourceIfMatchesAsync, and CleanupTemporaryFilesAsync.
- Produces: StagedFile(string FullPath, long Size, string Sha256).
- Produces: CommittedFile(string FullPath, string RelativePath, long Size, string Sha256).
- Produces: MediaPreviewResolver.ResolveAsync(MetadataOwner, CancellationToken) returning PreviewDescriptor(MediaKind, string TrustedFullPath, bool Exists).

- [ ] **Step 1: Write failing hash and safe-delete tests**

~~~csharp
[Fact]
public async Task DeleteSourceIfMatchesAsync_never_deletes_changed_source()
{
    using var folders = TestMediaFolders.Create();
    string source = folders.WriteInbox("photo.jpg", [1, 2, 3]);
    var storage = CreateStorage();
    string originalHash = await Hasher.ComputeSha256Async(source, CancellationToken.None);
    await File.WriteAllBytesAsync(source, [9, 9, 9]);

    SourceDeleteResult result = await storage.DeleteSourceIfMatchesAsync(
        folders.Value,
        source,
        expectedSize: 3,
        expectedSha256: originalHash,
        CancellationToken.None);

    Assert.Equal(SourceDeleteOutcome.SourceChanged, result.Outcome);
    Assert.True(File.Exists(source));
}
~~~

Add SHA-256 tests with known bytes and cancellation.

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~Sha256FileHasherTests|FullyQualifiedName~ControlledMediaStorageTests"
~~~

Expected: FAIL because storage contracts and implementations do not exist.

- [ ] **Step 2: Define the controlled storage port**

~~~csharp
public interface IControlledMediaStorage
{
    Task<bool> TargetExistsAsync(
        LibraryFolders folders,
        string targetFilename,
        CancellationToken cancellationToken);

    Task<StagedFile> StageAsync(
        Guid operationId,
        LibraryFolders folders,
        string sourcePath,
        string expectedSha256,
        CancellationToken cancellationToken);

    Task<CommittedFile> CommitAsync(
        Guid operationId,
        LibraryFolders folders,
        StagedFile staged,
        string targetFilename,
        CancellationToken cancellationToken);

    Task<SourceDeleteResult> DeleteSourceIfMatchesAsync(
        LibraryFolders folders,
        string sourcePath,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken);

    Task<bool> VerifyCommittedAsync(
        LibraryFolders folders,
        CommittedFile committed,
        CancellationToken cancellationToken);

    Task CleanupTemporaryFilesAsync(
        Guid operationId,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}
~~~

Every method canonicalizes roots and calls ControlledPathGuard. Validate targetFilename with Path.GetFileName(targetFilename) == targetFilename and reject separators, rooted names, dot names, and alternate data-stream colons.

- [ ] **Step 3: Implement one deterministic cross-volume-safe commit path**

Do not branch on drive letters. Always:

1. Copy source to WorkDirectory/.tracefold/ingest/{operationId:N}/payload.
2. Flush and verify size and SHA-256.
3. Copy staged payload to LibraryDirectory/.tracefold-{operationId:N}.tmp.
4. Flush and verify the target-side temporary file.
5. Recheck that the formal target does not exist.
6. File.Move the target-side temporary file to the final name within the same directory.

This is safe on same-volume and cross-volume configurations and avoids an unnecessary strategy abstraction in Stage 2.

CleanupTemporaryFilesAsync may delete only the exact operation work directory and exact target-side temporary filename after both are resolved under their configured roots.

- [ ] **Step 4: Implement ID-based preview resolution**

IPreviewPathStore accepts MetadataOwner only:

~~~csharp
public interface IPreviewPathStore
{
    Task<PreviewPathRecord?> FindAsync(
        MetadataOwner owner,
        CancellationToken cancellationToken);
}
~~~

EfPreviewPathStore joins:

- InboxItem → primary present SourceFile for inbox previews;
- MediaAsset → configured library root plus RelativePath for formal previews.

It validates the resolved path through ControlledPathGuard before returning it. MediaPreviewResolver never accepts a raw path from Desktop. It returns Exists false for a missing registered file and never repairs or deletes it.

- [ ] **Step 5: Add stage, commit, traversal, and preview tests**

Prove:

- staged bytes and hash equal source;
- source still exists after StageAsync and CommitAsync;
- final actual filename exactly matches input;
- existing formal target causes StorageConflictException and preserves both files;
- ../, rooted target names, reparse points, and alternate data streams are rejected;
- CleanupTemporaryFilesAsync cannot escape the work or library root;
- a database path escape is rejected by EfPreviewPathStore;
- a missing registered preview returns Exists false.

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter "FullyQualifiedName~Sha256FileHasherTests|FullyQualifiedName~ControlledMediaStorageTests|FullyQualifiedName~PreviewPathStoreTests"
~~~

Expected: all focused tests PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Storage apps/media-desktop/src/TraceFold.Media.Application/Preview apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfPreviewPathStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/PreviewPathStoreTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaFolders.cs
git commit -m "feat(media-desktop): guard media storage and previews"
~~~

---

### Task 9: Orchestrate ordinary ingest and startup reconciliation

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Ingest/OrdinaryIngestService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestRecoveryService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Ingest/MediaOperationCoordinator.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Startup/MediaRuntimeGate.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/OrdinaryIngestServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestRecoveryServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/MediaOperationCoordinatorTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/IngestPersistenceTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanTrigger.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Application/TraceFold.Media.Application.csproj
- Modify: apps/media-desktop/src/TraceFold.Media.Application/Startup/StartupCoordinator.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/CandidateStabilityWorker.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/AutomaticInboxScanWorker.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Consumes: ScanInboxService, MetadataEditorService, IFileHasher, IControlledMediaStorage, LibraryFolders, and Task 1 ingest states.
- Produces: OrdinaryIngestService.IngestAsync(Guid inboxItemId, CancellationToken).
- Produces: IngestResult(IngestOutcome Outcome, Guid? OperationId, Guid? AssetId, string Message).
- Produces: IIngestStore load/check/create/advance/commit methods below.
- Produces: IngestRecoveryService.ReconcileAsync(CancellationToken).
- Produces: IngestRecoveryService.RetryAsync(Guid inboxItemId, CancellationToken).
- Produces: IMediaOperationCoordinator.Begin(CancellationToken) and StopAcceptingAndDrainAsync(CancellationToken).
- Produces: IMediaRuntimeGate.WaitUntilReadyAsync(CancellationToken) and Open().

- [ ] **Step 1: Write failing happy-path and conflict tests**

~~~csharp
[Fact]
public async Task IngestAsync_commits_asset_before_deleting_source()
{
    IngestFixture fixture = IngestFixture.ReadyOrdinary();

    IngestResult result = await fixture.Service.IngestAsync(
        fixture.ItemId, CancellationToken.None);

    Assert.Equal(IngestOutcome.Completed, result.Outcome);
    Assert.True(fixture.Store.AssetCommitted);
    Assert.True(fixture.Storage.DeleteObservedAfterAssetCommit);
    Assert.False(fixture.Storage.SourceExists);
}

[Theory]
[InlineData(ConflictKind.Name)]
[InlineData(ConflictKind.RegisteredContent)]
public async Task IngestAsync_blocks_conflict_before_staging(ConflictKind kind)
{
    IngestFixture fixture = IngestFixture.WithConflict(kind);

    IngestResult result = await fixture.Service.IngestAsync(
        fixture.ItemId, CancellationToken.None);

    Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
    Assert.Equal(0, fixture.Storage.StageCallCount);
    Assert.True(fixture.Storage.SourceExists);
}
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~OrdinaryIngestServiceTests
~~~

Expected: FAIL because the ingest application types do not exist.

- [ ] **Step 2: Define the ingest store as durable atomic operations**

~~~csharp
public interface IIngestStore
{
    Task<IngestCandidate?> LoadCandidateAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<IngestOperationRecord?> FindActiveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);

    Task<ConflictCheck> CheckConflictsAsync(
        string targetFilename,
        string sourceSha256,
        CancellationToken cancellationToken);

    Task BlockConflictAsync(
        Guid inboxItemId,
        ConflictKind kind,
        CancellationToken cancellationToken);

    Task<IngestOperationRecord> CreateAsync(
        IngestPreparation preparation,
        CancellationToken cancellationToken);

    Task MarkStagedAsync(
        Guid operationId,
        StagedFile staged,
        CancellationToken cancellationToken);

    Task AbandonForRetryAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task MarkFileCommittedAsync(
        Guid operationId,
        CommittedFile committed,
        CancellationToken cancellationToken);

    Task<Guid> CommitAssetAsync(
        Guid operationId,
        CommittedFile committed,
        CancellationToken cancellationToken);

    Task MarkCleanupPendingAsync(
        Guid operationId,
        string errorCode,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken);

    Task FailAsync(
        Guid operationId,
        string errorCode,
        bool isRetryable,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IngestOperationRecord>> LoadIncompleteAsync(
        CancellationToken cancellationToken);
}
~~~

IngestResult uses a nullable operation identifier because Stage 2 records name/content blocking on InboxItem before creating an operation:

~~~csharp
public sealed record IngestResult(
    IngestOutcome Outcome,
    Guid? OperationId,
    Guid? AssetId,
    string Message);
~~~

EfIngestStore.CommitAssetAsync must use one SQLite transaction to insert MediaAsset, copy the operation metadata snapshot into asset relations, set InboxItem to Completed, clear all source ActivePathKey values, set operation to DatabaseCommitted, and save once.

The active operation unique index is the final idempotency guard. A repeated command returns the existing operation result rather than creating a second operation.

- [ ] **Step 3: Implement the exact ingest sequence**

OrdinaryIngestService.IngestAsync:

1. Acquire an IMediaOperationCoordinator lease; reject with ingest.shutting_down if new work is no longer accepted.
2. Call ScanInboxService.ScanAsync with ScanTrigger.PreIngest.
3. Reload the candidate; require ReadyToTag, one primary source, present source, and no RequiredCapability.
4. Re-read source fingerprint.
5. Compute SHA-256 without changing the source.
6. Acquire the singleton ordinary-ingest commit gate.
7. Recheck source fingerprint, physical target existence through IControlledMediaStorage.TargetExistsAsync, and registered name/hash conflicts through IIngestStore.
8. On conflict, call IIngestStore.BlockConflictAsync and return a null OperationId without creating a staged file.
9. Create IngestOperation with source fingerprint, hash, target filename, and serialized immutable metadata snapshot.
10. Stage and persist Staged.
11. Commit the file and persist FileCommitted.
12. Commit asset and relationships in SQLite.
13. Call IControlledMediaStorage.VerifyCommittedAsync.
14. Delete source only when its size and hash still match.
15. On delete success, clean temporary files and complete.
16. On delete failure/change, mark CleanupPending and preserve the source.
17. Release the commit gate and operation lease in finally.

The Desktop IngestCommand performs MetadataEditorViewModel.FlushAsync before calling OrdinaryIngestService. Non-UI callers ingest the latest already-persisted draft; Application never calls back into a Desktop service.

Map expected errors to stable codes:

~~~text
ingest.not_ready
ingest.source_missing
ingest.source_changed
ingest.requires_processing
ingest.name_conflict
ingest.content_conflict
ingest.stage_failed
ingest.commit_failed
ingest.database_failed
ingest.cleanup_failed
~~~

Do not catch OperationCanceledException when the caller token requested cancellation. For other exceptions, persist Failed if an operation already exists and log with OperationId.

- [ ] **Step 4: Write failing recovery tests**

~~~csharp
[Fact]
public async Task FileCommitted_with_matching_target_completes_database_commit()
{
    RecoveryFixture fixture = RecoveryFixture.FileCommittedWithMatchingTarget();

    await fixture.Service.ReconcileAsync(CancellationToken.None);

    Assert.True(fixture.Store.AssetCommitted);
    Assert.True(fixture.Storage.SourceDeleted);
    Assert.Equal(IngestOperationStage.Completed, fixture.Store.Stage);
}

[Fact]
public async Task Unknown_target_identity_preserves_source_and_reports_failure()
{
    RecoveryFixture fixture = RecoveryFixture.FileCommittedWithWrongHash();

    await fixture.Service.ReconcileAsync(CancellationToken.None);

    Assert.False(fixture.Storage.SourceDeleted);
    Assert.Equal(IngestOperationStage.Failed, fixture.Store.Stage);
    Assert.Equal("recovery.target_identity_unknown", fixture.Store.ErrorCode);
}
~~~

Recovery rules:

~~~text
Created or Staged:
  source matches -> remove exact temporary files, return item to ReadyToTag
  source missing/changed -> Failed, delete nothing

FileCommitted:
  target path/size/hash match operation -> CommitAssetAsync, then cleanup
  otherwise -> Failed, delete nothing

DatabaseCommitted or CleanupPending:
  formal asset still matches -> retry source delete by size/hash
  formal asset mismatch -> Failed, delete nothing
~~~

RetryAsync loads the one active operation for the item and applies the same reconciliation table to that operation only. It returns recovery.not_retryable when no safe transition exists and never restarts a new ordinary ingest from a failed state without first restoring the item to ReadyToTag.

For Created or Staged operations that are proven safe to retry, IngestRecoveryService cleans exact temporary files then calls IIngestStore.AbandonForRetryAsync. That method marks the old operation Failed, clears ActiveInboxItemId, and returns the item to ReadyToTag in one transaction.

- [ ] **Step 5: Track active operations and drain at shutdown**

MediaOperationCoordinator:

- owns an accepting flag and a shutdown CancellationTokenSource;
- Begin returns a disposable lease containing a token linked to caller cancellation and coordinator shutdown;
- rejects Begin after shutdown starts;
- increments the active count before returning a lease;
- decrements exactly once when the lease is disposed;
- StopAcceptingAndDrainAsync atomically disables Begin, cancels active lease tokens, and waits for active count zero with the caller timeout token.

~~~csharp
[Fact]
public async Task Stop_rejects_new_operations_and_waits_for_active_lease()
{
    var coordinator = new MediaOperationCoordinator();
    await using IMediaOperationLease lease =
        coordinator.Begin(CancellationToken.None);

    Task drain = coordinator.StopAcceptingAndDrainAsync(CancellationToken.None);
    Assert.False(drain.IsCompleted);
    Assert.Throws<MediaOperationRejectedException>(
        () => coordinator.Begin(CancellationToken.None));

    await lease.DisposeAsync();
    await drain;
}
~~~

OrdinaryIngestService uses the lease token for hashing, copying, database calls, verification, and cleanup. Cancellation can leave only a stage represented by the already-persisted operation; it never deletes the source before DatabaseCommitted.

- [ ] **Step 6: Gate workers until recovery finishes**

MediaRuntimeGate wraps a TaskCompletionSource created with RunContinuationsAsynchronously. WaitUntilReadyAsync awaits the gate task with WaitAsync(cancellationToken), so host shutdown can stop workers even while setup is incomplete. CandidateStabilityWorker and AutomaticInboxScanWorker await WaitUntilReadyAsync before their loops.

StartupCoordinator order becomes:

~~~text
database migration
load and validate directories
IngestRecoveryService.ReconcileAsync
MediaRuntimeGate.Open
publish Ready
~~~

For SetupRequired or Failed, leave the gate closed. Re-running InitializeAsync after valid settings performs recovery and opens the same idempotent gate.

- [ ] **Step 7: Implement and test EF ingest persistence**

Add integration tests proving:

- repeated idempotent calls return one operation and one asset;
- CommitAssetAsync writes asset, metadata relations, inbox completion, released active source paths, and DatabaseCommitted atomically;
- a forced SaveChanges failure leaves the operation recoverable and source present;
- CleanupPending survives a new DbContext;
- registered hash conflict is detected before staging.

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter "FullyQualifiedName~OrdinaryIngestServiceTests|FullyQualifiedName~IngestRecoveryServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~MediaOperationCoordinatorTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~IngestPersistenceTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~StartupCoordinatorTests
~~~

Expected: all focused tests PASS.

- [ ] **Step 8: Register application services and commit**

Add Microsoft.Extensions.DependencyInjection.Abstractions to the Application project and create AddMediaApplication:

~~~csharp
public static IServiceCollection AddMediaApplication(this IServiceCollection services)
{
    services.AddSingleton<DirectorySettingsService>();
    services.AddSingleton<IStartupCoordinator, StartupCoordinator>();
    services.AddSingleton<CandidateGroupingService>();
    services.AddSingleton<IInboxGroupingRule, Mp4M4aGroupingRule>();
    services.AddSingleton<IInboxGroupingRule, DirectMediaGroupingRule>();
    services.AddSingleton<ScanInboxService>();
    services.AddSingleton<ScanPreferencesService>();
    services.AddSingleton<MetadataEditorService>();
    services.AddSingleton<TaxonomyService>();
    services.AddSingleton<LibraryQueryService>();
    services.AddSingleton<OrdinaryIngestService>();
    services.AddSingleton<IngestRecoveryService>();
    services.AddSingleton<IMediaOperationCoordinator, MediaOperationCoordinator>();
    services.AddSingleton<IMediaRuntimeGate, MediaRuntimeGate>();
    services.AddSingleton<IInboxActivityNotifier, InboxActivityNotifier>();
    return services;
}
~~~

CandidateGroupingService consumes IEnumerable<IInboxGroupingRule>; do not resolve the container from inside the service.

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/IngestPersistenceTests.cs
git commit -m "feat(media-desktop): ingest ordinary media safely"
~~~

---

### Task 10: Build inbox queries and testable metadata-editor ViewModels

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxQueryContracts.cs
- Create: apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxQueryService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/IUiDispatcher.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/DispatcherQueueAdapter.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/IDebounceScheduler.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/ProductionDebounceScheduler.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/MetadataEditorViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxCardViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxInspectorViewModel.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/InboxQueryServiceTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MetadataEditorViewModelTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs
- Modify: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs

**Interfaces:**
- Produces: InboxQuery(string? Search, InboxItemState? State).
- Produces: InboxCardDto(Guid Id, string ActualFilename, string DisplayName, MediaKind MediaKind, InboxItemState State, string StatusText, bool CanIngest).
- Produces: InboxDetailsDto with SourceFiles, PreviewDescriptor, MetadataDocument, AllowedActions, ErrorCode, and RequiredCapability.
- Produces: InboxQueryService.SearchAsync, GetDetailsAsync, and ClearMissingAsync.
- Produces: MetadataEditorViewModel.LoadAsync(MetadataOwner), FlushAsync, and observable DisplayName, Authors, Series, Tags, IsSaving, and ErrorMessage.
- Produces: InboxViewModel.LoadAsync, RefreshCommand, selected item, and state/search filters.
- Produces: InboxViewModel.ClearMissingCommand and RetryCommand.
- Produces: InboxCardViewModel with AutomationId, DisplayName, StatusText, State, ThumbnailSource, ThumbnailVisibility, FallbackVisibility, and FallbackGlyph.

- [ ] **Step 1: Write failing query tests**

~~~csharp
[Fact]
public async Task Search_matches_actual_or_display_name_case_insensitively()
{
    var store = new InMemoryInboxQueryStore(
        Card("旅行记录.MP4", "杭州夜景"),
        Card("cat.jpg", null));
    var service = new InboxQueryService(store);

    IReadOnlyList<InboxCardDto> result = await service.SearchAsync(
        new InboxQuery("杭州", null),
        CancellationToken.None);

    Assert.Equal("旅行记录.MP4", Assert.Single(result).ActualFilename);
}
~~~

EfInboxQueryStore must return active non-Completed items, apply escaped name search and optional state filter, and order Waiting/Failed/Blocked before Ready, then by filename.

- [ ] **Step 2: Write failing debounce and flush tests**

Use a ManualDebounceScheduler in tests; do not sleep:

~~~csharp
[Fact]
public async Task Edit_saves_once_after_500_milliseconds()
{
    var gateway = new RecordingMetadataGateway();
    var scheduler = new ManualDebounceScheduler();
    var viewModel = new MetadataEditorViewModel(gateway, scheduler);
    await viewModel.LoadAsync(Owner, CancellationToken.None);

    viewModel.DisplayName = "A";
    viewModel.DisplayName = "AB";
    scheduler.AdvanceBy(TimeSpan.FromMilliseconds(499));
    Assert.Equal(0, gateway.SaveCount);
    scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
    await scheduler.DrainAsync();

    Assert.Equal(1, gateway.SaveCount);
    Assert.Equal("AB", gateway.LastUpdate!.DisplayName);
}

[Fact]
public async Task Switching_selection_flushes_old_owner_before_loading_new()
{
    var gateway = new RecordingMetadataGateway();
    var viewModel = CreateViewModel(gateway);
    await viewModel.LoadAsync(FirstOwner, CancellationToken.None);
    viewModel.DisplayName = "unsaved";

    await viewModel.LoadAsync(SecondOwner, CancellationToken.None);

    Assert.Equal(FirstOwner, gateway.SavedOwners.Single());
    Assert.Equal(SecondOwner, viewModel.Owner);
}
~~~

- [ ] **Step 3: Implement an injectable debounce scheduler**

~~~csharp
public interface IDebounceScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
~~~

ProductionDebounceScheduler uses Task.Delay(delay, TimeProvider, token). MetadataEditorViewModel cancels the previous delay when a field changes, captures the current owner and version, waits exactly 500 milliseconds, then calls MetadataEditorService.SaveAsync.

FlushAsync cancels the pending delay, waits for any in-flight save, and saves current dirty state immediately. Version conflict reloads the latest document and shows “资料已在其他位置更新，请重新确认。” It never silently overwrites.

- [ ] **Step 4: Implement InboxViewModel commands and refresh behavior**

InboxViewModel:

- loads cards on page activation;
- subscribes to InboxActivityNotifier through IUiDispatcher;
- RefreshCommand calls ScanInboxService.ScanAsync(Manual);
- exposes IsRefreshing and ScanStatusMessage;
- changing SearchText or SelectedStateFilter reloads query results;
- for image cards, resolves an existing trusted image path into ThumbnailSource; for video, missing, or failed image previews, uses a media-type Segoe Fluent Icons glyph and never reads arbitrary paths;
- selecting a card flushes the previous MetadataEditorViewModel then loads InboxDetails;
- IngestCommand first calls MetadataEditorViewModel.FlushAsync, then OrdinaryIngestService.IngestAsync;
- ClearMissingCommand is enabled only for SourceMissing and calls InboxQueryService.ClearMissingAsync;
- RetryCommand is enabled only when AllowedActions contains Retry or RetryCleanup and calls IngestRecoveryService.RetryAsync;
- clears selection after Completed and reloads cards;
- shows stable Chinese messages for RequiresProcessing, ConflictBlocked, SourceMissing, and Failed;
- refuses command re-entry while busy.

- [ ] **Step 5: Run query and ViewModel tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --filter FullyQualifiedName~InboxQueryServiceTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~MetadataEditorViewModelTests|FullyQualifiedName~InboxViewModelTests"
~~~

Expected: all focused tests PASS without launching WinUI.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Intake apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/src/TraceFold.Media.Desktop/Services apps/media-desktop/src/TraceFold.Media.Desktop/Pages apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/InboxQueryServiceTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): model the native inbox workspace"
~~~

---

### Task 11: Replace the inbox empty state with the native media workspace

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Controls/MediaPreviewControl.xaml
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Controls/MediaPreviewControl.xaml.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerControl.xaml
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerControl.xaml.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Controls/TaxonomyPickerViewModel.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Controls/TaxonomyPickerViewModelTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Shell/ShellPage.xaml.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs
- Modify: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Shell/ShellViewModelTests.cs

**Interfaces:**
- Consumes: InboxViewModel, InboxInspectorViewModel, MetadataEditorViewModel, and PreviewDescriptor.
- Produces: stable AutomationIds InboxSearchBox, InboxStateFilter, InboxRefreshButton, InboxGrid, InboxCard_{id}, InspectorPane, DisplayNameBox, AuthorPicker, SeriesPicker, TagPicker, and IngestButton.
- Produces: stable AutomationIds RetryInboxItemButton and ClearMissingItemButton for their allowed states.

- [ ] **Step 1: Add a failing desktop structure test**

Extend the desktop structural tests to read InboxPage.xaml and assert:

~~~csharp
Assert.Contains("AutomationProperties.AutomationId=\"InboxRefreshButton\"", xaml);
Assert.Contains("AutomationProperties.AutomationId=\"InboxGrid\"", xaml);
Assert.Contains("AutomationProperties.AutomationId=\"InspectorPane\"", xaml);
Assert.Contains("MediaPlayerElement", previewXaml);
~~~

Run the focused desktop test and expect failure because the current empty-state page has no workspace controls.

- [ ] **Step 2: Move the right pane from Shell into media pages**

ShellPage keeps NavigationView and a single ContentFrame. InboxPage owns:

~~~xml
<Grid ColumnDefinitions="*,360">
  <Grid Grid.Column="0" RowDefinitions="Auto,*">
    <Grid ColumnDefinitions="*,Auto,Auto" Margin="20,16,20,12">
      <AutoSuggestBox
          AutomationProperties.AutomationId="InboxSearchBox"
          QueryIcon="Find"
          PlaceholderText="搜索文件名或显示名称"
          Text="{x:Bind ViewModel.SearchText, Mode=TwoWay}" />
      <ComboBox
          Grid.Column="1"
          AutomationProperties.AutomationId="InboxStateFilter"
          ItemsSource="{x:Bind ViewModel.StateFilters, Mode=OneWay}"
          SelectedValue="{x:Bind ViewModel.SelectedStateFilter, Mode=TwoWay}"
          MinWidth="150" />
      <Button
          Grid.Column="2"
          AutomationProperties.AutomationId="InboxRefreshButton"
          Command="{x:Bind ViewModel.RefreshCommand}"
          Content="立即刷新" />
    </Grid>
    <GridView
        Grid.Row="1"
        AutomationProperties.AutomationId="InboxGrid"
        IsItemClickEnabled="True"
        ItemsSource="{x:Bind ViewModel.Items, Mode=OneWay}"
        SelectedItem="{x:Bind ViewModel.SelectedItem, Mode=TwoWay}"
        SelectionMode="Single">
      <GridView.ItemTemplate>
        <DataTemplate x:DataType="pages:InboxCardViewModel">
          <Border Width="190" Padding="10" CornerRadius="8"
                  AutomationProperties.AutomationId="{x:Bind AutomationId}">
            <Grid RowDefinitions="120,Auto,Auto">
              <Image Source="{x:Bind ThumbnailSource}" Stretch="UniformToFill"
                     Visibility="{x:Bind ThumbnailVisibility}" />
              <FontIcon Glyph="{x:Bind FallbackGlyph}"
                        Visibility="{x:Bind FallbackVisibility}" />
              <TextBlock Grid.Row="1" Margin="0,8,0,0"
                         Text="{x:Bind DisplayName}" TextTrimming="CharacterEllipsis" />
              <TextBlock Grid.Row="2" Text="{x:Bind StatusText}" />
            </Grid>
          </Border>
        </DataTemplate>
      </GridView.ItemTemplate>
    </GridView>
  </Grid>
  <Border
      Grid.Column="1"
      AutomationProperties.AutomationId="InspectorPane"
      BorderThickness="1,0,0,0">
    <ScrollViewer>
      <StackPanel Padding="20" Spacing="12">
        <controls:MediaPreviewControl
            Descriptor="{x:Bind ViewModel.Inspector.Preview, Mode=OneWay}" />
        <TextBlock Text="{x:Bind ViewModel.Inspector.ActualFilename, Mode=OneWay}" />
        <TextBox
            AutomationProperties.AutomationId="DisplayNameBox"
            Header="显示名称"
            Text="{x:Bind ViewModel.Inspector.Metadata.DisplayName, Mode=TwoWay}" />
        <controls:TaxonomyPickerControl
            AutomationProperties.AutomationId="AuthorPicker"
            Kind="Author" />
        <controls:TaxonomyPickerControl
            AutomationProperties.AutomationId="SeriesPicker"
            Kind="Series" />
        <controls:TaxonomyPickerControl
            AutomationProperties.AutomationId="TagPicker"
            Kind="Tag" />
        <Button
            AutomationProperties.AutomationId="IngestButton"
            Command="{x:Bind ViewModel.IngestCommand}"
            Content="正式入库" />
        <Button
            AutomationProperties.AutomationId="RetryInboxItemButton"
            Command="{x:Bind ViewModel.RetryCommand}"
            Content="重试" />
        <Button
            AutomationProperties.AutomationId="ClearMissingItemButton"
            Command="{x:Bind ViewModel.ClearMissingCommand}"
            Content="清除失效记录" />
      </StackPanel>
    </ScrollViewer>
  </Border>
</Grid>
~~~

Settings and Taxonomy remain full-width pages. Library receives its own right pane in Task 12. This preserves the visual three-column model on media pages while removing cross-page UI state from ShellPage.

- [ ] **Step 3: Implement the preview control with fallback behavior**

MediaPreviewControl accepts PreviewDescriptor and:

- uses Image for MediaKind.Image;
- uses MediaPlayerElement for MediaKind.Video;
- stops and clears MediaPlayer.Source when selection changes or control unloads;
- shows a media-type icon when descriptor is missing, file is missing, thumbnail fails, or playback fails;
- displays “无法预览此文件，但仍可安全入库。” for ReadyToTag preview failures;
- never changes InboxItem state.

Use a trusted file Uri created only from PreviewDescriptor.TrustedFullPath. Do not expose a textbox or command accepting paths.

- [ ] **Step 4: Implement taxonomy pickers without a new navigation step**

TaxonomyPickerControl uses AutoSuggestBox plus an ItemsControl of selected terms. Pressing Enter:

1. selects an exact existing suggestion when present;
2. otherwise calls TaxonomyService.CreateAsync for the configured TaxonomyKind;
3. appends the returned ID if absent;
4. triggers MetadataEditorViewModel debounce.

SeriesPicker allows one selected item and replaces it. AuthorPicker and TagPicker append multiple unique items. Every selected token has a remove button.

Keep this logic in TaxonomyPickerViewModel, not code-behind. Add tests proving exact suggestions are reused, a missing term is created once, duplicate author/tag IDs are ignored, and a second series replaces the first.

- [ ] **Step 5: Wire page lifetime and verify XAML**

InboxPage.Loaded calls ViewModel.LoadAsync once per activation-safe ViewModel gate. Unloaded flushes metadata and stops video but does not dispose singleton application services.

Run:

~~~powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: XAML compiles, desktop tests PASS, and build has 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Controls apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Shell apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): render the native inbox workspace"
~~~

---

### Task 12: Implement the formal library workspace and guarded Explorer reveal

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryCardViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryInspectorViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/IAssetRevealService.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/WindowsAssetRevealService.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibraryViewModelTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Services/WindowsAssetRevealServiceTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs

**Interfaces:**
- Consumes: LibraryQueryService, MetadataEditorViewModel behavior, MediaPreviewResolver, and MetadataOwner(MediaAsset, id).
- Produces: LibraryViewModel.LoadAsync, SearchText, Items, SelectedItem, and RevealSelectedCommand.
- Produces: LibraryCardViewModel with AutomationId, DisplayName, ActualFilename, ThumbnailSource, ThumbnailVisibility, FallbackVisibility, and FallbackGlyph.
- Produces: IAssetRevealService.RevealAsync(Guid assetId, CancellationToken); it never accepts a raw path.
- Produces: stable AutomationIds LibrarySearchBox, LibraryGrid, LibraryCard_{id}, LibraryInspectorPane, and RevealAssetButton.

- [ ] **Step 1: Write failing ViewModel tests**

~~~csharp
[Fact]
public async Task Search_reloads_cards_and_selection_loads_formal_metadata()
{
    var library = new RecordingLibraryQueryService();
    var metadata = new RecordingMetadataEditor();
    var viewModel = new LibraryViewModel(library, metadata, new FakeRevealService());

    viewModel.SearchText = "杭州";
    await viewModel.ApplySearchAsync(CancellationToken.None);
    await viewModel.SelectAsync(library.Result[0], CancellationToken.None);

    Assert.Equal("杭州", library.LastQuery);
    Assert.Equal(
        new MetadataOwner(MetadataOwnerKind.MediaAsset, library.Result[0].Id),
        metadata.LoadedOwner);
}

[Fact]
public async Task Reveal_command_passes_only_registered_asset_id()
{
    var reveal = new FakeRevealService();
    LibraryViewModel viewModel = CreateViewModel(reveal);
    await viewModel.SelectAsync(AssetCard, CancellationToken.None);

    await viewModel.RevealSelectedCommand.ExecuteAsync(null);

    Assert.Equal(AssetCard.Id, reveal.RevealedId);
}
~~~

Run the focused Desktop tests and expect failure because LibraryViewModel does not exist.

- [ ] **Step 2: Implement the guarded reveal service**

WindowsAssetRevealService calls MediaPreviewResolver.ResolveAsync with a MediaAsset owner. It requires Exists true, then starts explorer.exe with an ArgumentList containing /select, and the trusted full path:

~~~csharp
var startInfo = new ProcessStartInfo
{
    FileName = "explorer.exe",
    UseShellExecute = false
};
startInfo.ArgumentList.Add("/select,");
startInfo.ArgumentList.Add(descriptor.TrustedFullPath);
processStarter.Start(startInfo);
~~~

Wrap process creation behind IProcessStarter so tests assert arguments without opening Explorer. Missing assets return asset.file_missing and do not start a process.

- [ ] **Step 3: Implement LibraryViewModel and inspector**

LibraryViewModel:

- loads newest formal assets on activation;
- applies case-insensitive actual/display-name search;
- resolves trusted image card thumbnails by asset ID and uses a generic video/image glyph when no thumbnail is available;
- uses single selection;
- flushes previous metadata before changing selection;
- loads formal MetadataOwner into the shared editor behavior;
- reloads the selected card after a successful logical metadata save;
- never offers physical rename or deletion.

LibraryInspectorViewModel exposes the actual filename as read-only, MediaPreviewResolver output, MetadataEditorViewModel, and RevealSelectedCommand.

- [ ] **Step 4: Replace the LibraryPage empty state**

Use this concrete page structure:

~~~xml
<Grid ColumnDefinitions="*,360">
  <Grid Grid.Column="0" RowDefinitions="Auto,*">
    <AutoSuggestBox
        Margin="20,16,20,12"
        AutomationProperties.AutomationId="LibrarySearchBox"
        QueryIcon="Find"
        PlaceholderText="搜索文件名或显示名称"
        Text="{x:Bind ViewModel.SearchText, Mode=TwoWay}" />
    <GridView
        Grid.Row="1"
        AutomationProperties.AutomationId="LibraryGrid"
        IsItemClickEnabled="True"
        ItemsSource="{x:Bind ViewModel.Items, Mode=OneWay}"
        SelectedItem="{x:Bind ViewModel.SelectedItem, Mode=TwoWay}"
        SelectionMode="Single">
      <GridView.ItemTemplate>
        <DataTemplate x:DataType="pages:LibraryCardViewModel">
          <Border Width="190" Padding="10" CornerRadius="8"
                  AutomationProperties.AutomationId="{x:Bind AutomationId}">
            <Grid RowDefinitions="120,Auto,Auto">
              <Image Source="{x:Bind ThumbnailSource}" Stretch="UniformToFill"
                     Visibility="{x:Bind ThumbnailVisibility}" />
              <FontIcon Glyph="{x:Bind FallbackGlyph}"
                        Visibility="{x:Bind FallbackVisibility}" />
              <TextBlock Grid.Row="1" Margin="0,8,0,0"
                         Text="{x:Bind DisplayName}" TextTrimming="CharacterEllipsis" />
              <TextBlock Grid.Row="2" Opacity="0.7"
                         Text="{x:Bind ActualFilename}" TextTrimming="CharacterEllipsis" />
            </Grid>
          </Border>
        </DataTemplate>
      </GridView.ItemTemplate>
    </GridView>
  </Grid>
  <Border
      Grid.Column="1"
      AutomationProperties.AutomationId="LibraryInspectorPane"
      BorderThickness="1,0,0,0">
    <ScrollViewer>
      <StackPanel Padding="20" Spacing="12">
        <controls:MediaPreviewControl
            Descriptor="{x:Bind ViewModel.Inspector.Preview, Mode=OneWay}" />
        <TextBlock Text="{x:Bind ViewModel.Inspector.ActualFilename, Mode=OneWay}" />
        <TextBox
            Header="显示名称"
            Text="{x:Bind ViewModel.Inspector.Metadata.DisplayName, Mode=TwoWay}" />
        <controls:TaxonomyPickerControl Kind="Author" />
        <controls:TaxonomyPickerControl Kind="Series" />
        <controls:TaxonomyPickerControl Kind="Tag" />
        <Button
            AutomationProperties.AutomationId="RevealAssetButton"
            Command="{x:Bind ViewModel.RevealSelectedCommand}"
            Content="打开所在目录" />
      </StackPanel>
    </ScrollViewer>
  </Border>
</Grid>
~~~

Do not include:

- physical filename textbox;
- delete button;
- batch selection;
- author/series/tag filters.

- [ ] **Step 5: Run focused tests and XAML build**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryViewModelTests|FullyQualifiedName~WindowsAssetRevealServiceTests"
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

Expected: tests PASS and XAML build has 0 warnings and 0 errors.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryViewModel.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryCardViewModel.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryInspectorViewModel.cs apps/media-desktop/src/TraceFold.Media.Desktop/Services apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): browse the formal media library"
~~~

---

### Task 13: Finish taxonomy, settings, composition, and clean shutdown

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyViewModel.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Services/MetadataEditorRegistry.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/TaxonomyViewModelTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Services/MetadataEditorRegistryTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage2LoggingPrivacyTests.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsViewModel.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Lifecycle/AppShutdownCoordinator.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml.cs
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs
- Modify: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsViewModelTests.cs
- Modify: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Lifecycle/AppShutdownCoordinatorTests.cs
- Modify: apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/ArchitectureTests.cs

**Interfaces:**
- Produces: TaxonomyViewModel.SelectedKind, SearchText, Terms, CreateCommand, RenameCommand, and DeleteCommand.
- Produces: TaxonomyViewModel.KindOptions containing Author/作者, Series/系列, and Tag/标签.
- Produces: IMetadataEditorRegistry.Register(IMetadataEditorFlushable) and FlushAllAsync(CancellationToken).
- Extends: SettingsViewModel.AutoScanEnabled, LastScanAtText, LastScanSummary, and SaveAutoScanCommand.
- Extends: AppShutdownCoordinator to flush editors before stopping the host.
- Consumes: IMediaOperationCoordinator.StopAcceptingAndDrainAsync before host stop.

- [ ] **Step 1: Write failing taxonomy ViewModel tests**

~~~csharp
[Fact]
public async Task Delete_is_disabled_for_referenced_term()
{
    TaxonomyTermDto referenced = Term(referenceCount: 2);
    TaxonomyViewModel viewModel = CreateViewModel(referenced);
    await viewModel.SelectAsync(referenced, CancellationToken.None);

    Assert.False(viewModel.DeleteCommand.CanExecute(null));
    Assert.Equal("该词条已被 2 项资料引用，不能删除。", viewModel.StatusMessage);
}

[Fact]
public async Task Rename_collision_keeps_original_display_name()
{
    TaxonomyViewModel viewModel = CreateCollisionViewModel();
    await viewModel.RenameAsync("Existing", CancellationToken.None);

    Assert.Equal("词条名称已存在。", viewModel.StatusMessage);
    Assert.Equal("Original", viewModel.SelectedTerm!.DisplayName);
}
~~~

- [ ] **Step 2: Implement the taxonomy page**

TaxonomyPage uses a NavigationView or segmented selector for Author, Series, and Tag, a search box, a list showing DisplayName and ReferenceCount, and explicit New, Rename, and Delete buttons.

Implement this structure with a concrete three-item selector:

~~~xml
<Grid Padding="24" RowDefinitions="Auto,Auto,*">
  <ComboBox
      AutomationProperties.AutomationId="TaxonomyKindSelector"
      DisplayMemberPath="DisplayName"
      ItemsSource="{x:Bind ViewModel.KindOptions, Mode=OneWay}"
      SelectedValue="{x:Bind ViewModel.SelectedKind, Mode=TwoWay}"
      SelectedValuePath="Kind" />
  <Grid Grid.Row="1" ColumnDefinitions="*,Auto,Auto,Auto" Margin="0,12,0,12">
    <AutoSuggestBox
        AutomationProperties.AutomationId="TaxonomySearchBox"
        PlaceholderText="搜索词条" />
    <Button Grid.Column="1" AutomationProperties.AutomationId="TaxonomyCreateButton"
            Command="{x:Bind ViewModel.CreateCommand}" Content="新建" />
    <Button Grid.Column="2" AutomationProperties.AutomationId="TaxonomyRenameButton"
            Command="{x:Bind ViewModel.RenameCommand}" Content="重命名" />
    <Button Grid.Column="3" AutomationProperties.AutomationId="TaxonomyDeleteButton"
            Command="{x:Bind ViewModel.DeleteCommand}" Content="删除" />
  </Grid>
  <ListView
      Grid.Row="2"
      AutomationProperties.AutomationId="TaxonomyList"
      ItemsSource="{x:Bind ViewModel.Terms, Mode=OneWay}"
      SelectedItem="{x:Bind ViewModel.SelectedTerm, Mode=TwoWay}" />
</Grid>
~~~

Use stable AutomationIds:

~~~text
TaxonomyKindSelector
TaxonomySearchBox
TaxonomyList
TaxonomyCreateButton
TaxonomyRenameButton
TaxonomyDeleteButton
~~~

Create and rename use ContentDialog with one text field. Delete of an unreferenced term requires a confirmation dialog; referenced terms never open the dialog.

- [ ] **Step 3: Extend settings tests and ViewModel**

~~~csharp
[Fact]
public async Task LoadAsync_shows_auto_scan_off_and_last_result()
{
    var preferences = new FakeScanPreferencesService(
        new ScanPreferences(false, Instant, "扫描完成：发现 2 个文件，新增 1 项。"));
    SettingsViewModel viewModel = CreateSettingsViewModel(preferences);

    await viewModel.LoadAsync(CancellationToken.None);

    Assert.False(viewModel.AutoScanEnabled);
    Assert.Contains("扫描完成", viewModel.LastScanSummary);
}
~~~

SettingsPage adds:

~~~xml
<ToggleSwitch
    AutomationProperties.AutomationId="AutoScanToggle"
    Header="自动扫描"
    OffContent="已关闭"
    OnContent="每 5 秒检查一次" />
<TextBlock
    AutomationProperties.AutomationId="LastScanStatus"
    TextWrapping="Wrap" />
~~~

Changing the toggle calls ScanPreferencesService.SetAutoScanEnabledAsync. If directories are not saved, revert the toggle and show “请先保存三个工作目录。”

- [ ] **Step 4: Flush all active editors before host shutdown**

MetadataEditorRegistry holds weak references or explicit disposable registrations for active MetadataEditorViewModel instances. FlushAllAsync snapshots the registry and awaits every FlushAsync.

Add a failing shutdown-order test:

~~~csharp
[Fact]
public async Task StopAsync_flushes_metadata_before_stopping_host()
{
    var sequence = new List<string>();
    var coordinator = new AppShutdownCoordinator(
        new RecordingEditorRegistry(sequence),
        new RecordingMediaOperationCoordinator(sequence),
        new RecordingHostStopper(sequence),
        NullLogger<AppShutdownCoordinator>.Instance);

    await coordinator.StopAsync(CancellationToken.None);

    Assert.Equal(["flush", "drain", "stop"], sequence);
}
~~~

If flush fails, log MetadataFlushDuringShutdownFailed and continue draining. If drain times out, log MediaOperationDrainTimedOut and continue host shutdown; every interrupted ingest has a durable recovery stage and still retains its source. The normal 5-second shutdown timeout applies to flush plus drain plus host stop.

- [ ] **Step 5: Complete composition and startup ordering**

DesktopCompositionRoot must:

~~~csharp
services.AddMediaApplication();
services.AddMediaInfrastructure(paths.RootDirectory);
services.AddSingleton<InboxViewModel>();
services.AddSingleton<InboxInspectorViewModel>();
services.AddSingleton<LibraryViewModel>();
services.AddSingleton<LibraryInspectorViewModel>();
services.AddSingleton<TaxonomyViewModel>();
services.AddSingleton<SettingsViewModel>();
services.AddSingleton<IDebounceScheduler, ProductionDebounceScheduler>();
services.AddTransient<MetadataEditorViewModel>();
services.AddSingleton<IMetadataEditorRegistry, MetadataEditorRegistry>();
services.AddSingleton<IAssetRevealService, WindowsAssetRevealService>();
services.AddSingleton<IUiDispatcher, DispatcherQueueAdapter>();
~~~

Keep Windows-only services in Desktop. Infrastructure and Application must never reference Desktop. Extend ArchitectureTests to assert Desktop is the only project referencing WinUI/Windows App SDK types.

App.xaml.cs resolves the registry when constructing AppShutdownCoordinator. StartupCoordinator opens MediaRuntimeGate only after migration, valid directories, and IngestRecoveryService reconciliation.

Stage 2 services may log operation IDs, stable error codes, controlled file paths, sizes, hashes, and states. They must not log MetadataSnapshotJson, display names, author names, series names, tag names, or media bytes. Stage2LoggingPrivacyTests capture a failed ingest log and assert the secret display/term strings are absent while OperationId and error code are present.

- [ ] **Step 6: Run focused and full pre-package tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --filter "FullyQualifiedName~TaxonomyViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~MetadataEditorRegistryTests|FullyQualifiedName~AppShutdownCoordinatorTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --filter FullyQualifiedName~Stage2LoggingPrivacyTests
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

Expected: all tests PASS; build has 0 warnings and 0 errors.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests apps/media-desktop/tests/TraceFold.Media.Architecture.Tests apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage2LoggingPrivacyTests.cs
git commit -m "feat(media-desktop): complete stage-two native workflows"
~~~

---

### Task 14: Add a safe packaged flow probe, publish Stage 2, and prepare manual acceptance

**Files:**
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage2PackageProbe.cs
- Create: apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage2PackageProbeOptions.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage2PackageProbeOptionsTests.cs
- Create: apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration/OrdinaryMediaFlowTests.cs
- Create: docs/media-desktop-stage2-test-guide.md
- Modify: apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs
- Modify: scripts/publish-media-desktop.ps1
- Modify: scripts/smoke-media-desktop-package.ps1
- Modify: apps/media-desktop/README.md

**Interfaces:**
- Produces: TraceFold.Media.exe --stage2-package-probe-root <absolute-temp-root> --stage2-package-probe-token <guid>.
- Produces: stage2-probe-result.json inside the verified probe root.
- Produces: dist/TraceFold-MediaDesktop-Stage2-win-x64 and matching ZIP.

- [ ] **Step 1: Add an end-to-end non-UI integration test**

Use a real temporary SQLite database and three temporary directories:

~~~csharp
[Fact]
public async Task Ordinary_png_flows_from_inbox_to_formal_library()
{
    await using OrdinaryMediaFixture fixture = await OrdinaryMediaFixture.CreateAsync();
    string source = fixture.WriteOnePixelPng("photo.png");

    await fixture.Scanner.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
    fixture.AdvanceBy(TimeSpan.FromSeconds(2));
    await fixture.Scanner.ScanAsync(ScanTrigger.StabilityCheck, CancellationToken.None);
    InboxCardDto card = Assert.Single(await fixture.Inbox.SearchAsync(
        new InboxQuery(null, null), CancellationToken.None));
    await fixture.Metadata.SaveAsync(
        new MetadataOwner(MetadataOwnerKind.InboxItem, card.Id),
        new MetadataUpdate("封面", [fixture.AuthorId], fixture.SeriesId, [fixture.TagId], 0),
        CancellationToken.None);

    IngestResult result = await fixture.Ingest.IngestAsync(card.Id, CancellationToken.None);

    Assert.Equal(IngestOutcome.Completed, result.Outcome);
    Assert.False(File.Exists(source));
    Assert.True(File.Exists(Path.Combine(fixture.Folders.LibraryDirectory, "photo.png")));
    LibraryAssetDto asset = Assert.Single(await fixture.Library.SearchAsync(null, CancellationToken.None));
    Assert.Equal("封面", asset.DisplayName);
    Assert.Equal("photo.png", asset.ActualFilename);
}
~~~

Add companion tests for name conflict, registered hash conflict, MP4/M4A RequiresProcessing, preview failure not blocking ordinary ingest, and one failed item not blocking another.

- [ ] **Step 2: Add a deliberately constrained package probe mode**

Program checks probe arguments before AppInstance registration. Stage2PackageProbeOptions.Parse accepts the mode only when:

- the root is an existing direct child of Path.GetTempPath();
- its basename starts with tracefold-media-desktop-stage2-probe-;
- probe-owner.txt exists directly under root;
- probe-owner.txt content exactly equals the supplied GUID token;
- data, inbox, library, and work subdirectories do not pre-exist.

Any validation failure returns non-zero before creating, moving, or deleting a file.

Stage2PackageProbe builds a headless Host with AddMediaApplication and AddMediaInfrastructure only; it must not resolve MainWindow, Page, IFolderPicker, or other WinUI presentation services.

Stage2PackageProbe:

1. creates data, inbox, library, and work below the verified root;
2. initializes a fresh database and saves those directories;
3. writes a built-in one-pixel PNG named probe.png to inbox;
4. performs first scan;
5. waits at least 2 seconds;
6. performs stability scan;
7. saves display name “Stage 2 Probe”;
8. ingests the PNG;
9. verifies source absent, formal file present, one asset present, physical filename unchanged;
10. writes stage2-probe-result.json with Succeeded true and the verified relative paths;
11. stops the host and exits zero.

The probe never accepts an inbox, library, work, source, target, or delete path from command-line input.

- [ ] **Step 3: Update publish naming and package contents**

Change package names to:

~~~text
TraceFold-MediaDesktop-Stage2-win-x64
TraceFold-MediaDesktop-Stage2-win-x64.zip
~~~

Publish checks must reject:

~~~text
*.pdb
python.exe
uvicorn.exe
node.exe
msedge.exe
chrome.exe
ffmpeg.exe
ffprobe.exe
~~~

Keep Windows App SDK self-contained files, XBF, PRI, SQLite native dependencies, and no installer.

- [ ] **Step 4: Extend the PowerShell smoke script**

Retain all Stage 1 checks:

- native window appears;
- second launch redirects and exits zero;
- exactly one package process remains;
- no browser process opens;
- closing the window exits cleanly;
- invalid data root exits non-zero without a headless process.

Then create an exact test-owned temp root and owner marker, invoke package probe mode, wait for exit code zero, parse stage2-probe-result.json, and assert:

~~~powershell
if (-not $result.Succeeded) { throw 'Stage 2 package probe reported failure.' }
if (Test-Path -LiteralPath (Join-Path $probeRoot 'inbox\probe.png')) {
    throw 'Probe source was not removed after verified ingest.'
}
if (-not (Test-Path -LiteralPath (Join-Path $probeRoot 'library\probe.png'))) {
    throw 'Probe formal file is missing.'
}
~~~

The finally block may remove only the exact GUID-named root after rechecking that it is a direct child of the system temp directory and contains the matching owner marker.

- [ ] **Step 5: Write the manual test guide**

docs/media-desktop-stage2-test-guide.md must instruct the user to:

1. create three dedicated test folders;
2. use copies, never the Web version's live directories;
3. test manual refresh and waiting-to-ready transition;
4. test optional metadata and restart persistence;
5. ingest one image and one video and verify unchanged physical names;
6. edit formal display name and verify physical filename unchanged;
7. test MP4/M4A blocked processing;
8. test same-name and same-content conflicts preserving sources;
9. test an unpreviewable file without blocking healthy files;
10. close the app and verify no TraceFold.Media process remains.

State explicitly that clean-guest verification is not available and must be recorded as not executed, not passed.

- [ ] **Step 6: Run final verification from a clean build state**

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1
Get-FileHash -Algorithm SHA256 dist/TraceFold-MediaDesktop-Stage2-win-x64.zip
~~~

Expected:

- restore exits zero;
- Release build reports 0 warnings and 0 errors;
- every Domain, Application, Infrastructure, Desktop, and Architecture test passes;
- publish exits zero;
- smoke prints PASS for the native window and Stage 2 package flow;
- SHA-256 is recorded for handoff.

- [ ] **Step 7: Commit the verified checkpoint**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1 apps/media-desktop/README.md docs/media-desktop-stage2-test-guide.md
git commit -m "build(media-desktop): publish stage-two test checkpoint"
~~~

After this commit, hand the ZIP, SHA-256, and manual test guide to the user. Do not mark Stage 2 accepted or merge its implementation branch until the user reports the manual result. Record the unavailable clean Windows guest test truthfully in the later acceptance document.

---

## Spec coverage

| Approved Stage 2 requirement | Implemented by |
|---|---|
| Domain states, allowed actions, optional metadata cardinality | Tasks 1 and 7 |
| Extensible direct media and MP4/M4A grouping | Task 2 |
| First-level guarded scan and ignored files | Task 3 |
| SQLite model, safe migration, and uniqueness | Task 4 |
| Manual refresh, two observations, late companion, idempotency | Task 5 |
| Auto scan default off, fixed 5-second cycle, last scan result | Tasks 6 and 13 |
| Draft persistence, vocabulary creation/rename/delete, version safety | Tasks 7, 10, and 13 |
| Preview fallback without blocking ordinary ingest | Tasks 8 and 11 |
| Same-name/same-hash blocking and unchanged actual filename | Tasks 8 and 9 |
| Stage, commit, database transaction, post-commit source cleanup | Tasks 8 and 9 |
| Minimal startup recovery and targeted retry | Task 9 |
| Native inbox, library, inspector, taxonomy, and settings UI | Tasks 10–13 |
| Editor flush, hosted-worker shutdown, log privacy | Task 13 |
| Offline ZIP, safe package probe, host manual acceptance | Task 14 |
| Batch actions, processing, conflict choices, advanced filtering deferred | Global Constraints and Tasks 2, 9, and 14 |

---

## Execution checkpoints

After Tasks 1, 4, 9, 11, and 14:

1. run git status --short --branch and confirm only expected task changes exist;
2. run the full solution tests, not only focused filters;
3. inspect the latest commit diff for scope drift;
4. stop and repair any Critical or Important review finding before continuing.

The implementation is complete only after Task 14 produces a passing package probe and a user-testable ZIP. Branch integration remains a separate, user-authorized finishing action after manual acceptance.
