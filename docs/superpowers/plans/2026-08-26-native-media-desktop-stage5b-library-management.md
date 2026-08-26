# Native Media Desktop Stage 5B Library Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add strict-intersection formal-library filtering, stable sorting, Explorer-style multi-selection, one-dialog batch metadata and rule-based display-name editing, persistent naming presets, and one-level versioned undo without touching physical files.

**Architecture:** Desktop owns ephemeral query drafts, selection, dialog state, focus, and undo presentation; Application owns immutable query/selection contracts, pure metadata and naming transformations, preset validation, preview, and batch orchestration; Infrastructure owns translated SQLite queries, versioned preset persistence, and atomic batch apply/undo transactions. The formal-library path reuses Stage 5A's pure metadata mutation semantics but has formal-asset-specific selection and storage ports.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, Serilog, xUnit v3, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev, PowerShell packaging scripts.

**Spec:** docs/superpowers/specs/2026-08-26-native-media-desktop-stage5b-library-management-design.md

## Global Constraints

- Execute in an isolated `codex/native-media-desktop-stage5b-library-management` worktree created with `superpowers:using-git-worktrees`; branch from approved specification commit `41cee7e`.
- Target Windows 11 x64; keep `TraceFold.Media.Desktop` unpackaged, self-contained, single-instance, native-windowed, and fully offline.
- Do not run `git pull`, browse, download dependencies, or update pinned packages. Restore only from `apps/media-desktop/.packages` with failed remote sources ignored.
- Never rename, move, delete, rewrite, or inspect media contents during Stage 5B batch editing. Only SQLite metadata and display-name fields may change.
- Search and every active filter category combine with `AND`. Multiple selected author IDs and multiple selected tag IDs each mean “contains all.”
- Media type, series, and display-name customization state are single-valued filters. Missing-author/tag filters are mutually exclusive with selected terms of the same kind; missing-series is mutually exclusive with a selected series.
- Search or applied-filter changes clear selection and report it; sort changes preserve selection by asset ID and rebuild the ordinal from the new query order.
- Persist only sort field and direction. Search, active filters, selected assets, dialog drafts, and undo receipts are process-session state.
- Batch metadata and generated display names commit in one SQLite transaction. Any missing asset, stale version, invalid taxonomy reference, invalid rule, illegal generated name, or write failure rolls back every selected asset.
- Generated display names are trimmed, non-empty, and at most 1024 characters. Duplicate display names are warnings, not blockers or filesystem conflicts.
- Naming previews and transactional execution call the same pure `NamingRuleEngine`; numbering uses the immutable selection ordinal captured when the dialog opens.
- Actual-filename source strips only the final extension. Current-display-name source uses the explicit stored display name and otherwise falls back to the extensionless actual filename.
- Author and tag placeholders preserve persisted ordinal order; their default separator is `、`.
- Naming presets are manually invoked, versioned SQLite records. Unknown schema/rule versions never run by silently dropping content.
- Keep one in-memory undo receipt. Undo version-checks and atomically restores all changed assets; app close, a later related edit, a later batch edit, or a conflict invalidates it.
- Undo restores asset metadata but never deletes taxonomy terms created by the batch.
- Do not add a batch aggregate, batch/history table, persistent undo stack, task center, regular-expression engine, scripting, conditional rules, automatic preset trigger, library paging, or physical-file command.
- Logs may contain operation ID, asset count, rule kinds, phase, duration, result counts, and stable error codes only. Do not log paths, actual/display/generated names, taxonomy values, rule text, hashes, or preset content.
- All package probes use test-owned direct children of the system temporary directory with an ownership token. Never point probes at user-configured media roots.
- Stage 5A's 561 passing tests are the minimum regression baseline. Every task follows red-green-refactor, ends with focused verification, and makes one reviewable commit.
- Do not commit `dist`, generated databases, logs, temporary workspaces, probe output, FFmpeg binaries, or ignored `.superpowers` session artifacts.

## Before Task 1

- [ ] Create and enter the isolated worktree with `superpowers:using-git-worktrees`, then verify its identity:

```powershell
git status --short --branch
git rev-parse HEAD
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
```

Expected: the tree is clean, `HEAD` is `41cee7e`, and the worktree Git directory differs from the common Git directory.

- [ ] Restore exclusively from the repository cache and prove the Stage 5A baseline:

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

Expected: Release build reports 0 warnings and 0 errors and all 561 baseline tests pass. If the count differs, record the actual count and inspect the difference before editing. If restore, build, or a baseline test fails, stop and report the exact failure.

## File and Responsibility Map

- `Application/Library/LibraryContracts.cs`: immutable query criteria, stable sorting, card version facts, details, and query ports.
- `Application/Library/LibrarySortPreferences.cs`: persisted sort preference validation and service boundary.
- `Infrastructure/Persistence/EfLibraryQueryStore.cs`: translated SQLite search, strict-intersection filters, missing-data predicates, and deterministic sorting.
- `Infrastructure/Persistence/EfLibrarySortPreferenceStore.cs`: only persisted formal-library UI preference.
- `Desktop/Pages/LibraryFilterViewModel.cs`: draft/applied filter state, chips, term search, and contradictory-condition prevention.
- `Desktop/Pages/LibrarySelectionViewModel.cs`: query-order selection, versioned capture, clear, and refresh reconciliation.
- `Desktop/Pages/LibraryViewModel.cs`: page orchestration only; delegates filtering, selection, preview, batch dialog, and undo.
- `Application/Library/NamingContracts.cs`: versioned naming source, rule records, asset input, preview rows, issues, and error codes.
- `Application/Library/NamingRuleEngine.cs`: pure sequential rule execution and final-name validation.
- `Application/Library/NamingPresetContracts.cs` and `NamingPresetService.cs`: preset validation, save/overwrite/delete semantics, and storage ports.
- `Infrastructure/Persistence/NamingPresetRow.cs` and `EfNamingPresetStore.cs`: case-insensitive unique preset persistence and explicit versioned JSON.
- `Application/Library/LibraryBatchContracts.cs`: formal selection, immutable editor snapshots, metadata draft, preview, apply result, and undo state.
- `Application/Library/LibraryBatchPreviewService.cs`: proposed metadata first, then naming, then batch warnings/errors.
- `Application/Library/LibraryBatchEditorService.cs`: prepare/apply/undo orchestration and one in-memory undo receipt.
- `Infrastructure/Persistence/EfLibraryBatchEditStore.cs`: chunked snapshot load, taxonomy resolution, atomic apply, and atomic versioned undo.
- `Desktop/Dialogs/LibraryBatchEdit*`: metadata and naming draft ViewModels, large native dialog, preset/rule controls, preview, and result conversion.
- `Desktop/Diagnostics/PackageProbe/Stage5BLibraryProbeScenario.cs`: guarded package-level filter/apply/undo proof against test-owned data.
- `scripts/publish-media-desktop.ps1` and `scripts/smoke-media-desktop-package.ps1`: Stage 5B checkpoint selection and package evidence.
- `docs/media-desktop-stage5b-test-guide.md`: offline human acceptance steps.
- `docs/media-desktop-stage5b-acceptance.md`: create only after automated/package evidence exists and the user reports manual results.

---

## Phase A — Query Facts, Sorting, and Selection

### Task 1: Define Formal-Library Query and Sort Contracts

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibrarySortPreferences.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryQueryService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibraryQueryContractsTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibrarySortPreferenceServiceTests.cs`

**Interfaces:**
- Produces: `LibraryMediaFilter`, `LibraryDisplayNameFilter`, `LibrarySortField`, and `LibrarySortDirection`.
- Produces: `LibraryQuery.Create(...)` with copied, distinct taxonomy IDs and contradiction validation.
- Extends: `LibraryAssetDto` with `long MetadataVersion` and `bool HasCustomDisplayName`.
- Changes: `ILibraryQueryStore.SearchAsync(LibraryQuery, CancellationToken)` and `ILibraryQueryService.SearchAsync(LibraryQuery, CancellationToken)`.
- Produces: `LibrarySortPreference`, `PersistedLibrarySortPreference(string Field, bool Descending)`, `ILibrarySortPreferenceStore`, `ILibrarySortPreferenceService`, and `LibrarySortPreferenceService`.

- [ ] **Step 1: Write failing query-invariant and preference tests**

```csharp
[Fact]
public void Query_rejects_missing_authors_with_selected_authors()
{
    Assert.Throws<ArgumentException>(() => LibraryQuery.Create(
        searchText: null,
        LibraryMediaFilter.All,
        [Guid.NewGuid()],
        seriesId: null,
        tagIds: [],
        missingAuthors: true,
        missingSeries: false,
        missingTags: false,
        LibraryDisplayNameFilter.All,
        LibrarySortField.CreatedAt,
        LibrarySortDirection.Descending));
}

[Fact]
public void Query_trims_search_and_deduplicates_ids_without_reordering()
{
    Guid first = Guid.NewGuid();
    Guid second = Guid.NewGuid();

    LibraryQuery query = LibraryQuery.Create(
        "  杭州  ", LibraryMediaFilter.Image,
        [first, second, first], null, [], false, false, false,
        LibraryDisplayNameFilter.Customized,
        LibrarySortField.DisplayName,
        LibrarySortDirection.Ascending);

    Assert.Equal("杭州", query.SearchText);
    Assert.Equal([first, second], query.AuthorIds);
}
```

```csharp
[Fact]
public async Task Invalid_persisted_sort_falls_back_to_created_descending()
{
    var store = new StubSortStore(
        new PersistedLibrarySortPreference("unknown", false));
    var service = new LibrarySortPreferenceService(store);

    LibrarySortPreference result = await service.LoadAsync(
        TestContext.Current.CancellationToken);

    Assert.Equal(LibrarySortPreference.Default, result);
}
```

- [ ] **Step 2: Run the tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryQueryContractsTests|FullyQualifiedName~LibrarySortPreferenceServiceTests"
```

Expected: FAIL because the query enums, factory, card version facts, and preference service do not exist.

- [ ] **Step 3: Add exact query and preference contracts**

```csharp
public enum LibraryMediaFilter { All, Image, Video }
public enum LibraryDisplayNameFilter { All, Customized, ActualFilename }
public enum LibrarySortField { CreatedAt, DisplayName, ActualFilename, MediaKind }
public enum LibrarySortDirection { Ascending, Descending }

public sealed record LibraryQuery(
    string? SearchText,
    LibraryMediaFilter Media,
    IReadOnlyList<Guid> AuthorIds,
    Guid? SeriesId,
    IReadOnlyList<Guid> TagIds,
    bool MissingAuthors,
    bool MissingSeries,
    bool MissingTags,
    LibraryDisplayNameFilter DisplayName,
    LibrarySortField SortField,
    LibrarySortDirection SortDirection)
{
    public static LibraryQuery Default { get; } = Create(
        null, LibraryMediaFilter.All, [], null, [], false, false, false,
        LibraryDisplayNameFilter.All,
        LibrarySortField.CreatedAt,
        LibrarySortDirection.Descending);

    public static LibraryQuery Create(
        string? searchText,
        LibraryMediaFilter media,
        IReadOnlyList<Guid> authorIds,
        Guid? seriesId,
        IReadOnlyList<Guid> tagIds,
        bool missingAuthors,
        bool missingSeries,
        bool missingTags,
        LibraryDisplayNameFilter displayName,
        LibrarySortField sortField,
        LibrarySortDirection sortDirection)
    {
        Guid[] authors = CopyValidDistinct(authorIds);
        Guid[] tags = CopyValidDistinct(tagIds);
        if (missingAuthors && authors.Length > 0
            || missingTags && tags.Length > 0
            || missingSeries && seriesId.HasValue)
        {
            throw new ArgumentException("A missing filter cannot carry selected terms.");
        }

        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException("A persisted series id is required.");
        }

        return new LibraryQuery(
            string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim(),
            media, authors, seriesId, tags, missingAuthors, missingSeries,
            missingTags, displayName, sortField, sortDirection);
    }
}

public sealed record LibrarySortPreference(
    LibrarySortField Field,
    LibrarySortDirection Direction)
{
    public static LibrarySortPreference Default { get; } =
        new(LibrarySortField.CreatedAt, LibrarySortDirection.Descending);
}

public sealed record PersistedLibrarySortPreference(
    string Field,
    bool Descending);

public interface ILibrarySortPreferenceStore
{
    Task<PersistedLibrarySortPreference> LoadAsync(
        CancellationToken cancellationToken);

    Task<string?> SaveAsync(
        LibrarySortPreference preference,
        CancellationToken cancellationToken);
}
```

Add the private `CopyValidDistinct` helper using a `HashSet<Guid>` while preserving first occurrence. Reject `Guid.Empty` and undefined enum values.

- [ ] **Step 4: Change the query service to accept only a validated `LibraryQuery`**

```csharp
public sealed class LibraryQueryService(ILibraryQueryStore store)
    : ILibraryQueryService
{
    public Task<IReadOnlyList<LibraryAssetDto>> SearchAsync(
        LibraryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return store.SearchAsync(query, cancellationToken);
    }

    public Task<LibraryAssetDetailsDto?> GetDetailsAsync(
        Guid assetId,
        CancellationToken cancellationToken) =>
        store.GetDetailsAsync(assetId, cancellationToken);
}
```

Implement `LibrarySortPreferenceService` so `LoadAsync` maps invalid persisted values to `Default`, while `SaveAsync` only accepts defined enum values and delegates to the store. Register `ILibrarySortPreferenceService` in Application DI.

- [ ] **Step 5: Run the focused Application tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Library"
```

Expected: PASS; invalid or contradictory filters cannot reach Infrastructure.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Library apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library
git commit -m "feat(media-desktop): define formal library query contracts"
```

### Task 2: Translate Strict Filters and Persist Sort Preference in SQLite

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibraryQueryStore.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibrarySortPreferenceStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/AppSettingsRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260826090000_Stage5BLibraryQuery.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260826090000_Stage5BLibraryQuery.Designer.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/MediaDesktopDbContextModelSnapshot.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/LibraryQueryPersistenceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/Stage5BMigrationTests.cs`

**Interfaces:**
- Consumes: `LibraryQuery` and `LibrarySortPreference` from Task 1.
- Produces: strict SQLite-backed `ILibraryQueryStore` and `ILibrarySortPreferenceStore`.
- Adds: `app_settings.library_sort_field` with default `created_at` and `app_settings.library_sort_descending` with default `1`.

- [ ] **Step 1: Add failing strict-intersection, missing-data, and stable-sort tests**

```csharp
[Fact]
public async Task Authors_and_tags_require_every_selected_term()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    LibraryFixture fixture = await database.SeedLibraryFilterFixtureAsync();
    var store = new EfLibraryQueryStore(database.CreateContextFactory());

    LibraryQuery query = LibraryQuery.Create(
        null, LibraryMediaFilter.All,
        [fixture.AuthorA, fixture.AuthorB], null,
        [fixture.TagA, fixture.TagB],
        false, false, false,
        LibraryDisplayNameFilter.All,
        LibrarySortField.CreatedAt,
        LibrarySortDirection.Descending);

    LibraryAssetDto asset = Assert.Single(await store.SearchAsync(
        query, TestContext.Current.CancellationToken));
    Assert.Equal(fixture.AssetWithEveryTerm, asset.Id);
}

[Fact]
public async Task Explicit_name_equal_to_filename_is_still_customized()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid id = await database.SeedFormalAssetAsync(
        "same.jpg", displayName: "same.jpg", metadataVersion: 3);
    var store = new EfLibraryQueryStore(database.CreateContextFactory());

    IReadOnlyList<LibraryAssetDto> result = await store.SearchAsync(
        LibraryQuery.Create(null, LibraryMediaFilter.All, [], null, [],
            false, false, false, LibraryDisplayNameFilter.Customized,
            LibrarySortField.DisplayName, LibrarySortDirection.Ascending),
        TestContext.Current.CancellationToken);

    Assert.Contains(result, item => item.Id == id && item.HasCustomDisplayName);
}
```

Add named tests `Missing_filters_use_not_exists`, `Search_escapes_percent_and_underscore`, `All_four_sort_fields_have_stable_id_tiebreaker`, and `Six_hundred_assets_return_in_deterministic_order`.

Add these exact test-helper records and methods to `TestMediaDatabase`:

```csharp
internal sealed record LibraryFixture(
    Guid AuthorA,
    Guid AuthorB,
    Guid TagA,
    Guid TagB,
    Guid AssetWithEveryTerm);

public Task<LibraryFixture> SeedLibraryFilterFixtureAsync();

public Task<Guid> SeedFormalAssetAsync(
    string actualFilename,
    string? displayName,
    long metadataVersion,
    MediaKind mediaKind = MediaKind.Image,
    IReadOnlyList<Guid>? authorIds = null,
    Guid? seriesId = null,
    IReadOnlyList<Guid>? tagIds = null);
```

The helper inserts only into its temporary `TestMediaDatabase`; author/tag link ordinals are the supplied list indices.

- [ ] **Step 2: Add failing migration and sort-preference round-trip tests**

```csharp
[Fact]
public async Task Stage5B_query_migration_preserves_stage4_settings()
{
    await using TestMediaDatabase database =
        await TestMediaDatabase.CreateAtMigrationAsync("Stage4ConflictRecovery");
    await database.InsertStage1SettingsAsync("C:\\inbox", "C:\\library", "C:\\work");

    await database.MigrateAsync();

    Assert.Equal("created_at", await database.ScalarAsync<string>(
        "SELECT library_sort_field FROM app_settings WHERE id = 1"));
    Assert.Equal(1L, await database.ScalarAsync<long>(
        "SELECT library_sort_descending FROM app_settings WHERE id = 1"));
}
```

- [ ] **Step 3: Run the Infrastructure tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryQueryPersistenceTests|FullyQualifiedName~Stage5BMigrationTests"
```

Expected: FAIL because the translated query, settings columns, migration, and store do not exist.

- [ ] **Step 4: Implement translated filtering and deterministic ordering**

Use one `IQueryable<MediaAssetRow>` and append one predicate per selected author/tag so every ID must match:

```csharp
foreach (Guid authorId in query.AuthorIds)
{
    Guid captured = authorId;
    rows = rows.Where(asset =>
        asset.Authors.Any(link => link.AuthorId == captured));
}

foreach (Guid tagId in query.TagIds)
{
    Guid captured = tagId;
    rows = rows.Where(asset =>
        asset.Tags.Any(link => link.TagId == captured));
}

if (query.MissingAuthors)
{
    rows = rows.Where(asset => !asset.Authors.Any());
}

if (query.MissingSeries)
{
    rows = rows.Where(asset => asset.SeriesId == null);
}

if (query.MissingTags)
{
    rows = rows.Where(asset => !asset.Tags.Any());
}
```

Translate media, series, customization, and escaped search filters in the same query. Apply the requested primary sort, then `ActualFilename`, then `Id`. Project `MetadataVersion` and `DisplayName != null` directly in SQL instead of loading every row and sorting in memory.

- [ ] **Step 5: Add settings persistence and migration**

Map enum values to the fixed strings `created_at`, `display_name`, `actual_filename`, and `media_kind`. Store direction in `library_sort_descending`. `EfLibrarySortPreferenceStore.LoadAsync` returns default raw values if the settings row is missing; `SaveAsync` returns `settings.directories_required` rather than creating an incomplete settings row.

Register both stores in Infrastructure DI and update the EF model snapshot. Extend the test helper's schema-inspection allowlist with `app_settings`.

- [ ] **Step 6: Run focused and migration regression tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryQueryPersistenceTests|FullyQualifiedName~Stage5BMigrationTests|FullyQualifiedName~Stage2MigrationTests|FullyQualifiedName~Stage3MigrationTests|FullyQualifiedName~Stage4MigrationTests"
```

Expected: PASS; the 600-item test has a stable order and every earlier migration still upgrades.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests
git commit -m "feat(media-desktop): query formal library with strict filters"
```

### Task 3: Model Formal Selection and Query Draft State

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibrarySelectionContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryCardViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibrarySelectionViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/ExistingTaxonomyFilterViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryFilterViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibrarySelectionViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibraryFilterViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibraryViewModelTests.cs`

**Interfaces:**
- Produces: `LibrarySelectionEntry(Guid AssetId, long ExpectedMetadataVersion, int Ordinal)`.
- Produces: `LibraryCardViewModel.SelectionEntry(int ordinal)`.
- Produces: `LibrarySelectionViewModel.ReplaceFromGrid`, `Capture`, `Clear`, and `Reconcile`.
- Produces: `LibraryFilterViewModel.AppliedQuery`, draft fields, term search, `ApplyDraft`, `CancelDraft`, `RemoveChip`, and `ClearAll`.
- Changes: `LibraryViewModel` owns `Selection` and `Filter`, loads saved sort once, and routes every reload through `LibraryQuery`.

- [ ] **Step 1: Write failing selection-order and query-change tests**

```csharp
[Fact]
public void Capture_uses_current_query_order_after_sort()
{
    LibraryCardViewModel first = Card("first.jpg", version: 4);
    LibraryCardViewModel second = Card("second.jpg", version: 7);
    var selection = new LibrarySelectionViewModel();
    selection.ReplaceFromGrid([first, second], [second, first]);

    IReadOnlyList<LibrarySelectionEntry> snapshot = selection.Capture();

    Assert.Equal(second.Id, snapshot[0].AssetId);
    Assert.Equal(0, snapshot[0].Ordinal);
    Assert.Equal(7, snapshot[0].ExpectedMetadataVersion);
}

[Fact]
public async Task Applying_filter_clears_selection_but_sorting_preserves_it()
{
    LibraryViewModel viewModel = CreateLoadedViewModel();
    viewModel.Selection.ReplaceFromGrid(
        [viewModel.Items[0]], viewModel.Items.ToArray());

    await viewModel.ApplyFilterAsync(FilterWithMissingTags(), CancellationToken.None);
    Assert.Equal(0, viewModel.Selection.SelectedCount);
    Assert.Equal("查询条件已变化，已取消选择。", viewModel.StatusMessage);

    viewModel.Selection.ReplaceFromGrid(
        [viewModel.Items[0]], viewModel.Items.ToArray());
    await viewModel.ChangeSortAsync(
        LibrarySortField.ActualFilename,
        LibrarySortDirection.Ascending,
        CancellationToken.None);
    Assert.Equal(1, viewModel.Selection.SelectedCount);
}
```

- [ ] **Step 2: Write failing filter-draft tests**

```csharp
[Fact]
public void Missing_author_clears_selected_author_terms_in_the_draft()
{
    var filter = CreateFilter();
    filter.Authors.Select(Author("Alice"));

    filter.MissingAuthors = true;

    Assert.Empty(filter.Authors.SelectedTerms);
    Assert.True(filter.MissingAuthors);
}

[Fact]
public void Cancel_discards_draft_without_changing_applied_chips()
{
    var filter = CreateFilter();
    filter.BeginEdit();
    filter.MissingTags = true;
    filter.CancelDraft();

    Assert.False(filter.AppliedQuery.MissingTags);
    Assert.Empty(filter.ActiveChips);
}
```

- [ ] **Step 3: Run the Desktop tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibrarySelectionViewModelTests|FullyQualifiedName~LibraryFilterViewModelTests|FullyQualifiedName~LibraryViewModelTests"
```

Expected: FAIL because formal selection, filter drafts, version capture, and persisted sort loading do not exist.

- [ ] **Step 4: Implement selection and existing-term-only filter state**

Mirror the proven `InboxSelectionViewModel` identity/reconciliation behavior but emit `LibrarySelectionEntry`. `ExistingTaxonomyFilterViewModel` consumes `ITaxonomySearchService`, accepts only returned existing `TaxonomyTermDto` values, preserves selection order, and never calls `ITaxonomyService.CreateAsync`.

Represent active chips with stable keys:

```csharp
public sealed record LibraryFilterChipViewModel(
    string Key,
    string Label,
    Action Remove);
```

`ApplyDraft` constructs one validated `LibraryQuery`, replaces applied state, and rebuilds chips. `CancelDraft` restores draft fields from applied state.

- [ ] **Step 5: Refactor `LibraryViewModel` into orchestration**

Load `LibrarySortPreference` once in `LoadAsync`, call `ILibraryQueryService.SearchAsync(Filter.AppliedQuery, ...)`, reconcile selection by ID after reload, and expose these methods:

```csharp
public Task ApplyFilterAsync(
    LibraryQuery query,
    CancellationToken cancellationToken);

public Task ChangeSortAsync(
    LibrarySortField field,
    LibrarySortDirection direction,
    CancellationToken cancellationToken);

public void ReplaceSelectionFromGrid(
    IEnumerable<LibraryCardViewModel> selected);

public void ClearSelection();
```

Search or filter changes call `Selection.Clear()` before reload. Sort saves the preference, reloads, and calls `Selection.Reconcile` without clearing.

- [ ] **Step 6: Run focused Desktop tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Library"
```

Expected: PASS; query changes and sort changes have different, tested selection behavior.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Library/LibrarySelectionContracts.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages
git commit -m "feat(media-desktop): model formal library selection"
```

### Task 4: Connect Filter Flyout, Sort Controls, and Extended Selection

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibrarySelectionSummaryViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryViewModel.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibraryViewModelTests.cs`

**Interfaces:**
- Consumes: query/filter/selection state from Tasks 1–3.
- Produces: filter-button flyout, active chips, sort controls, `GridView.SelectionMode="Extended"`, multi-selection summary, and stable Automation IDs.
- Preserves: the existing single-item inspector and taxonomy pickers for exactly one selected asset.

- [ ] **Step 1: Add failing structural and summary tests**

```csharp
[Fact]
public void Library_page_exposes_filter_sort_and_extended_selection_automation_ids()
{
    string xaml = File.ReadAllText(ProjectFile("Pages", "LibraryPage.xaml"));

    Assert.Contains("AutomationProperties.AutomationId=\"LibraryFilterButton\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibrarySortField\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibrarySortDirection\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibraryActiveFilters\"", xaml);
    Assert.Contains("SelectionMode=\"Extended\"", xaml);
}

[Fact]
public void Multi_selection_summary_counts_images_and_videos()
{
    LibrarySelectionSummaryViewModel summary =
        LibrarySelectionSummaryViewModel.From([
            Card(MediaKind.Image), Card(MediaKind.Image), Card(MediaKind.Video)]);

    Assert.Equal(3, summary.TotalCount);
    Assert.Equal(2, summary.ImageCount);
    Assert.Equal(1, summary.VideoCount);
}
```

- [ ] **Step 2: Run the tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~LibraryViewModelTests"
```

Expected: FAIL because the controls, Extended Selection, and summary do not exist.

- [ ] **Step 3: Add the compact filter/sort surface**

In `LibraryPage.xaml`, keep the current three-column shell and replace the single search row with a compact command row. The filter button owns a `Flyout` whose controls bind to draft state; its footer has `LibraryFilterApplyButton` and `LibraryFilterCancelButton`. Put active chips below the row in `LibraryActiveFilters`. Do not expand a permanent filter sidebar.

Add these stable IDs: `LibrarySearchBox`, `LibrarySortField`, `LibrarySortDirection`, `LibraryFilterButton`, `LibraryFilterFlyout`, `LibraryFilterApplyButton`, `LibraryFilterCancelButton`, `LibraryActiveFilters`, `LibraryClearFiltersButton`, `LibraryGrid`, `LibraryClearSelectionButton`, and `LibrarySelectionSummary`.

- [ ] **Step 4: Connect grid selection without per-item re-entry**

Set `SelectionMode="Extended"`. In `OnSelectionChanged`, pass `LibraryGrid.SelectedItems.Cast<LibraryCardViewModel>()` once to `ViewModel.ReplaceSelectionFromGrid`. Then:

```csharp
if (ViewModel.Selection.IsSingleSelection)
{
    await ViewModel.SelectAsync(
        ViewModel.Selection.SingleSelectedItem,
        CancellationToken.None);
    SynchronizePickers();
}
else
{
    await ViewModel.SelectAsync(null, CancellationToken.None);
}
```

Use a synchronization guard when `LibraryViewModel` reconciles selection after reload so programmatic updates do not recursively rebuild the selection.

- [ ] **Step 5: Switch the inspector by selection count**

Show the existing inspector only for one selection. Show `LibrarySelectionSummary` for two or more selections with total/image/video counts and “取消选择”. Keep the 0-selection empty preview. Do not add the batch-edit button until Task 11 gives it a working dialog command.

- [ ] **Step 6: Run Desktop and full query regression tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Library|FullyQualifiedName~MediaWorkspaceStructureTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryQueryPersistenceTests"
```

Expected: PASS; single-item editing remains available and multi-selection never renders single-item inputs.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages
git commit -m "feat(media-desktop): add formal library filter and selection UI"
```

---

## Phase B — Pure Naming and Persistent Presets

### Task 5: Implement the Pure Sequential Naming Engine

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/NamingContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/NamingRuleEngine.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/NamingRuleEngineTests.cs`

**Interfaces:**
- Produces: `NamingSource`, `NamingPlacement`, `NamingCaseMode`, `NamingBracketPair`, `NamingIssueSeverity`.
- Produces sealed rule records implementing `INamingRule`: `PrefixNamingRule`, `SuffixNamingRule`, `ReplaceNamingRule`, `RemoveTextNamingRule`, `RemoveBracketContentNamingRule`, `CaseNamingRule`, `SequenceNamingRule`, and `MetadataTemplateNamingRule`.
- Produces: `NamingDefinition(int SchemaVersion, NamingSource Source, IReadOnlyList<INamingRule> Rules)`.
- Produces: `NamingAssetInput`, `NamingPreviewRow`, `NamingIssue`, `NamingPreviewResult`.
- Produces: `INamingRuleEngine.Preview(NamingDefinition, IReadOnlyList<NamingAssetInput>, IReadOnlySet<string>)`.

Use these exact immutable input/result shapes:

```csharp
public sealed record NamingAssetInput(
    Guid AssetId,
    string ActualFilename,
    string? ExplicitDisplayName,
    IReadOnlyList<string> Authors,
    string? Series,
    IReadOnlyList<string> Tags,
    int Ordinal);

public sealed record NamingIssue(
    NamingIssueSeverity Severity,
    string Code,
    int? RuleIndex);

public sealed record NamingPreviewRow(
    Guid AssetId,
    int Ordinal,
    string SourceName,
    string? GeneratedName,
    bool IsChanged,
    IReadOnlyList<NamingIssue> Issues);

public sealed record NamingPreviewResult(
    IReadOnlyList<NamingPreviewRow> Rows,
    bool CanApply,
    int WarningCount,
    int ErrorCount);
```

- [ ] **Step 1: Write failing source, order, and rule tests**

```csharp
[Fact]
public void Actual_filename_source_strips_only_the_final_extension()
{
    NamingPreviewRow row = Preview(
        actualFilename: "archive.part.webp",
        explicitDisplayName: null,
        NamingSource.ActualFilename,
        [new SuffixNamingRule("-done")]);

    Assert.Equal("archive.part-done", row.GeneratedName);
}

[Fact]
public void Rules_execute_in_user_order_and_disabled_rules_are_skipped()
{
    NamingPreviewRow row = Preview(
        "photo.jpg", null, NamingSource.ActualFilename,
        [
            new PrefixNamingRule("A-"),
            new ReplaceNamingRule("photo", "image", CaseSensitive: false),
            new SuffixNamingRule("-ignored", Enabled: false),
            new CaseNamingRule(NamingCaseMode.Upper)
        ]);

    Assert.Equal("A-IMAGE", row.GeneratedName);
}
```

- [ ] **Step 2: Write failing metadata, numbering, validation, and duplicate tests**

```csharp
[Fact]
public void Template_uses_stable_metadata_order_and_selection_ordinal()
{
    NamingPreviewRow row = PreviewAsset(new NamingAssetInput(
        Guid.NewGuid(), "clip.mp4", null,
        Authors: ["Alice", "Bob"],
        Series: "旅行",
        Tags: ["夜景", "城市"],
        Ordinal: 2),
        new MetadataTemplateNamingRule(
            "{series} - {authors} - {number}",
            MultiValueSeparator: "、",
            SequenceStart: 1,
            SequenceStep: 1,
            SequenceMinimumDigits: 3));

    Assert.Equal("旅行 - Alice、Bob - 003", row.GeneratedName);
}

[Theory]
[InlineData("", "naming.empty")]
[InlineData("{series}", "naming.placeholder_missing")]
public void Invalid_result_blocks_the_batch(string template, string errorCode)
{
    NamingPreviewResult result = PreviewBatch(
        [Asset(series: null)],
        [new MetadataTemplateNamingRule(template)]);

    Assert.False(result.CanApply);
    Assert.Contains(result.Rows[0].Issues, issue => issue.Code == errorCode);
}
```

Add exact cases for 1024 accepted/1025 rejected, literal case-sensitive and insensitive replacement, literal removal, each supported bracket pair, unmatched bracket retention, invariant upper/lower, sequence prefix/suffix/start/step/padding, current-name fallback, duplicate-within-batch warning, duplicate-with-existing warning, no-op row, and undefined rule parameters.

- [ ] **Step 3: Run the naming tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NamingRuleEngineTests"
```

Expected: FAIL because no naming contracts or engine exist.

- [ ] **Step 4: Add immutable rule records and stable issue codes**

```csharp
public interface INamingRule
{
    bool Enabled { get; }
    string Kind { get; }
}

public sealed record PrefixNamingRule(string Text, bool Enabled = true)
    : INamingRule
{
    public string Kind => "prefix";
}

public sealed record SuffixNamingRule(string Text, bool Enabled = true)
    : INamingRule { public string Kind => "suffix"; }
public sealed record ReplaceNamingRule(
    string Search, string Replacement, bool CaseSensitive, bool Enabled = true)
    : INamingRule { public string Kind => "replace"; }
public sealed record RemoveTextNamingRule(
    string Text, bool CaseSensitive, bool Enabled = true)
    : INamingRule { public string Kind => "remove_text"; }
public sealed record RemoveBracketContentNamingRule(
    NamingBracketPair Pair, bool Enabled = true)
    : INamingRule { public string Kind => "remove_brackets"; }
public sealed record CaseNamingRule(
    NamingCaseMode Mode, bool Enabled = true)
    : INamingRule { public string Kind => "case"; }
public sealed record SequenceNamingRule(
    int Start,
    int Step,
    int MinimumDigits,
    NamingPlacement Placement,
    string Separator,
    bool Enabled = true) : INamingRule
{
    public string Kind => "sequence";
}

public sealed record MetadataTemplateNamingRule(
    string Template,
    string MultiValueSeparator = "、",
    int SequenceStart = 1,
    int SequenceStep = 1,
    int SequenceMinimumDigits = 1,
    bool Enabled = true) : INamingRule
{
    public string Kind => "metadata_template";
}
```

Define every remaining rule as a sealed record with explicit parameters. `NamingBracketPair` contains `Round`, `Square`, `Curly`, `FullWidthRound`, and `CjkSquare`; map them respectively to `()`, `[]`, `{}`, `（）`, and `【】`. A sequence requires `Start >= 0`, `Step > 0`, and `MinimumDigits` from 1 through 10. A replace/remove-text search value must be non-empty. Metadata templates support only `{name}`, `{authors}`, `{series}`, `{tags}`, and `{number}`; an unknown token is `naming.placeholder_unknown`. `NamingDefinition.CurrentSchemaVersion` is `1`. Copy the rule list in the factory and reject null entries and undefined enum values.

- [ ] **Step 5: Implement one pure evaluation path**

`NamingRuleEngine.Preview` must:

1. order inputs by `Ordinal` and reject duplicate IDs/ordinals;
2. derive the source string;
3. apply enabled rules in list order;
4. trim and validate the final result;
5. create row-level issues with `RuleIndex` when one rule fails;
6. calculate duplicate warnings after all rows have valid generated names;
7. return `CanApply = rows.All(row => row has no Error issue)`.

Use `StringComparison.Ordinal` or `OrdinalIgnoreCase` exactly as configured. Use invariant upper/lower conversion. Do not use regex APIs.

- [ ] **Step 6: Run naming tests and Application regression**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NamingRuleEngineTests|FullyQualifiedName~BatchMetadataMutationTests"
```

Expected: PASS; naming does not depend on EF, WinUI, disk, or current culture.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Library/NamingContracts.cs apps/media-desktop/src/TraceFold.Media.Application/Library/NamingRuleEngine.cs apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/NamingRuleEngineTests.cs
git commit -m "feat(media-desktop): add pure display naming engine"
```

### Task 6: Persist Versioned Naming Presets

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/NamingPresetContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/NamingPresetService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/NamingPresetRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/NamingRuleSerializer.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfNamingPresetStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260826100000_Stage5BNamingPresets.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260826100000_Stage5BNamingPresets.Designer.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/MediaDesktopDbContextModelSnapshot.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/NamingPresetServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/NamingPresetPersistenceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/Stage5BMigrationTests.cs`

**Interfaces:**
- Produces: `NamingPresetDto`, `NamingPresetSaveRequest`, `NamingPresetSaveResult`, `NamingPresetDeleteResult`.
- Produces: `INamingPresetStore` and `INamingPresetService` list/load/save/delete methods.
- Produces: explicit schema-version-1 JSON with a `kind` discriminator for every naming rule.
- Adds: `naming_presets` table with unique normalized name.

```csharp
public sealed record NamingPresetDto(
    Guid Id,
    string Name,
    NamingDefinition? Definition,
    bool CanExecute,
    string? ErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NamingPresetSaveRequest(
    Guid? Id,
    string Name,
    NamingDefinition Definition,
    bool OverwriteExisting);

public sealed record NamingPresetSaveResult(
    bool Succeeded,
    NamingPresetDto? Preset,
    string? ErrorCode);

public sealed record NamingPresetDeleteResult(
    bool Succeeded,
    string? ErrorCode);
```

- [ ] **Step 1: Write failing service semantics tests**

```csharp
[Fact]
public async Task Save_as_rejects_duplicate_name_without_explicit_overwrite()
{
    var store = new InMemoryNamingPresetStore(Preset("Cleanup"));
    var service = new NamingPresetService(store);

    NamingPresetSaveResult result = await service.SaveAsync(
        new NamingPresetSaveRequest(
            Id: null,
            Name: " cleanup ",
            Definition: Definition(new PrefixNamingRule("x")),
            OverwriteExisting: false),
        TestContext.Current.CancellationToken);

    Assert.False(result.Succeeded);
    Assert.Equal("naming.preset.name_conflict", result.ErrorCode);
}

[Fact]
public async Task Unsupported_schema_is_visible_but_not_executable()
{
    var store = new InMemoryNamingPresetStore(
        Preset("Future", schemaVersion: 2));
    var service = new NamingPresetService(store);

    NamingPresetDto preset = Assert.Single(await service.ListAsync(
        TestContext.Current.CancellationToken));

    Assert.False(preset.CanExecute);
    Assert.Equal("naming.preset.version_unsupported", preset.ErrorCode);
}
```

- [ ] **Step 2: Write failing migration and JSON round-trip tests**

```csharp
[Fact]
public async Task Every_rule_round_trips_with_order_and_enabled_state()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    var store = new EfNamingPresetStore(database.CreateContextFactory());
    NamingDefinition definition = NamingDefinition.Create(
        NamingSource.ActualFilename,
        [
            new PrefixNamingRule("pre-"),
            new SuffixNamingRule("-post", Enabled: false),
            new ReplaceNamingRule("old", "new", CaseSensitive: false),
            new RemoveTextNamingRule("draft", CaseSensitive: true),
            new RemoveBracketContentNamingRule(NamingBracketPair.Round),
            new CaseNamingRule(NamingCaseMode.Lower),
            new SequenceNamingRule(1, 1, 3, NamingPlacement.Suffix, "-"),
            new MetadataTemplateNamingRule("{series}-{authors}-{number}")
        ]);
    NamingPresetDto saved = Assert.NotNull((await store.SaveAsync(
        new NamingPresetSaveRequest(
            null, "Every rule", definition, OverwriteExisting: false),
        CancellationToken.None)).Preset);

    NamingPresetDto loaded = Assert.NotNull(await store.LoadAsync(
        saved.Id, CancellationToken.None));

    Assert.Equal(saved.Definition, loaded.Definition);
}
```

Add exact tests for case-insensitive normalized uniqueness, overwrite preserving ID/CreatedAt, delete not changing assets, malformed JSON returning a non-executable DTO, and Stage 4-to-latest migration preserving assets/settings.

- [ ] **Step 3: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NamingPresetServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NamingPresetPersistenceTests|FullyQualifiedName~Stage5BMigrationTests"
```

Expected: FAIL because preset contracts, serializer, table, migration, and stores do not exist.

- [ ] **Step 4: Add the versioned persistence model**

```csharp
internal sealed class NamingPresetRow
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public required string NormalizedName { get; set; }
    public int SchemaVersion { get; set; }
    public required string Source { get; set; }
    public required string RulesJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
```

Configure `DisplayName`/`NormalizedName` at 512 characters, `Source` at 32, a unique `NormalizedName` index, and snake-case columns. The migration creates only `naming_presets`; it never changes media assets.

- [ ] **Step 5: Implement explicit serialization and service validation**

`NamingRuleSerializer` writes a version-1 array of objects containing `kind`, `enabled`, and only the parameters for that rule. Deserialize through a closed `switch` on `kind`. Unknown kinds, invalid fields, or unsupported schema versions return `naming.preset.rule_unsupported` or `naming.preset.version_unsupported`; never skip the invalid object.

`NamingPresetService` trims the display name, normalizes with the same invariant name rules used for taxonomy-style uniqueness, validates the `NamingDefinition` through the engine, and requires `OverwriteExisting=true` before replacing a case-insensitive name match.

- [ ] **Step 6: Run preset, migration, and engine tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NamingPresetServiceTests|FullyQualifiedName~NamingRuleEngineTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NamingPresetPersistenceTests|FullyQualifiedName~Stage5BMigrationTests"
```

Expected: PASS; every supported rule round-trips and future/unknown content cannot execute.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence
git commit -m "feat(media-desktop): persist versioned naming presets"
```

---

## Phase C — Combined Preview, Atomic Apply, and Undo

### Task 7: Build Formal Batch Draft and Pure Combined Preview

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/BatchMetadataContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/BatchMetadataService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchPreviewService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/BatchMetadataMutationTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibraryBatchPreviewServiceTests.cs`

**Interfaces:**
- Produces: selection-independent `BatchMetadataDraft` with the existing mode/set/series edit records.
- Preserves: `BatchMetadataCommand.Create(selection, draft)` and all Stage 5A behavior.
- Produces: `LibraryBatchAssetDto`, `LibraryBatchEditorDto`, `LibraryBatchEditDraft`, `LibraryBatchPreviewDto`, and `LibraryBatchPreviewRowDto`.
- Produces: `ILibraryBatchPreviewService.Preview(LibraryBatchEditorDto, LibraryBatchEditDraft)`.

```csharp
public sealed record LibraryBatchAssetDto(
    LibrarySelectionEntry Selection,
    string ActualFilename,
    string? ExplicitDisplayName,
    IReadOnlyList<TaxonomyTermDto> Authors,
    TaxonomyTermDto? Series,
    IReadOnlyList<TaxonomyTermDto> Tags);

public sealed record LibraryBatchEditorDto(
    IReadOnlyList<LibraryBatchAssetDto> Assets,
    IReadOnlyList<BatchTermCoverageDto> Authors,
    IReadOnlyList<BatchTermCoverageDto> Tags,
    IReadOnlySet<string> UnselectedDisplayNames);

public sealed record LibraryBatchPreviewRowDto(
    Guid AssetId,
    int Ordinal,
    string ActualFilename,
    string CurrentDisplayName,
    string? GeneratedDisplayName,
    bool MetadataChanged,
    bool DisplayNameChanged,
    IReadOnlyList<NamingIssue> Issues);

public sealed record LibraryBatchPreviewDto(
    IReadOnlyList<LibraryBatchPreviewRowDto> Rows,
    bool CanApply,
    int ChangedCount,
    int WarningCount,
    int ErrorCount,
    string? ErrorCode);
```

- [ ] **Step 1: Extract a failing selection-independent metadata-draft test**

```csharp
[Fact]
public void Inbox_command_wraps_the_same_validated_metadata_draft()
{
    BatchMetadataDraft draft = BatchMetadataDraft.Create(
        BatchMetadataMode.Replace,
        new(BatchMetadataFieldOperation.Clear, []),
        BatchMetadataSeriesEdit.Unchanged,
        BatchMetadataSetEdit.Unchanged);

    BatchMetadataCommand command = BatchMetadataCommand.Create(
        [new InboxSelectionEntry(Guid.NewGuid(), 2, 0)], draft);

    Assert.Same(draft, command.Draft);
    Assert.Equal(BatchMetadataFieldOperation.Clear, command.Draft.Authors.Operation);
}
```

- [ ] **Step 2: Write failing combined-preview ordering tests**

```csharp
[Fact]
public void Proposed_metadata_is_visible_to_naming_placeholders()
{
    LibraryBatchEditorDto editor = Editor(
        Asset(authors: [Term("Old")], version: 3));
    LibraryBatchEditDraft draft = Draft(
        metadata: ReplaceAuthorsWithNew("New"),
        naming: Definition(new MetadataTemplateNamingRule("{authors}-{number}")));

    LibraryBatchPreviewDto preview = new LibraryBatchPreviewService(
        new NamingRuleEngine()).Preview(editor, draft);

    LibraryBatchPreviewRowDto row = Assert.Single(preview.Rows);
    Assert.Equal("New-1", row.GeneratedDisplayName);
    Assert.True(preview.CanApply);
}

[Fact]
public void Any_naming_error_blocks_metadata_only_changes_too()
{
    LibraryBatchPreviewDto preview = Preview(
        metadata: AddTag("Keep"),
        naming: Definition(new MetadataTemplateNamingRule("{series}")),
        asset: Asset(series: null));

    Assert.False(preview.CanApply);
    Assert.Contains(preview.Rows[0].Issues,
        issue => issue.Code == "naming.placeholder_missing");
}
```

Add exact cases for metadata-only, naming-only, no-op draft rejection, remove coverage, explicit clear, new deferred taxonomy names, duplicates as warnings, and deterministic ordinal order.

- [ ] **Step 3: Run focused Application tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataMutationTests|FullyQualifiedName~LibraryBatchPreviewServiceTests"
```

Expected: FAIL because `BatchMetadataDraft` and formal batch preview contracts do not exist.

- [ ] **Step 4: Extract `BatchMetadataDraft` without weakening Stage 5A validation**

Move the existing operation-matrix validation into:

```csharp
public sealed record BatchMetadataDraft(
    BatchMetadataMode Mode,
    BatchMetadataSetEdit Authors,
    BatchMetadataSeriesEdit Series,
    BatchMetadataSetEdit Tags)
{
    public static BatchMetadataDraft Create(
        BatchMetadataMode mode,
        BatchMetadataSetEdit authors,
        BatchMetadataSeriesEdit series,
        BatchMetadataSetEdit tags);
}
```

`BatchMetadataCommand.Create` validates the inbox selection, then stores the validated draft. Keep compatibility properties `Mode`, `Authors`, `Series`, and `Tags` if existing consumers need them during the refactor.

- [ ] **Step 5: Implement combined pure preview**

`LibraryBatchPreviewService` validates asset IDs/versions/ordinals, resolves existing and deferred taxonomy names in memory, applies `BatchMetadataMutation` to each asset, converts the proposed term order to `NamingAssetInput`, calls `INamingRuleEngine.Preview`, and merges metadata/name changes into one row. It must not call a store or create a taxonomy term.

```csharp
public sealed record LibraryBatchEditDraft(
    BatchMetadataDraft? Metadata,
    NamingDefinition? Naming)
{
    public static LibraryBatchEditDraft Create(
        BatchMetadataDraft? metadata,
        NamingDefinition? naming) =>
        metadata is null && naming is null
            ? throw new ArgumentException("At least one batch edit is required.")
            : new(metadata, naming);
}
```

- [ ] **Step 6: Run preview and Stage 5A metadata tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchPreviewServiceTests|FullyQualifiedName~BatchMetadata"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataDialogViewModelTests"
```

Expected: PASS; the extraction does not alter Stage 5A command semantics.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/tests/TraceFold.Media.Application.Tests apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/BatchMetadataDialogViewModelTests.cs
git commit -m "feat(media-desktop): preview combined formal asset edits"
```

### Task 8: Apply Metadata and Display Names in One SQLite Transaction

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchEditorService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/MetadataEditorService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibraryBatchEditStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibraryBatchEditorServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/MetadataEditorServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/LibraryBatchEditPersistenceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage5BLoggingPrivacyTests.cs`

**Interfaces:**
- Produces: `ILibraryBatchEditStore.PrepareAsync`, `ApplyAsync`, and result/receipt records.
- Produces: `ILibraryBatchEditorService.PrepareAsync`, `Preview`, and `ApplyAsync`.
- Produces: one opaque `LibraryBatchUndoReceipt` held by Application, not Desktop.
- Consumes: `BatchMetadataMutation`, `INamingRuleEngine`, `LibrarySelectionEntry`, and deferred taxonomy references.

Define the storage boundary with these exact result shapes:

```csharp
public sealed record LibraryBatchStoreResult(
    bool Succeeded,
    int ChangedCount,
    int UnchangedCount,
    int WarningCount,
    LibraryBatchUndoReceipt? UndoReceipt,
    string? ErrorCode);

public sealed record LibraryBatchUndoReceipt(
    Guid OperationId,
    IReadOnlyList<LibraryBatchUndoItem> Items);

public sealed record LibraryBatchUndoItem(
    Guid AssetId,
    long ExpectedPostVersion,
    string? PreviousDisplayName,
    Guid? PreviousSeriesId,
    IReadOnlyList<Guid> PreviousAuthorIds,
    IReadOnlyList<Guid> PreviousTagIds);

public enum LibraryActivityKind
{
    SingleMetadataChanged,
    BatchChanged,
    UndoCompleted
}

public sealed class LibraryActivityEventArgs(
    LibraryActivityKind kind,
    IReadOnlyList<Guid> assetIds) : EventArgs
{
    public LibraryActivityKind Kind { get; } = kind;
    public IReadOnlyList<Guid> AssetIds { get; } = assetIds.ToArray();
}

public interface ILibraryActivityNotifier
{
    event EventHandler<LibraryActivityEventArgs>? Changed;
    void Publish(LibraryActivityKind kind, IReadOnlyList<Guid> assetIds);
}
```

- [ ] **Step 1: Write failing prepare and service-gate tests**

```csharp
[Fact]
public async Task Apply_is_not_sent_to_store_when_preview_has_errors()
{
    var store = new RecordingLibraryBatchEditStore(Editor(Asset(series: null)));
    var service = CreateService(store);
    LibraryBatchEditDraft draft = Draft(
        metadata: AddTag("Safe"),
        naming: Definition(new MetadataTemplateNamingRule("{series}")));

    LibraryBatchApplyResult result = await service.ApplyAsync(
        Selection(), draft, TestContext.Current.CancellationToken);

    Assert.False(result.Succeeded);
    Assert.Equal("library.batch.preview_invalid", result.ErrorCode);
    Assert.Equal(0, store.ApplyCallCount);
}
```

- [ ] **Step 2: Write failing atomicity and version tests**

```csharp
[Fact]
public async Task Metadata_and_names_commit_together_once()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(3);
    var store = CreateStore(database);

    LibraryBatchStoreResult result = await store.ApplyAsync(
        fixture.Selection,
        Draft(ReplaceAuthors("Alice"),
            Definition(new MetadataTemplateNamingRule("{authors}-{number}"))),
        CancellationToken.None);

    Assert.True(result.Succeeded);
    IReadOnlyList<PersistedLibraryAssetSnapshot> rows =
        await database.ReadFormalAssetsAsync(fixture.AssetIds);
    Assert.Equal(["Alice-1", "Alice-2", "Alice-3"],
        rows.Select(row => row.DisplayName));
    Assert.All(rows, row => Assert.Equal(1, row.MetadataVersion));
}

[Fact]
public async Task Trigger_failure_rolls_back_every_asset_and_new_taxonomy_term()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    LibraryBatchFixture fixture = await database.SeedLibraryBatchFixtureAsync(3);
    await database.CreateFormalAssetUpdateFailureTriggerAsync(fixture.AssetIds[1]);

    LibraryBatchStoreResult result = await CreateStore(database).ApplyAsync(
        fixture.Selection,
        Draft(AddAuthor("Transient"), Definition(new PrefixNamingRule("X-"))),
        CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.All(await database.ReadFormalAssetsAsync(fixture.AssetIds), row =>
    {
        Assert.Null(row.DisplayName);
        Assert.Equal(0, row.MetadataVersion);
        Assert.Empty(row.AuthorNames);
    });
    Assert.Equal(0, await database.CountTaxonomyTermsAsync(
        TaxonomyKind.Author, "Transient"));
}
```

Add exact tests for stale one-of-many versions, missing asset, invalid existing taxonomy ID, 1025-character recomputed name, duplicates committed with warnings, no-op assets not incremented, proposed metadata used by naming, and 600 assets chunked inside one transaction.

Add these exact test-helper records and methods:

```csharp
internal sealed record LibraryBatchFixture(
    IReadOnlyList<Guid> AssetIds,
    IReadOnlyList<LibrarySelectionEntry> Selection);

internal sealed record PersistedLibraryAssetSnapshot(
    Guid AssetId,
    string? DisplayName,
    Guid? SeriesId,
    IReadOnlyList<string> AuthorNames,
    IReadOnlyList<string> TagNames,
    long MetadataVersion);

public Task<LibraryBatchFixture> SeedLibraryBatchFixtureAsync(int count);
public Task<IReadOnlyList<PersistedLibraryAssetSnapshot>>
    ReadFormalAssetsAsync(IReadOnlyList<Guid> assetIds);
public Task CreateFormalAssetUpdateFailureTriggerAsync(Guid assetId);
public Task SetFormalAssetVersionAsync(Guid assetId, long version);
```

- [ ] **Step 3: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEditorServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEditPersistenceTests|FullyQualifiedName~Stage5BLoggingPrivacyTests"
```

Expected: FAIL because formal batch store/service and receipt do not exist.

- [ ] **Step 4: Implement chunked immutable snapshot loading**

`PrepareAsync` loads each selected asset, explicit display name, metadata version, ordered author/tag term DTOs, series, and display names of unselected assets for duplicate warnings. Chunk IDs at 200. Reject duplicate IDs/ordinals, missing assets, or version mismatches with stable `library.batch.*` codes.

Do not pass `RelativePath`, `NormalizedRelativePath`, `Sha256`, or a full filesystem path into any Application DTO.

- [ ] **Step 5: Implement one transactional apply path**

Within one `BeginTransactionAsync` scope:

1. re-read and version-check every selected asset;
2. resolve/create deferred taxonomy tokens;
3. apply proposed metadata in ordinal order;
4. build naming inputs from resolved post-edit term display names;
5. call the same `INamingRuleEngine.Preview` and reject any error;
6. update changed `DisplayName`, `SeriesId`, author links, and tag links;
7. increment each changed asset's `MetadataVersion` exactly once;
8. capture before/after snapshots in `LibraryBatchUndoReceipt`;
9. commit, then return counts/warnings/receipt.

Catch only expected validation, `DbUpdateException`, and `SqliteException` for stable results; let cancellation propagate after rollback. Publish no user data in logs.

- [ ] **Step 6: Implement the Application service gate and notification**

`LibraryBatchEditorService.ApplyAsync` calls `PrepareAsync`, runs pure preview, and only then calls store apply. It replaces any previous undo receipt only after a successful commit. Return:

```csharp
public sealed record LibraryBatchApplyResult(
    bool Succeeded,
    int ChangedCount,
    int UnchangedCount,
    int WarningCount,
    bool CanUndo,
    string? ErrorCode);
```

Add the lightweight `ILibraryActivityNotifier` above and publish one `BatchChanged` event after successful apply. Inject it into `MetadataEditorService`; after a successful save whose owner kind is `MediaAsset`, publish `SingleMetadataChanged` with that one asset ID. Register the notifier as a singleton in Application DI. `LibraryBatchEditorService` listens only to `SingleMetadataChanged` when deciding whether a stored undo receipt is related; `LibraryViewModel` subscribes to all three kinds in Task 11 for coalesced refresh.

- [ ] **Step 7: Run focused, privacy, and metadata regression tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatch|FullyQualifiedName~NamingRuleEngineTests|FullyQualifiedName~BatchMetadata"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEditPersistenceTests|FullyQualifiedName~Stage5BLoggingPrivacyTests|FullyQualifiedName~MetadataPersistenceTests"
```

Expected: PASS; injected failure leaves all selected assets and deferred taxonomy creation unchanged.

- [ ] **Step 8: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests
git commit -m "feat(media-desktop): atomically edit formal asset batches"
```

### Task 9: Add One-Level Versioned Undo

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchEditorService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibraryBatchEditStore.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibraryBatchEditorServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/LibraryBatchEditPersistenceTests.cs`

**Interfaces:**
- Produces: `LibraryBatchUndoState(bool IsAvailable, int AffectedCount)` and `UndoStateChanged` event.
- Produces: `ILibraryBatchEditorService.UndoLastAsync` and `InvalidateUndoFor(Guid assetId)`.
- Produces: `ILibraryBatchEditStore.UndoAsync(LibraryBatchUndoReceipt, CancellationToken)`.

```csharp
public sealed record LibraryBatchUndoState(
    bool IsAvailable,
    int AffectedCount)
{
    public static LibraryBatchUndoState Unavailable { get; } = new(false, 0);
}

public sealed record LibraryBatchUndoResult(
    bool Succeeded,
    int RestoredCount,
    string? ErrorCode);
```

- [ ] **Step 1: Write failing in-memory lifecycle tests**

```csharp
[Fact]
public async Task Successful_apply_replaces_the_previous_receipt_and_exposes_one_undo()
{
    var store = SuccessfulStore();
    LibraryBatchEditorService service = CreateService(store);

    await service.ApplyAsync(Selection(AssetA), DraftA, CancellationToken.None);
    await service.ApplyAsync(Selection(AssetB), DraftB, CancellationToken.None);

    Assert.True(service.UndoState.IsAvailable);
    Assert.Equal(1, service.UndoState.AffectedCount);
    await service.UndoLastAsync(CancellationToken.None);
    Assert.Equal(AssetB, store.LastUndoReceipt!.Items.Single().AssetId);
}

[Fact]
public async Task Related_single_edit_invalidates_but_unrelated_edit_does_not()
{
    LibraryBatchEditorService service = await AppliedServiceFor(AssetA);

    service.InvalidateUndoFor(AssetB);
    Assert.True(service.UndoState.IsAvailable);
    service.InvalidateUndoFor(AssetA);
    Assert.False(service.UndoState.IsAvailable);
}
```

- [ ] **Step 2: Write failing atomic undo tests**

```csharp
[Fact]
public async Task Undo_restores_names_terms_and_versions_atomically()
{
    IReadOnlyList<PersistedLibraryAssetSnapshot> before = _before;
    LibraryBatchStoreResult applied = await ApplyFixtureAsync();

    LibraryBatchUndoResult undone = await _store.UndoAsync(
        Assert.NotNull(applied.UndoReceipt), CancellationToken.None);

    Assert.True(undone.Succeeded);
    IReadOnlyList<PersistedLibraryAssetSnapshot> restored =
        await _database.ReadFormalAssetsAsync(_assetIds);
    Assert.Equal(before.Select(item => item.DisplayName),
        restored.Select(item => item.DisplayName));
    Assert.Equal(before.Select(item => string.Join("|", item.AuthorNames)),
        restored.Select(item => string.Join("|", item.AuthorNames)));
    Assert.All(restored, item => Assert.Equal(2, item.MetadataVersion));
}

[Fact]
public async Task One_changed_post_version_rejects_the_entire_undo()
{
    LibraryBatchStoreResult applied = await ApplyFixtureAsync();
    await _database.SetFormalAssetVersionAsync(_assetIds[1], 99);

    LibraryBatchUndoResult undone = await _store.UndoAsync(
        Assert.NotNull(applied.UndoReceipt), CancellationToken.None);

    Assert.False(undone.Succeeded);
    Assert.Equal("library.batch.undo.version_conflict", undone.ErrorCode);
    Assert.NotEqual(_before, await _database.ReadFormalAssetsAsync(_assetIds));
}
```

Add exact cases for a second undo returning `library.batch.undo.unavailable`, a failed new batch preserving the prior receipt, a successful new batch replacing it, and created taxonomy terms remaining after undo.

- [ ] **Step 3: Run focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEditorServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEditPersistenceTests"
```

Expected: FAIL because undo state, invalidation, and storage restore are not implemented.

- [ ] **Step 4: Implement atomic restore against post-apply versions**

For every receipt item, verify current `MetadataVersion == ExpectedPostVersion` before writing anything. Restore explicit display name, series, ordered author links, and ordered tag links in one transaction. Set the restored version to `ExpectedPostVersion + 1` so observers never see a version move backward. If any row is missing or stale, rollback every change.

- [ ] **Step 5: Implement one-receipt session state**

Keep the receipt in a private field guarded by a lock. Successful apply replaces it; failed apply leaves the existing receipt unchanged. `UndoLastAsync` atomically takes the receipt for execution, restores it only if an unexpected cancellation occurs before the store begins, and clears it after success or stable conflict. `InvalidateUndoFor` clears only when the receipt contains that asset ID.

Do not serialize the receipt, add a database table, or delete taxonomy rows during undo.

- [ ] **Step 6: Run undo and full batch tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatch"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEditPersistenceTests"
```

Expected: PASS; one stale row causes zero restored rows.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Library apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfLibraryBatchEditStore.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/LibraryBatchEditPersistenceTests.cs
git commit -m "feat(media-desktop): add versioned one-level batch undo"
```

---

## Phase D — Native Batch Editing Experience

### Task 10: Build Testable Batch Dialog State

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryMetadataDraftViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/NamingRuleRowViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryNamingDraftViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialogViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ILibraryBatchEditDialogService.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/LibraryMetadataDraftViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/LibraryNamingDraftViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/LibraryBatchEditDialogViewModelTests.cs`

**Interfaces:**
- Produces: selection-independent metadata draft UI using the existing deferred `BatchTaxonomyPickerViewModel`.
- Produces: naming source, preset list, ordered rule rows, selected-rule parameters, real-time preview rows, issue counts, and save/overwrite/delete commands.
- Produces: `LibraryBatchEditDialogResult(bool Applied, LibraryBatchApplyResult? Result)`.
- Keeps: all user input after validation or apply failure.

- [ ] **Step 1: Write failing metadata-draft and page-sharing tests**

```csharp
[Fact]
public void Enabled_replace_field_requires_value_or_explicit_clear()
{
    LibraryMetadataDraftViewModel viewModel = CreateMetadataDraft();
    viewModel.Mode = BatchMetadataMode.Replace;
    viewModel.ReplaceAuthorsEnabled = true;

    Assert.False(viewModel.TryBuildDraft(out _, out string? error));
    Assert.Equal("已启用的替换字段必须选择资料或明确清空。", error);
}

[Fact]
public async Task Switching_tabs_preserves_both_drafts()
{
    LibraryBatchEditDialogViewModel viewModel = CreateDialog();
    viewModel.Metadata.AddAuthorPicker.InputText = "Alice";
    await viewModel.Metadata.AddAuthorPicker.CommitInputAsync(
        TestContext.Current.CancellationToken);
    viewModel.SelectedTab = LibraryBatchEditTab.Naming;
    viewModel.Naming.AddRule(new PrefixNamingRule("A-"));
    viewModel.SelectedTab = LibraryBatchEditTab.Metadata;

    Assert.Equal("Alice", viewModel.Metadata.AddAuthorPicker.SelectedChoices[0].DisplayText);
    Assert.Single(viewModel.Naming.Rules);
}
```

- [ ] **Step 2: Write failing rule-order, preview, preset, and failure-retention tests**

```csharp
[Fact]
public void Moving_a_rule_recomputes_preview_in_the_new_order()
{
    LibraryNamingDraftViewModel viewModel = CreateNamingDraft();
    viewModel.AddRule(new PrefixNamingRule("A"));
    viewModel.AddRule(new ReplaceNamingRule("Aphoto", "done", true));

    viewModel.MoveRuleUp(viewModel.Rules[1]);

    Assert.Equal("Aphoto", viewModel.PreviewRows[0].GeneratedDisplayName);
}

[Fact]
public async Task Apply_failure_keeps_drafts_and_selects_first_error()
{
    LibraryBatchEditDialogViewModel viewModel = CreateDialog(
        applyResult: new(false, 0, 0, 0, false, "library.batch.version_conflict"));
    viewModel.Naming.AddRule(new PrefixNamingRule("keep-"));

    await viewModel.ApplyCommand.ExecuteAsync(null);

    Assert.Single(viewModel.Naming.Rules);
    Assert.Equal("选择中的资料已变化，请刷新后重试。", viewModel.StatusMessage);
    Assert.True(viewModel.HasFocusedProblem);
}
```

Add exact cases for rule enable/disable/delete, sequence parameter validation, source switching, warning-vs-error apply gate, preset save-as conflict requiring overwrite, unsupported preset disabled, and metadata changes feeding preview.

- [ ] **Step 3: Run the dialog ViewModel tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryMetadataDraftViewModelTests|FullyQualifiedName~LibraryNamingDraftViewModelTests|FullyQualifiedName~LibraryBatchEditDialogViewModelTests"
```

Expected: FAIL because formal batch dialog state does not exist.

- [ ] **Step 4: Implement three focused ViewModels**

`LibraryMetadataDraftViewModel` owns only Add/Remove/Replace field semantics and returns `BatchMetadataDraft?`. `LibraryNamingDraftViewModel` owns source, rules, presets, and preview. `LibraryBatchEditDialogViewModel` coordinates editor snapshot, selected tab, submit state, error focus, and one `ILibraryBatchEditorService.ApplyAsync` call.

Every rule row exposes a stable `AutomationId` from a per-dialog GUID and a 1-based display index. Moving rules changes the underlying list and immediately recomputes preview; it never mutates a saved preset until Save/Overwrite is invoked.

- [ ] **Step 5: Map stable errors and apply gate**

Set `CanApply` only when at least one draft has an actual change, preview has no error, and `IsSubmitting` is false. Map at least these exact codes: `library.batch.version_conflict`, `library.batch.selection_missing`, `library.batch.preview_invalid`, `library.batch.persistence_failed`, `naming.empty`, `naming.too_long`, and `naming.placeholder_missing`.

Warnings increment `WarningCount` but do not disable apply. `ApplyCommand` must not clear draft collections on any failure.

- [ ] **Step 6: Run all dialog-state tests and Stage 5A dialog regression**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatchEdit|FullyQualifiedName~LibraryNamingDraft|FullyQualifiedName~LibraryMetadataDraft|FullyQualifiedName~BatchMetadataDialogViewModelTests"
```

Expected: PASS; Stage 5A's dialog remains unchanged while the shared draft semantics agree.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs
git commit -m "feat(media-desktop): model formal batch edit dialog"
```

### Task 11: Integrate the Large Native Dialog, Refresh, and Undo Banner

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialog.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/LibraryBatchEditDialog.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/WindowsLibraryBatchEditDialogService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/LibraryPage.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/LibraryViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs`

**Interfaces:**
- Consumes: dialog state from Task 10 and `ILibraryBatchEditorService` from Tasks 8–9.
- Produces: working “批量编辑” command, large native two-tab dialog, post-commit merged refresh, undo teaching tip/banner, and related-edit invalidation.
- Preserves: physical files, single-item inspector, single-item metadata save, and reveal command.

- [ ] **Step 1: Add failing orchestration and composition tests**

```csharp
[Fact]
public async Task Batch_edit_passes_only_versioned_selection_and_refreshes_once()
{
    var dialog = new RecordingLibraryBatchEditDialogService(applied: true);
    var library = new RecordingLibraryQueryService();
    LibraryViewModel viewModel = CreateViewModel(
        library: library,
        dialog: dialog);
    await viewModel.LoadAsync(CancellationToken.None);
    viewModel.Selection.ReplaceFromGrid(viewModel.Items.Take(2), viewModel.Items);

    await viewModel.BatchEditCommand.ExecuteAsync(null);

    Assert.Equal(2, dialog.Selection.Count);
    Assert.All(dialog.Selection, entry => Assert.True(entry.ExpectedMetadataVersion >= 0));
    Assert.Equal(2, library.SearchCallCount);
}

[Fact]
public void Undo_state_change_hides_the_banner()
{
    var batchEditor = new RecordingLibraryBatchEditorService(
        new LibraryBatchUndoState(true, 2));
    LibraryViewModel viewModel = CreateViewModel(batchEditor: batchEditor);

    batchEditor.PublishUndoState(LibraryBatchUndoState.Unavailable);

    Assert.False(viewModel.CanUndoBatch);
}
```

Add exact tests for failed dialog preserving selection, successful refresh dropping assets that no longer match filters, undo success refreshing once, undo conflict hiding the banner, and unrelated single save preserving undo.

- [ ] **Step 2: Add failing XAML structure and Automation ID tests**

```csharp
[Fact]
public void Library_batch_dialog_has_two_tabs_rules_preview_and_safe_primary_action()
{
    string xaml = File.ReadAllText(ProjectFile(
        "Dialogs", "LibraryBatchEditDialog.xaml"));

    Assert.Contains("AutomationProperties.AutomationId=\"LibraryBatchMetadataTab\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibraryBatchNamingTab\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibraryNamingPresetList\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibraryNamingRuleList\"", xaml);
    Assert.Contains("AutomationProperties.AutomationId=\"LibraryNamingPreviewList\"", xaml);
    Assert.Contains("仅修改软件中的资料和显示名称", xaml);
}
```

- [ ] **Step 3: Run Desktop tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
```

Expected: FAIL because the dialog, command, composition registration, and undo UI do not exist.

- [ ] **Step 4: Build the confirmed large-dialog layout**

Use a wide `ContentDialog` with:

- header: selected count and immutable physical-file safety text;
- `TabView` or equivalent two-page selector for 资料/命名;
- naming page left column: presets and source;
- naming page middle column: ordered rule list and current-rule editor;
- naming page right column: actual/current/generated preview plus issues;
- footer: cancel, save preset, and `应用 N 项`.

Set stable Automation IDs from the specification. Error focus targets the first preview row or rule control. Closing returns focus to `LibraryGrid`.

- [ ] **Step 5: Wire page commands, notifications, and undo**

Show “批量编辑” only for two or more selected assets. Capture selection once and call `ILibraryBatchEditDialogService.ShowAsync`. On success, coalesce `ILibraryActivityNotifier` and direct result into one reload. Preserve selected IDs that still match the query.

Bind the undo banner to `ILibraryBatchEditorService.UndoState`. “撤销” calls `UndoLastAsync`; success reloads once and reports restored count. Subscribe to `Inspector.Metadata.Saved` and call `InvalidateUndoFor(owner.Id)` only for formal assets.

Register `WindowsLibraryBatchEditDialogService` and the focused dialog ViewModels in `DesktopCompositionRoot`.

- [ ] **Step 6: Run Desktop, Application, and architecture tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Library|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LibraryBatch|FullyQualifiedName~Naming"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release --no-restore
```

Expected: PASS; Desktop has no EF dependency, Application has no WinUI dependency, and batch editing exposes no filesystem command.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): integrate formal library batch editor"
```

---

## Phase E — Package Evidence and Acceptance

### Task 12: Add Stage 5B Probe, Offline Package Checkpoint, and Acceptance Guide

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeOptions.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeEnvironment.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeRunner.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5BLibraryProbeScenario.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5BLibraryProbeResult.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5BPackageProbeTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/PackageProbeRefactorTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/PackageProbeTestSupport.cs`
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Create: `docs/media-desktop-stage5b-test-guide.md`

**Interfaces:**
- Adds: guarded `stage5b-library` package-probe scenario using the existing root/token protocol.
- Adds: `5B` to package and smoke `StageCheckpoint` validation.
- Produces: machine-readable proof for strict filter, combined atomic apply, naming result, undo, physical-file invariance, and cleanup.

```csharp
public sealed record Stage5BPackageProbeResult(
    bool Succeeded,
    bool StrictIntersectionPassed,
    bool AtomicBatchPassed,
    bool NamingPreviewMatchedApply,
    bool UndoPassed,
    bool PhysicalFilesUnchanged,
    bool InternalStorageClean,
    string? ErrorCode);
```

- [ ] **Step 1: Write failing parser, guard, and scenario tests**

```csharp
[Fact]
public void Stage5B_scenario_requires_owned_direct_temp_child_and_token()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
        "tracefold-media-desktop-stage5b-probe-");

    PackageProbeOptionResult result = PackageProbeOptions.Parse([
        "--package-probe-root", root.Path,
        "--package-probe-token", Guid.NewGuid().ToString("D"),
        "--package-probe-scenario", "stage5b-library"]);

    Assert.Equal(PackageProbeOptionState.Invalid, result.State);
    Assert.Equal("probe.owner_mismatch", result.ErrorCode);
}

[Fact]
public async Task Stage5B_probe_filters_applies_undoes_and_keeps_files_identical()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
        "tracefold-media-desktop-stage5b-probe-");
    int exitCode = await PackageProbeRunner.RunAsync(
        PackageProbeRequests.Stage5BLibrary(root),
        "C:\\Program Files\\ffmpeg\\bin",
        TestContext.Current.CancellationToken);
    Stage5BPackageProbeResult result = Assert.NotNull(
        JsonSerializer.Deserialize<Stage5BPackageProbeResult>(
            await File.ReadAllTextAsync(
                Path.Combine(root.Path, "stage5b-probe-result.json"),
                TestContext.Current.CancellationToken)));

    Assert.Equal(0, exitCode);
    Assert.True(result.Succeeded);
    Assert.True(result.StrictIntersectionPassed);
    Assert.True(result.AtomicBatchPassed);
    Assert.True(result.NamingPreviewMatchedApply);
    Assert.True(result.UndoPassed);
    Assert.True(result.PhysicalFilesUnchanged);
    Assert.True(result.InternalStorageClean);
}
```

- [ ] **Step 2: Run probe tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage5BPackageProbeTests|FullyQualifiedName~PackageProbeRefactorTests"
```

Expected: FAIL because the Stage 5B scenario and result do not exist.

- [ ] **Step 3: Implement a data-only guarded Stage 5B scenario**

Add `PackageProbeScenarioKind.Stage5BLibrary`, `PackageProbeOptions.Stage5BLibraryScenario = "stage5b-library"`, `Stage5BRootNamePrefix`, and `Stage5BResultFilename`. Rename the private `ParseStage5A` method to `ParseGeneric`, map only the two exact allowed scenario strings to their enum/prefix, and pass the mapped enum into `ValidateRootOwnership`. Add Stage 5B arms to `PackageProbeEnvironment.ResultFilenameFor`, `RootNamePrefixFor`, and `PackageProbeRunner`; unknown strings remain `probe.invalid_arguments`. Add `PackageProbeRequests.Stage5BLibrary` to test support.

Use existing fixture ingestion to create at least four formal assets. Record SHA-256 and relative filenames before editing. Through public services only:

1. assign overlapping authors/tags and missing-series states;
2. query two authors and two tags and require exactly the all-term asset;
3. prepare a three-asset selection in explicit ordinal order;
4. replace authors and run `{authors}-{number}` naming;
5. require preview names equal persisted names;
6. undo and require metadata/display names restored;
7. recompute file SHA-256 and require names, lengths, and hashes unchanged;
8. require internal work/recovery storage clean.

Write only counts, booleans, IDs, and stable error codes to `stage5b-probe-result.json`. Do not write media names, taxonomy values, rule text, paths, or hashes to the result.

- [ ] **Step 4: Extend publish and smoke scripts**

Change both `ValidateSet` declarations to include `5B`. In smoke selection, map `5B` to `stage5b-library` and `stage5b-probe-result.json`. Validate the seven booleans in the test above, require an empty `ErrorCode`, and retain all existing native-window, single-instance, browser, FFmpeg, log, invalid-config, ownership, and cleanup checks.

Do not weaken the direct-child, reparse-point, marker, process-ownership, or package-local executable checks.

- [ ] **Step 5: Write the human test guide with exact checkpoints**

`docs/media-desktop-stage5b-test-guide.md` must list setup, expected result, and failure evidence for the 17 manual checks in specification section 17. Include explicit checks that:

- two authors/tags mean all, not any;
- search/filter clears selection while sorting preserves it;
- proposed metadata appears in naming preview;
- errors block the whole batch and preserve input;
- duplicates warn but apply;
- undo restores all or none;
- actual filenames, extensions, file sizes, and hashes do not change;
- the package starts without network or browser and exits without residual processes.

- [ ] **Step 6: Run probe tests and the full Release suite**

```powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

Expected: build has 0 warnings and 0 errors and every test passes.

- [ ] **Step 7: Commit probe, scripts, and guide**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1 docs/media-desktop-stage5b-test-guide.md
git commit -m "test(media-desktop): add stage 5b package checkpoint"
```

- [ ] **Step 8: Prove the committed checkpoint can publish and smoke offline**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 5B -FfmpegSourceRoot apps/media-desktop/tools/ffmpeg
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -StageCheckpoint 5B
git rev-parse HEAD
Get-FileHash -Algorithm SHA256 dist/TraceFold-MediaDesktop-Stage5B-win-x64.zip
git status --short
```

Expected: publish creates `dist/TraceFold-MediaDesktop-Stage5B-win-x64` and matching ZIP from the committed `HEAD`; smoke reports native window/single instance/offline/clean-exit plus Stage 5B filter/apply/undo/file-invariance PASS lines. If the bundled tools live at a different already-verified local path, pass that explicit path without downloading anything. Do not commit `dist`. Add `docs/media-desktop-stage5b-acceptance.md` only after the user reports manual results; until then report manual validation as pending.

## Final Verification and Review Gate

- [ ] Confirm no accidental scope expansion or uncommitted generated content:

```powershell
git status --short --branch
git diff --check 41cee7e...HEAD
git diff --stat 41cee7e...HEAD
git log --oneline --decorate 41cee7e..HEAD
```

Expected: only Stage 5B source/tests/migrations/scripts/docs are tracked; `dist`, probe roots, databases, logs, and `.superpowers` content are absent.

- [ ] Run the final offline verification from a clean build state without deleting user data:

```powershell
dotnet clean apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --disable-build-servers
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

Expected: restore uses the local cache, Release build has 0 warnings and 0 errors, and the full suite passes with at least the 561 Stage 5A baseline tests plus Stage 5B tests.

- [ ] Request code review with `superpowers:requesting-code-review`. Resolve every verified issue using `superpowers:receiving-code-review`, rerun the smallest affected test set, then rerun the full Release suite.

- [ ] Rebuild the package from the final reviewed commit and repeat the offline smoke before recording delivery evidence:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 5B -FfmpegSourceRoot apps/media-desktop/tools/ffmpeg
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -StageCheckpoint 5B
git rev-parse HEAD
Get-FileHash -Algorithm SHA256 dist/TraceFold-MediaDesktop-Stage5B-win-x64.zip
git status --short --branch
```

Expected: the package manifest source commit equals the printed `HEAD`; smoke passes; only ignored `dist` artifacts exist outside tracked changes. Do not reuse an earlier ZIP after review fixes.

- [ ] Use `superpowers:verification-before-completion` before reporting implementation complete. Report the exact test count, build result, package path, source commit, ZIP SHA-256, package-smoke result, manual acceptance status, and the still-open clean-Windows release gate.

- [ ] Use `superpowers:finishing-a-development-branch` only after all automated review gates pass and the user chooses how to integrate the branch. Do not merge, push, or delete the worktree without that explicit choice.
