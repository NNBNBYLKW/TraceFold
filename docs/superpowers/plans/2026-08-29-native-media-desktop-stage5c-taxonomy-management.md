# Native Media Desktop Stage 5C-A Taxonomy Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Explorer-style taxonomy workspace that can query reference facts, explicitly merge one or more same-kind source terms into an existing target, atomically delete unused terms, and safely undo the latest taxonomy-maintenance operation without touching physical media.

**Architecture:** Desktop owns query controls, Extended Selection, dialog drafts, focus, and undo presentation; Application owns immutable management/maintenance contracts, the pure merge planner, preview, orchestration, stable errors, and the single in-memory undo receipt; Infrastructure owns translated SQLite queries plus atomic prepare/apply/delete/undo persistence. Existing picker CRUD stays separate, and Stage 5A/5B batch abstractions are not generalized into a universal platform.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, Serilog, xUnit v3, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev, PowerShell packaging scripts.

**Spec:** docs/superpowers/specs/2026-08-29-native-media-desktop-stage5c-taxonomy-management-design.md

## Global Constraints

- Execute in an isolated `codex/native-media-desktop-stage5c-taxonomy-management` worktree created with `superpowers:using-git-worktrees`; branch from the approved plan commit, whose parent specification commit is `dd8e8f245047c1d9af3f9d7fc546149d4a6d75a1`.
- Target Windows 11 x64; keep `TraceFold.Media.Desktop` unpackaged, self-contained, single-instance, native-windowed, and fully offline.
- Do not run `git pull`, browse, download dependencies, or update pinned packages. Restore only from `apps/media-desktop/.packages` with failed remote sources ignored.
- Runtime taxonomy-maintenance commands never rename, move, delete, rewrite, hash, or inspect media contents. Only SQLite taxonomy rows, metadata relationships, metadata versions, and timestamps may change. A guarded test probe may hash only its own fixtures before and after a command to prove physical-file invariance.
- A merge has exactly one existing target and at least one distinct existing source. Every term must share one `TaxonomyKind`; cross-kind merge is always rejected.
- A null final-name override retains the target name. A non-null override must pass `TaxonomyName.Create`. It may reuse a source normalized name but must not collide with a third same-kind term.
- Author and tag migration preserves the existing target ordinal when present; otherwise it replaces the earliest source ordinal. Unrelated terms keep relative order, duplicates are removed, and ordinals become dense `0..n-1`.
- Series migration preserves the zero-or-one invariant.
- Merge both inbox metadata drafts and formal-library metadata. Increment each actually changed draft `Version` and asset `MetadataVersion` exactly once; undo increments versions again and never rewinds them.
- Prepare/preview is not authority. Apply reloads source/target facts, relationships, ordinals, and owner versions in one SQLite transaction; any drift rolls back the whole command.
- Unused means zero inbox-draft references and zero formal-asset references. One referenced term blocks the entire selected delete set; never delete references or partially delete the safe subset.
- Keep one in-memory taxonomy-maintenance undo receipt shared by merge and unused delete. App close clears it; a later successful maintenance operation replaces it; related state drift rejects the entire undo.
- Do not add a migration, maintenance/history table, persistent undo stack, background job, fuzzy matcher, automatic duplicate recommendation, automatic cleanup, cross-kind merge, or physical-file command.
- Logs may contain operation ID, taxonomy kind, source/delete counts, inbox/formal owner counts, phase, duration, success, and stable error codes only. Do not log term names, normalized names, actual/display filenames, paths, metadata values, hashes, dialog input, or preview rows.
- All package probes use test-owned direct children of the system temporary directory with an ownership token. Never point probes at user-configured media roots.
- The current 713 passing tests are the minimum regression baseline. Every implementation task follows red-green-refactor, ends with focused verification, and makes one reviewable commit.
- Do not commit `dist`, generated databases, logs, temporary workspaces, probe output, FFmpeg binaries, or ignored `.superpowers` session artifacts.

## Before Task 1

- [ ] Create and enter the isolated worktree with `superpowers:using-git-worktrees`, then verify identity and cleanliness:

```powershell
git status --short --branch
git rev-parse HEAD
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
```

Expected: the tree is clean, `HEAD` is the approved plan commit, and the worktree Git directory differs from the common Git directory.

- [ ] Restore only from the repository cache and prove the baseline:

```powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

Expected: Release build reports 0 warnings and 0 errors and at least 713 tests pass. If the count differs, inspect and record why before editing. If restore, build, or a baseline test fails, stop and report the exact failure.

## File and Responsibility Map

- `Application/Metadata/TaxonomyManagementContracts.cs`: immutable management query, reference filters, sorting, term rows, and query ports.
- `Application/Metadata/TaxonomyManagementQueryService.cs`: query normalization and delegation only.
- `Infrastructure/Persistence/EfTaxonomyManagementQueryStore.cs`: one translated SQLite management query with inbox/formal reference counts and deterministic sorting.
- `Application/Metadata/TaxonomyMaintenanceContracts.cs`: merge/delete preparation, owner snapshots, plans, previews, results, receipts, and maintenance ports.
- `Application/Metadata/TaxonomyMergePlanner.cs`: pure same-kind relationship transformation and name-collision validation used by preview and persistence.
- `Application/Metadata/TaxonomyMergePreviewBuilder.cs`: user-facing before/after rows derived from a merge plan.
- `Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs`: snapshot preparation plus atomic merge, unused delete, and undo transactions.
- `Application/Metadata/TaxonomyMaintenanceService.cs`: operation logging, single session receipt, apply/delete/undo orchestration, and coalesced inbox/library notifications.
- `Desktop/Pages/TaxonomySelectionViewModel.cs`: query-order Extended Selection, capture, clear, and refresh reconciliation.
- `Desktop/Pages/TaxonomyViewModel.cs`: page orchestration and command availability; no dialog construction, EF, SQL, or relationship mutation.
- `Desktop/Dialogs/TaxonomyMergeDialogViewModel.cs`: same-kind target search, source derivation, optional rename draft, preview refresh, apply state, and input retention.
- `Desktop/Dialogs/TaxonomyMergeDialog.xaml(.cs)`: large native merge dialog, affected-file list, validation focus, and cancellation.
- `Desktop/Dialogs/WindowsTaxonomyMaintenanceDialogService.cs`: main-window/XamlRoot adapter and simple unused-delete confirmation.
- `Desktop/Pages/TaxonomyPage.xaml(.cs)`: three-column shell, selection synchronization, keyboard accelerators, focus return, and rename/create prompts.
- `Desktop/Diagnostics/PackageProbe/Stage5CTaxonomyProbeScenario.cs`: guarded package-level query/merge/delete/undo/version/file-invariance proof.
- `scripts/publish-media-desktop.ps1` and `scripts/smoke-media-desktop-package.ps1`: Stage 5C checkpoint packaging and evidence.
- `docs/media-desktop-stage5c-taxonomy-test-guide.md`: offline human acceptance steps; create an acceptance record only after the user reports results.

---

## Phase A — Management Query and Pure Merge Semantics

### Task 1: Define Taxonomy Management Query Contracts

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyManagementContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyManagementQueryService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyManagementQueryServiceTests.cs`

**Interfaces:**
- Produces: `TaxonomyReferenceFilter`, `TaxonomySortField`, and `TaxonomySortDirection`.
- Produces: `TaxonomyManagementQuery.Create(...)` with trimmed search and enum validation.
- Produces: `TaxonomyManagementTermDto` with `InboxReferenceCount`, `LibraryReferenceCount`, computed `ReferenceCount`, and `UpdatedAtUtc`.
- Produces: `ITaxonomyManagementQueryStore.QueryAsync(...)` and `ITaxonomyManagementQueryService.QueryAsync(...)`.

- [ ] **Step 1: Write failing contract and delegation tests**

```csharp
[Fact]
public void Query_trims_search_and_rejects_unknown_values()
{
    TaxonomyManagementQuery query = TaxonomyManagementQuery.Create(
        TaxonomyKind.Author, "  Alice  ", TaxonomyReferenceFilter.Referenced,
        TaxonomySortField.ReferenceCount, TaxonomySortDirection.Descending);

    Assert.Equal("Alice", query.SearchText);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        TaxonomyManagementQuery.Create(
            (TaxonomyKind)99, null, TaxonomyReferenceFilter.All,
            TaxonomySortField.DisplayName,
            TaxonomySortDirection.Ascending));
}

[Fact]
public async Task Service_forwards_one_immutable_query()
{
    var store = new RecordingManagementQueryStore();
    var service = new TaxonomyManagementQueryService(store);
    TaxonomyManagementQuery query = TaxonomyManagementQuery.Create(
        TaxonomyKind.Tag, "夜景", TaxonomyReferenceFilter.Unused,
        TaxonomySortField.DisplayName, TaxonomySortDirection.Ascending);

    await service.QueryAsync(query, TestContext.Current.CancellationToken);

    Assert.Same(query, store.LastQuery);
}
```

- [ ] **Step 2: Run the focused tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyManagementQueryServiceTests
```

Expected: FAIL because the management contracts and service do not exist.

- [ ] **Step 3: Add the exact query contracts**

```csharp
public enum TaxonomyReferenceFilter { All, Referenced, Unused }
public enum TaxonomySortField { DisplayName, ReferenceCount }
public enum TaxonomySortDirection { Ascending, Descending }

public sealed record TaxonomyManagementQuery(
    TaxonomyKind Kind,
    string? SearchText,
    TaxonomyReferenceFilter ReferenceFilter,
    TaxonomySortField SortField,
    TaxonomySortDirection SortDirection)
{
    public static TaxonomyManagementQuery Create(
        TaxonomyKind kind,
        string? searchText,
        TaxonomyReferenceFilter referenceFilter,
        TaxonomySortField sortField,
        TaxonomySortDirection sortDirection)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(referenceFilter))
            throw new ArgumentOutOfRangeException(nameof(referenceFilter));
        if (!Enum.IsDefined(sortField))
            throw new ArgumentOutOfRangeException(nameof(sortField));
        if (!Enum.IsDefined(sortDirection))
            throw new ArgumentOutOfRangeException(nameof(sortDirection));
        return new(kind, string.IsNullOrWhiteSpace(searchText)
            ? null : searchText.Trim(), referenceFilter, sortField, sortDirection);
    }
}

public sealed record TaxonomyManagementTermDto(
    Guid Id,
    TaxonomyKind Kind,
    string DisplayName,
    string NormalizedName,
    DateTimeOffset UpdatedAtUtc,
    int InboxReferenceCount,
    int LibraryReferenceCount)
{
    public int ReferenceCount => checked(InboxReferenceCount + LibraryReferenceCount);
}
```

Add the two query interfaces with `Task<IReadOnlyList<TaxonomyManagementTermDto>> QueryAsync(TaxonomyManagementQuery, CancellationToken)`. Implement `TaxonomyManagementQueryService` as a null-checking thin wrapper. Register the concrete service and interface singleton in Application DI; do not alter `ITaxonomySearchService`, which remains the lightweight picker boundary.

- [ ] **Step 4: Run the tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyManagementQueryServiceTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyManagementContracts.cs apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyManagementQueryService.cs apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyManagementQueryServiceTests.cs
git commit -m "feat(media-desktop): define taxonomy management query"
```

### Task 2: Execute Reference-Aware Management Queries in SQLite

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyManagementQueryStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyManagementQueryPersistenceTests.cs`

**Interfaces:**
- Consumes: `ITaxonomyManagementQueryStore` and `TaxonomyManagementQuery` from Task 1.
- Produces: one database-side query that returns separate inbox/formal counts and deterministic order without `EfTaxonomyStore.SearchAsync`'s per-term count loop.

- [ ] **Step 1: Write failing SQLite query tests**

```csharp
[Fact]
public async Task Query_splits_inbox_and_library_counts_and_filters_unused()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    TaxonomyManagementFixture fixture =
        await database.SeedTaxonomyManagementFixtureAsync();
    var store = new EfTaxonomyManagementQueryStore(database.CreateContextFactory());

    IReadOnlyList<TaxonomyManagementTermDto> referenced = await store.QueryAsync(
        TaxonomyManagementQuery.Create(
            TaxonomyKind.Author, null, TaxonomyReferenceFilter.Referenced,
            TaxonomySortField.ReferenceCount, TaxonomySortDirection.Descending),
        TestContext.Current.CancellationToken);
    IReadOnlyList<TaxonomyManagementTermDto> unused = await store.QueryAsync(
        TaxonomyManagementQuery.Create(
            TaxonomyKind.Author, null, TaxonomyReferenceFilter.Unused,
            TaxonomySortField.DisplayName, TaxonomySortDirection.Ascending),
        TestContext.Current.CancellationToken);

    TaxonomyManagementTermDto shared = Assert.Single(
        referenced, term => term.Id == fixture.SharedAuthorId);
    Assert.Equal(1, shared.InboxReferenceCount);
    Assert.Equal(1, shared.LibraryReferenceCount);
    Assert.Equal(fixture.UnusedAuthorId, Assert.Single(unused).Id);
}
```

Add tests for `%`, `_`, and `\` literal search; Author/Series/Tag count sources; name ascending/descending; reference-count ties ordered by display name then ID; and one owner contributing only once to one term.

- [ ] **Step 2: Run the tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyManagementQueryPersistenceTests
```

Expected: FAIL because the EF management query store and fixture do not exist.

- [ ] **Step 3: Seed exact inbox/formal relationships in the test helper**

Add `TaxonomyManagementFixture` and `SeedTaxonomyManagementFixtureAsync()` using existing `SeedInboxItemAsync`, `SeedFormalAssetAsync`, and `SeedTaxonomyAsync`. Seed one author referenced by one inbox draft and one formal asset, one formal-only author, one unused author, referenced/unused series and tags, and literal names containing `%`, `_`, and `\`. Use parameterized/interpolated SQL only.

- [ ] **Step 4: Implement one translated query**

Build kind-specific projection expressions with correlated `Count` subqueries so SQLite returns both counts in one round trip. The Author projection is equivalent to:

```csharp
var projected = rows.Select(term => new Projection(
    term.Id,
    term.DisplayName,
    term.NormalizedName,
    term.UpdatedAtUtc,
    context.InboxDraftAuthors.Count(link => link.AuthorId == term.Id),
    context.MediaAssetAuthors.Count(link => link.AuthorId == term.Id)));
```

Use the corresponding series foreign keys or tag link tables for the other kinds. Apply search and referenced/unused predicates before materialization. Apply sort in SQL, then `DisplayName`, then `Id` as deterministic tie breakers. Reuse the current `EscapeLike` semantics and register the store in Infrastructure DI. Do not add a migration.

- [ ] **Step 5: Run focused and legacy taxonomy persistence tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyManagementQueryPersistenceTests|FullyQualifiedName~MetadataPersistenceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyManagementQueryStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyManagementQueryPersistenceTests.cs
git commit -m "feat(media-desktop): query taxonomy reference facts"
```

### Task 3: Define Maintenance Snapshots and the Pure Merge Planner

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMaintenanceContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMergePlanner.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMergePreviewBuilder.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMaintenanceContractsTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMergePlannerTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMergePreviewBuilderTests.cs`

**Interfaces:**
- Produces: immutable term, same-kind name-owner, assignment, owner, merge preparation, delete preparation, plan, preview, result, and undo-receipt records.
- Produces: `TaxonomyMergeSelection.Create(kind, sourceIds, targetId)` and `TaxonomyDeleteSelection.Create(kind, termIds)`.
- Produces: `ITaxonomyMaintenanceStore` and `ITaxonomyMaintenanceService` final signatures used by Tasks 4–11.
- Produces: `TaxonomyMergePlanner.Build(preparation, TaxonomyName? finalName)` and `TaxonomyMergePreviewBuilder.Build(plan)`.

- [ ] **Step 1: Write failing validation and merge-order tests**

```csharp
[Fact]
public void Selection_rejects_target_in_sources_and_empty_sources()
{
    Guid target = Guid.NewGuid();
    Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
        TaxonomyKind.Author, [target], target));
    Assert.Throws<ArgumentException>(() => TaxonomyMergeSelection.Create(
        TaxonomyKind.Author, [], Guid.NewGuid()));
}

[Fact]
public void Author_plan_keeps_existing_target_position_and_dense_order()
{
    TaxonomyMergePreparation preparation = Preparation(
        TaxonomyKind.Author,
        authors: [Unrelated, Target, SourceA, SourceB],
        ordinals: [0, 4, 8, 9]);

    TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(
        preparation, finalName: null);

    Assert.Equal([Unrelated, Target], plan.Owners.Single().After.AuthorIds);
}

[Fact]
public void Author_plan_uses_earliest_source_position_when_target_is_absent()
{
    TaxonomyMergePreparation preparation = Preparation(
        TaxonomyKind.Author,
        authors: [Unrelated, SourceB, SourceA],
        ordinals: [0, 3, 7]);

    TaxonomyMergePlan plan = TaxonomyMergePlanner.Build(preparation, null);

    Assert.Equal([Unrelated, Target], plan.Owners.Single().After.AuthorIds);
}
```

Add equivalent tag tests, series replacement, multiple sources on one owner, inbox/formal owners, source-name reuse, third-term final-name collision, and affected-owner de-duplication.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMaintenanceContractsTests|FullyQualifiedName~TaxonomyMergePlannerTests|FullyQualifiedName~TaxonomyMergePreviewBuilderTests"
```

Expected: FAIL because maintenance types and planner do not exist.

- [ ] **Step 3: Add the final immutable contract shapes**

```csharp
public sealed record TaxonomyTermSnapshot(
    Guid Id, TaxonomyKind Kind, string DisplayName, string NormalizedName,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record TaxonomyNameOwnerSnapshot(string NormalizedName, Guid TermId);

public sealed record TaxonomyAssignmentSnapshot(
    IReadOnlyList<Guid> AuthorIds, Guid? SeriesId, IReadOnlyList<Guid> TagIds);

public sealed record TaxonomyOwnerSnapshot(
    MetadataOwner Owner, string VisibleName, long Version,
    TaxonomyAssignmentSnapshot Assignment);

public sealed record TaxonomyMergePreparation(
    Guid OperationId,
    TaxonomyMergeSelection Selection,
    TaxonomyTermSnapshot Target,
    IReadOnlyList<TaxonomyTermSnapshot> Sources,
    IReadOnlyList<TaxonomyNameOwnerSnapshot> SameKindNames,
    IReadOnlyList<TaxonomyOwnerSnapshot> Owners);

public sealed record TaxonomyMergePlanOwner(
    MetadataOwner Owner, string VisibleName, long BeforeVersion,
    TaxonomyAssignmentSnapshot Before, TaxonomyAssignmentSnapshot After);

public sealed record TaxonomyMergePlan(
    Guid OperationId, TaxonomyKind Kind, TaxonomyTermSnapshot Target,
    TaxonomyName FinalName, IReadOnlyList<TaxonomyTermSnapshot> Sources,
    IReadOnlyList<TaxonomyMergePlanOwner> Owners);

public sealed record TaxonomyMergeSelection(
    TaxonomyKind Kind, IReadOnlyList<Guid> SourceIds, Guid TargetId)
{
    public static TaxonomyMergeSelection Create(
        TaxonomyKind kind, IEnumerable<Guid> sourceIds, Guid targetId)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (!Enum.IsDefined(kind) || targetId == Guid.Empty)
            throw new ArgumentException("The merge target is invalid.");
        Guid[] copied = sourceIds.ToArray();
        if (copied.Length == 0 || copied.Any(id => id == Guid.Empty)
            || copied.Distinct().Count() != copied.Length
            || copied.Contains(targetId))
            throw new ArgumentException("The merge sources are invalid.");
        return new(kind, Array.AsReadOnly(copied), targetId);
    }
}

public sealed record TaxonomyDeleteSelection(
    TaxonomyKind Kind, IReadOnlyList<Guid> TermIds)
{
    public static TaxonomyDeleteSelection Create(
        TaxonomyKind kind, IEnumerable<Guid> termIds)
    {
        ArgumentNullException.ThrowIfNull(termIds);
        Guid[] copied = termIds.ToArray();
        if (!Enum.IsDefined(kind) || copied.Length == 0
            || copied.Any(id => id == Guid.Empty)
            || copied.Distinct().Count() != copied.Length)
            throw new ArgumentException("The delete selection is invalid.");
        return new(kind, Array.AsReadOnly(copied));
    }
}

public sealed record TaxonomyMergePreviewRowDto(
    MetadataOwner Owner,
    string VisibleName,
    TaxonomyAssignmentSnapshot Before,
    TaxonomyAssignmentSnapshot After);

public sealed record TaxonomyDeleteBlocker(
    TaxonomyTermSnapshot Term,
    int InboxReferenceCount,
    int LibraryReferenceCount);

public sealed record TaxonomyDeletePreparation(
    Guid OperationId,
    TaxonomyDeleteSelection Selection,
    IReadOnlyList<TaxonomyTermSnapshot> Terms,
    IReadOnlyList<TaxonomyDeleteBlocker> Blockers)
{
    public bool CanDelete => Terms.Count > 0 && Blockers.Count == 0;
}
```

Define preparation/result wrappers instead of using null as failure. Define `TaxonomyDeletePreparation` with exact term snapshots and all `Blockers`; computed `CanDelete` is true only when every selected term has zero references.

Define one base receipt and two concrete receipts:

```csharp
public abstract record TaxonomyMaintenanceUndoReceipt(Guid OperationId);

public sealed record TaxonomyMergeUndoReceipt(
    Guid OperationId,
    TaxonomyTermSnapshot TargetBefore,
    TaxonomyName TargetAfter,
    IReadOnlyList<TaxonomyTermSnapshot> Sources,
    IReadOnlyList<TaxonomyMergeUndoOwner> Owners)
    : TaxonomyMaintenanceUndoReceipt(OperationId);

public sealed record TaxonomyDeleteUndoReceipt(
    Guid OperationId,
    IReadOnlyList<TaxonomyTermSnapshot> Terms)
    : TaxonomyMaintenanceUndoReceipt(OperationId);
```

`TaxonomyMergeUndoOwner` contains `MetadataOwner`, `ExpectedPostVersion`, `Before`, and `After`. `TaxonomyMaintenanceStoreResult` contains success, changed owners, deleted term count, receipt, and error code. `TaxonomyMaintenanceUndoResult` contains success, restored owner/term counts, changed owners, `ReceiptIsRetryable`, and error code.

```csharp
public sealed record TaxonomyMergePrepareResult(
    bool Succeeded, TaxonomyMergePreparation? Preparation, string? ErrorCode);

public sealed record TaxonomyDeletePrepareResult(
    bool Succeeded, TaxonomyDeletePreparation? Preparation, string? ErrorCode);

public sealed record TaxonomyMaintenanceStoreResult(
    bool Succeeded,
    IReadOnlyList<MetadataOwner> ChangedOwners,
    int DeletedTermCount,
    TaxonomyMaintenanceUndoReceipt? UndoReceipt,
    string? ErrorCode);

public sealed record TaxonomyMaintenanceUndoResult(
    bool Succeeded,
    int RestoredOwnerCount,
    int RestoredTermCount,
    IReadOnlyList<MetadataOwner> ChangedOwners,
    bool ReceiptIsRetryable,
    string? ErrorCode);
```

Use these exact public result names across later tasks:

```csharp
public sealed record TaxonomyMergePreviewDto(
    IReadOnlyList<TaxonomyMergePreviewRowDto> Rows,
    int InboxAffectedCount,
    int LibraryAffectedCount,
    bool CanApply,
    string? ErrorCode);

public sealed record TaxonomyMaintenanceResult(
    bool Succeeded,
    int ChangedInboxCount,
    int ChangedLibraryCount,
    int DeletedTermCount,
    bool CanUndo,
    string? ErrorCode);

public sealed record TaxonomyMaintenanceUndoState(
    bool IsAvailable,
    int AffectedOwnerCount,
    int AffectedTermCount);

public static class TaxonomyMaintenanceErrorCodes
{
    public const string SelectionInvalid = "taxonomy.maintenance.selection_invalid";
    public const string NotFound = "taxonomy.maintenance.not_found";
    public const string KindMismatch = "taxonomy.maintenance.kind_mismatch";
    public const string TargetInvalid = "taxonomy.maintenance.target_invalid";
    public const string NameInvalid = "taxonomy.maintenance.name_invalid";
    public const string NameConflict = "taxonomy.maintenance.name_conflict";
    public const string PreviewStale = "taxonomy.maintenance.preview_stale";
    public const string DeleteInUse = "taxonomy.maintenance.delete.in_use";
    public const string PersistenceFailed = "taxonomy.maintenance.persistence_failed";
    public const string UndoUnavailable = "taxonomy.maintenance.undo.unavailable";
    public const string UndoConflict = "taxonomy.maintenance.undo.conflict";
}
```

Define the final store port:

```csharp
public interface ITaxonomyMaintenanceStore
{
    Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
        TaxonomyMergeSelection selection, CancellationToken cancellationToken);
    Task<TaxonomyMaintenanceStoreResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation, TaxonomyName? finalName,
        CancellationToken cancellationToken);
    Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
        TaxonomyDeleteSelection selection, CancellationToken cancellationToken);
    Task<TaxonomyMaintenanceStoreResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation, CancellationToken cancellationToken);
    Task<TaxonomyMaintenanceUndoResult> UndoAsync(
        TaxonomyMaintenanceUndoReceipt receipt, CancellationToken cancellationToken);
}
```

The service port additionally exposes `PreviewMerge(preparation, string? finalNameOverride)`, `UndoState`, `UndoStateChanged`, `UndoLastAsync`, and `InvalidateUndo()`.

```csharp
public interface ITaxonomyMaintenanceService
{
    TaxonomyMaintenanceUndoState UndoState { get; }
    event EventHandler? UndoStateChanged;
    Task<TaxonomyMergePrepareResult> PrepareMergeAsync(
        TaxonomyMergeSelection selection, CancellationToken cancellationToken);
    TaxonomyMergePreviewDto PreviewMerge(
        TaxonomyMergePreparation preparation, string? finalNameOverride);
    Task<TaxonomyMaintenanceResult> ApplyMergeAsync(
        TaxonomyMergePreparation preparation, string? finalNameOverride,
        CancellationToken cancellationToken);
    Task<TaxonomyDeletePrepareResult> PrepareDeleteAsync(
        TaxonomyDeleteSelection selection, CancellationToken cancellationToken);
    Task<TaxonomyMaintenanceResult> DeleteUnusedAsync(
        TaxonomyDeletePreparation preparation, CancellationToken cancellationToken);
    Task<TaxonomyMaintenanceUndoResult> UndoLastAsync(
        CancellationToken cancellationToken);
    void InvalidateUndo();
}
```

- [ ] **Step 4: Implement the pure planner once**

Use one helper for ordered Author/Tag replacement:

```csharp
private static IReadOnlyList<Guid> MergeOrdered(
    IReadOnlyList<Guid> current,
    IReadOnlySet<Guid> sourceIds,
    Guid targetId)
{
    if (current.Contains(targetId))
        return current.Where(id => !sourceIds.Contains(id)).Distinct().ToArray();

    int firstSource = current.Select((id, index) => (id, index))
        .Where(item => sourceIds.Contains(item.id))
        .Select(item => item.index)
        .DefaultIfEmpty(-1)
        .Min();
    if (firstSource < 0)
        return current.ToArray();

    var result = new List<Guid>(current.Count);
    for (int index = 0; index < current.Count; index++)
    {
        Guid id = current[index];
        if (index == firstSource)
            result.Add(targetId);
        if (!sourceIds.Contains(id))
            result.Add(id);
    }
    return result.Distinct().ToArray();
}
```

The persisted ordinal is represented by list position in snapshots; Infrastructure loads links ordered by `Ordinal` and writes dense list indexes. For series, replace only when `SeriesId` belongs to the source set. Resolve null final name as the target current name. Reject a normalized collision whose owner is neither target nor source. Build preview rows only from the returned plan.

- [ ] **Step 5: Run focused application tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMaintenance|FullyQualifiedName~TaxonomyMerge"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMaintenanceContracts.cs apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMergePlanner.cs apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMergePreviewBuilder.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMaintenanceContractsTests.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMergePlannerTests.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMergePreviewBuilderTests.cs
git commit -m "feat(media-desktop): plan taxonomy merges"
```

---

## Phase B — SQLite Preparation and Atomic Maintenance

### Task 4: Prepare Immutable Merge and Delete Snapshots

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyMaintenancePreparationTests.cs`

**Interfaces:**
- Consumes: maintenance snapshot contracts from Task 3.
- Produces: `EfTaxonomyMaintenanceStore.PrepareMergeAsync(...)` and `.PrepareDeleteAsync(...)` backed by shared context-local loaders.
- Does not register the store yet; registration waits until all mutation methods are implemented in Task 7.

- [ ] **Step 1: Write failing preparation tests**

```csharp
[Fact]
public async Task Prepare_merge_loads_distinct_inbox_and_library_owners_in_ordinal_order()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    TaxonomyMaintenanceFixture fixture =
        await database.SeedTaxonomyMaintenanceFixtureAsync();
    var store = new EfTaxonomyMaintenanceStore(
        database.CreateContextFactory(), TimeProvider.System);

    TaxonomyMergePrepareResult result = await store.PrepareMergeAsync(
        TaxonomyMergeSelection.Create(
            TaxonomyKind.Author,
            [fixture.SourceAuthorA, fixture.SourceAuthorB],
            fixture.TargetAuthor),
        TestContext.Current.CancellationToken);

    Assert.True(result.Succeeded);
    Assert.Equal(2, result.Preparation!.Sources.Count);
    Assert.Contains(result.Preparation.Owners,
        owner => owner.Owner.Kind == MetadataOwnerKind.InboxItem);
    Assert.Contains(result.Preparation.Owners,
        owner => owner.Owner.Kind == MetadataOwnerKind.MediaAsset);
    Assert.Equal(result.Preparation.Owners.Select(owner => owner.Owner).Distinct().Count(),
        result.Preparation.Owners.Count);
}
```

Add tests for target/source missing, cross-kind rows, target in sources, multiple source links on one owner counted once, all same-kind normalized-name owners loaded, and delete preparation returning every referenced blocker.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyMaintenancePreparationTests
```

Expected: FAIL because the maintenance store and fixture do not exist.

- [ ] **Step 3: Add deterministic maintenance fixtures**

Extend `TestMediaDatabase` with target, two source, unrelated, and unused Author/Tag terms; target/source/unrelated Series terms; one inbox draft and one formal asset with multiple source links and sparse ordinals; a second owner already containing the target; independent versions and visible names. Add read helpers for exact term rows, ordered assignments, and owner versions. Use parameterized SQL only.

- [ ] **Step 4: Implement context-local snapshot loaders**

`PrepareMergeAsync` validates before opening the context; loads target and all sources in one query; rejects missing/cross-kind terms with stable codes; loads same-kind ID/name ownership; finds distinct inbox/formal owners referencing any source; loads every affected owner's complete ordered assignment/version; uses display-name with actual-filename fallback for `VisibleName`; and returns owners ordered by kind, visible name, then ID.

`PrepareDeleteAsync` loads exact selected snapshots and all six reference sources. Return every term with non-zero total as a blocker. Do not write in either preparation method.

- [ ] **Step 5: Run focused preparation and query tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMaintenancePreparationTests|FullyQualifiedName~TaxonomyManagementQueryPersistenceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyMaintenancePreparationTests.cs
git commit -m "feat(media-desktop): prepare taxonomy maintenance snapshots"
```

### Task 5: Apply Same-Kind Merge Atomically

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyMergePersistenceTests.cs`

**Interfaces:**
- Consumes: `TaxonomyMergePlanner.Build(...)` and immutable preparation from Tasks 3–4.
- Produces: `ApplyMergeAsync(...)` and a `TaxonomyMergeUndoReceipt` containing exact pre-state plus expected post-state.

- [ ] **Step 1: Write failing atomic-merge tests**

```csharp
[Fact]
public async Task Merge_moves_both_owner_types_deduplicates_and_versions_once()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    TaxonomyMaintenanceFixture fixture =
        await database.SeedTaxonomyMaintenanceFixtureAsync();
    var store = new EfTaxonomyMaintenanceStore(
        database.CreateContextFactory(), TimeProvider.System);
    TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
        fixture.AuthorMergeSelection, TestContext.Current.CancellationToken);

    TaxonomyMaintenanceStoreResult result = await store.ApplyMergeAsync(
        prepared.Preparation!, TaxonomyName.Create("合并后作者"),
        TestContext.Current.CancellationToken);

    Assert.True(result.Succeeded);
    Assert.IsType<TaxonomyMergeUndoReceipt>(result.UndoReceipt);
    Assert.Equal(2, result.DeletedTermCount);
    Assert.Equal(2, result.ChangedOwners.Count);
    Assert.Equal(fixture.InboxVersion + 1,
        await database.ReadInboxVersionAsync(fixture.InboxId));
    Assert.Equal(fixture.AssetVersion + 1,
        await database.ReadAssetVersionAsync(fixture.AssetId));
}
```

Add target-present/absent, tag order, series, target-only rename, source-name reuse, third-term collision, stale term, late source reference, changed ordinal/version, missing owner, and injected later-write failure tests. Every failure asserts all terms, links, and versions are unchanged.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyMergePersistenceTests
```

Expected: FAIL because atomic apply is absent.

- [ ] **Step 3: Re-read and compare inside one transaction**

Open one context and one transaction. Call a private context-local preparation loader; do not call the public method. Compare target/source identity and name evidence, affected owner identity, complete ordered assignments, and versions. Ignore unrelated new terms unless one owns the requested final name. Map state drift to `taxonomy.maintenance.preview_stale` and a third-term name owner to `taxonomy.maintenance.name_conflict`.

- [ ] **Step 4: Apply the planner result without a second algorithm**

Call `TaxonomyMergePlanner.Build(currentPreparation, finalName)`. For each changed owner, replace only the relevant kind's links/FK, write dense list indexes, increment its version once, and update timestamp from injected `TimeProvider`. Remove source rows after migrating links; update target name after source removal. Capture receipt `Before`, `After`, and expected post versions before mutation. Commit once and return distinct changed owners.

Catch known unique/foreign-key `DbUpdateException` cases after rollback and return stable codes without exception text or private values.

- [ ] **Step 5: Run merge and metadata persistence tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMergePersistenceTests|FullyQualifiedName~MetadataPersistenceTests|FullyQualifiedName~LibraryBatchEditPersistenceTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyMergePersistenceTests.cs
git commit -m "feat(media-desktop): merge taxonomy references atomically"
```

### Task 6: Delete Unused Terms Atomically

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyDeletePersistenceTests.cs`

**Interfaces:**
- Consumes: `TaxonomyDeletePreparation` from Task 4.
- Produces: `DeleteUnusedAsync(...)` with all-or-nothing revalidation and a `TaxonomyDeleteUndoReceipt`.

- [ ] **Step 1: Write failing delete tests**

```csharp
[Fact]
public async Task Delete_unused_removes_every_selected_term_and_returns_one_receipt()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    TaxonomyMaintenanceFixture fixture =
        await database.SeedTaxonomyMaintenanceFixtureAsync();
    var store = new EfTaxonomyMaintenanceStore(
        database.CreateContextFactory(), TimeProvider.System);
    TaxonomyDeletePrepareResult prepared = await store.PrepareDeleteAsync(
        TaxonomyDeleteSelection.Create(
            TaxonomyKind.Tag, fixture.UnusedTagIds),
        TestContext.Current.CancellationToken);

    TaxonomyMaintenanceStoreResult result = await store.DeleteUnusedAsync(
        prepared.Preparation!, TestContext.Current.CancellationToken);

    Assert.True(result.Succeeded);
    Assert.Equal(fixture.UnusedTagIds.Count, result.DeletedTermCount);
    Assert.IsType<TaxonomyDeleteUndoReceipt>(result.UndoReceipt);
}
```

Add cases where one selected term is referenced at preparation, gets a late inbox/formal reference, is renamed, is removed, or a later write fails. Every failure leaves every selected term present.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyDeletePersistenceTests
```

Expected: FAIL because unused deletion is absent.

- [ ] **Step 3: Implement exact all-or-nothing deletion**

Inside one transaction reload every selected term and compare ID/kind/name/update evidence, then recompute all six reference sources. A non-zero count returns `taxonomy.maintenance.delete.in_use`; missing/changed evidence returns `taxonomy.maintenance.preview_stale`. Capture all complete term rows in one receipt, remove all rows, and commit once. Never remove only the safe subset. `ChangedOwners` is empty.

- [ ] **Step 4: Run focused tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyDeletePersistenceTests|FullyQualifiedName~TaxonomyMaintenancePreparationTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyDeletePersistenceTests.cs
git commit -m "feat(media-desktop): delete unused taxonomy atomically"
```

### Task 7: Restore Merge and Delete Receipts Atomically

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyMaintenanceUndoPersistenceTests.cs`

**Interfaces:**
- Consumes: both receipt types from Tasks 5–6.
- Produces: `UndoAsync(...)`; makes and registers `EfTaxonomyMaintenanceStore` as `ITaxonomyMaintenanceStore` after every method works.

- [ ] **Step 1: Write failing merge/delete undo tests**

```csharp
[Fact]
public async Task Merge_undo_restores_term_ids_name_order_and_advances_versions()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    TaxonomyMaintenanceFixture fixture =
        await database.SeedTaxonomyMaintenanceFixtureAsync();
    var store = new EfTaxonomyMaintenanceStore(
        database.CreateContextFactory(), TimeProvider.System);
    IReadOnlyList<Guid> originalAuthorOrder =
        await database.ReadInboxAuthorIdsAsync(fixture.InboxId);
    TaxonomyMergePrepareResult prepared = await store.PrepareMergeAsync(
        fixture.AuthorMergeSelection, TestContext.Current.CancellationToken);
    TaxonomyMaintenanceStoreResult applied = await store.ApplyMergeAsync(
        prepared.Preparation!, finalName: null,
        TestContext.Current.CancellationToken);
    TaxonomyMergeUndoReceipt receipt = Assert.IsType<TaxonomyMergeUndoReceipt>(
        applied.UndoReceipt);

    TaxonomyMaintenanceUndoResult undone = await store.UndoAsync(
        receipt, TestContext.Current.CancellationToken);

    Assert.True(undone.Succeeded);
    Assert.Equal(originalAuthorOrder,
        await database.ReadInboxAuthorIdsAsync(fixture.InboxId));
    Assert.Equal(receipt.Owners.Single(owner => owner.Owner.Id == fixture.InboxId)
        .ExpectedPostVersion + 1,
        await database.ReadInboxVersionAsync(fixture.InboxId));
}
```

Add delete restoration, target renamed, source ID/name recreated, owner version/assignment changed, missing owner, transient save failure, and multi-owner all-or-nothing tests. Each test creates and migrates a fresh `TestMediaDatabase`, seeds `TaxonomyMaintenanceFixture`, prepares the operation, applies it, mutates only the fact named by the test, invokes `UndoAsync`, and asserts every unaffected row remains byte-for-byte equivalent through the test read helpers.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyMaintenanceUndoPersistenceTests
```

Expected: FAIL because undo is absent.

- [ ] **Step 3: Implement receipt-specific validation and restore**

For merge receipts, verify target ID/after-name, absent source IDs and unowned source names, every owner at expected post version, and every current assignment equal to `After`. Restore source rows with original stable facts, target old name, and `Before` assignments; increment owner versions from expected post values. For delete receipts, verify IDs absent and names unowned, then restore all rows.

Return `taxonomy.maintenance.undo.conflict` with `ReceiptIsRetryable=false` for semantic conflicts. Return `taxonomy.maintenance.persistence_failed` with `ReceiptIsRetryable=true` only after a transient failure fully rolls back.

- [ ] **Step 4: Register and run all persistence coverage**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMaintenance|FullyQualifiedName~TaxonomyMerge|FullyQualifiedName~TaxonomyDelete"
```

Expected: PASS. Register one concrete store singleton and map `ITaxonomyMaintenanceStore` to the same instance.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfTaxonomyMaintenanceStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/TaxonomyMaintenanceUndoPersistenceTests.cs
git commit -m "feat(media-desktop): undo taxonomy maintenance atomically"
```

---

## Phase C — Application Orchestration and Desktop Workflow

### Task 8: Orchestrate Maintenance, Session Undo, Notifications, and Private Logs

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMaintenanceService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchEditorService.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMaintenanceServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMaintenanceLoggingPrivacyTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibraryBatchEditorServiceTests.cs`

**Interfaces:**
- Consumes: completed maintenance store, planner, preview builder, `IInboxActivityNotifier`, and `ILibraryActivityNotifier`.
- Produces: singleton `TaxonomyMaintenanceService` / `ITaxonomyMaintenanceService`, one in-memory receipt, `UndoStateChanged`, and coalesced notifications.
- Extends: `LibraryActivityKind` with `TaxonomyChanged`; Stage 5B undo invalidates if affected assets intersect.

- [ ] **Step 1: Write failing orchestration tests**

```csharp
[Fact]
public async Task Successful_merge_replaces_receipt_and_publishes_both_owner_kinds()
{
    var store = new RecordingMaintenanceStore
    {
        MergeResult = SuccessfulMergeResult(
            new MetadataOwner(MetadataOwnerKind.InboxItem, InboxId),
            new MetadataOwner(MetadataOwnerKind.MediaAsset, AssetId))
    };
    var inbox = new RecordingInboxActivityNotifier();
    var library = new RecordingLibraryActivityNotifier();
    var service = Service(store, inbox, library);

    TaxonomyMaintenanceResult result = await service.ApplyMergeAsync(
        Preparation(), finalNameOverride: null,
        TestContext.Current.CancellationToken);

    Assert.True(result.Succeeded);
    Assert.True(service.UndoState.IsAvailable);
    Assert.Contains(InboxActivityKind.ItemChanged, inbox.Published);
    Assert.Contains(library.Published,
        item => item.Kind == LibraryActivityKind.TaxonomyChanged
            && item.AssetIds.SequenceEqual([AssetId]));
}
```

Add preview invalid-name conversion, old receipt retained after zero-write failure, receipt replaced after successful merge/delete, explicit invalidation, conflict undo consumed, transient undo restored for retry, and one notification per owner category.

- [ ] **Step 2: Write privacy tests before logging**

Inject term names, visible filenames, normalized values, and paths as sentinel strings. Capture logs and assert every sentinel is absent while operation ID, kind, counts, phase, duration, success, and stable error code remain observable.

- [ ] **Step 3: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMaintenanceServiceTests|FullyQualifiedName~TaxonomyMaintenanceLoggingPrivacyTests|FullyQualifiedName~LibraryBatchEditorServiceTests"
```

Expected: FAIL because the service and taxonomy activity kind do not exist.

- [ ] **Step 4: Implement one locked receipt slot**

Use the claim/restore pattern already proven by `LibraryBatchEditorService`:

```csharp
lock (_undoLock)
{
    receipt = _lastUndoReceipt;
    _lastUndoReceipt = null;
}
UndoStateChanged?.Invoke(this, EventArgs.Empty);
```

If store undo returns `ReceiptIsRetryable`, restore only when no newer receipt exists. Semantic conflicts consume it. Successful merge/delete replaces the old receipt only after committed success. `InvalidateUndo()` clears and notifies only when state changed.

`PreviewMerge` parses a non-null override with `TaxonomyName.Create`; invalid input returns `CanApply=false` and `taxonomy.maintenance.name_invalid` while preserving dialog text.

After committed merge/undo, publish one `InboxActivityKind.ItemChanged` if any inbox owner changed and one `LibraryActivityKind.TaxonomyChanged` with distinct formal asset IDs. Delete/restore of unused terms publishes no metadata-owner event. Update `LibraryBatchEditorService.OnLibraryActivityChanged` so `SingleMetadataChanged` and `TaxonomyChanged` both invalidate intersecting Stage 5B receipts.

- [ ] **Step 5: Register and run focused tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMaintenance|FullyQualifiedName~LibraryBatchEditorServiceTests"
```

Expected: PASS. Register one concrete maintenance service singleton and map its interface to the same instance.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Metadata/TaxonomyMaintenanceService.cs apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchContracts.cs apps/media-desktop/src/TraceFold.Media.Application/Library/LibraryBatchEditorService.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMaintenanceServiceTests.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/TaxonomyMaintenanceLoggingPrivacyTests.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Library/LibraryBatchEditorServiceTests.cs
git commit -m "feat(media-desktop): orchestrate taxonomy maintenance"
```

### Task 9: Build Query-Order Selection and Page State

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomySelectionViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ITaxonomyMaintenanceDialogService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyViewModel.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/TaxonomySelectionViewModelTests.cs`
- Replace: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/TaxonomyViewModelTests.cs`

**Interfaces:**
- Consumes: management query service, legacy `ITaxonomyService`, maintenance service, and dialog port.
- Produces: Extended Selection capture/reconcile, query controls, 0/1/many details, blockers, merge/delete/undo commands, and rename-conflict target state.
- Produces: `ITaxonomyMaintenanceDialogService.ShowMergeAsync(...)` and `.ConfirmDeleteAsync(...)` for Task 11.

```csharp
public interface ITaxonomyMaintenanceDialogService
{
    Task<TaxonomyMaintenanceResult?> ShowMergeAsync(
        IReadOnlyList<TaxonomyManagementTermDto> selectedTerms,
        Guid? preselectedTargetId,
        CancellationToken cancellationToken);
    Task<bool> ConfirmDeleteAsync(
        TaxonomyDeletePreparation preparation,
        CancellationToken cancellationToken);
}
```

`ShowMergeAsync` returns null only when the user cancels; otherwise it returns the exact apply result so the page can preserve selection on failure and refresh on success.

- [ ] **Step 1: Write failing selection tests**

```csharp
[Fact]
public void Reconcile_preserves_ids_in_new_query_order()
{
    var selection = new TaxonomySelectionViewModel();
    TaxonomyManagementTermDto first = Term("B");
    TaxonomyManagementTermDto second = Term("A");
    selection.ReplaceFromList([first, second], [first, second]);

    selection.Reconcile([second, first]);

    Assert.Equal([second.Id, first.Id], selection.SelectedIds);
}
```

Add duplicate-ID rejection, clear, single/multiple derived state, and selected blocker enumeration.

- [ ] **Step 2: Write failing page-state tests**

Cover all of these explicitly:

- labels are 作者/系列/内容标签;
- search/filter changes clear selection and report it;
- sort changes preserve selection;
- delete is enabled only when every selected term has zero total references;
- mixed selection lists every blocker;
- merge is enabled for one or more selected terms;
- successful merge/delete reloads and keeps the target when visible;
- failed dialog retains selection;
- undo event controls banner and command;
- rename conflict resolves the exact normalized term and exposes “改为合并” without auto-merging;
- successful page create/ordinary rename invalidates an existing maintenance receipt.

- [ ] **Step 3: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomySelectionViewModelTests|FullyQualifiedName~TaxonomyViewModelTests"
```

Expected: FAIL because the selection model and new page state do not exist.

- [ ] **Step 4: Implement selection and orchestration only**

Mirror `LibrarySelectionViewModel` for query-order storage, but capture management term identities. `TaxonomyViewModel` builds every query through `TaxonomyManagementQuery.Create`, uses one reload semaphore/cancellation generation, and exposes:

```csharp
public ObservableCollection<TaxonomyManagementTermDto> Terms { get; }
public TaxonomySelectionViewModel Selection { get; }
public TaxonomyReferenceFilter ReferenceFilter { get; set; }
public TaxonomySortField SortField { get; set; }
public TaxonomySortDirection SortDirection { get; set; }
public IReadOnlyList<TaxonomyManagementTermDto> DeleteBlockers { get; }
public TaxonomyManagementTermDto? RenameConflictTarget { get; }
public bool CanMerge { get; }
public bool CanDeleteSelection { get; }
public bool CanUndoMaintenance { get; }
```

Merge calls the dialog with selected snapshots and optional preselected conflict target. Delete calls `PrepareDeleteAsync`, refuses blockers, asks for confirmation, then calls `DeleteUnusedAsync`. Map only stable error codes; never show raw exception text.

- [ ] **Step 5: Run page and picker tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomySelectionViewModelTests|FullyQualifiedName~TaxonomyViewModelTests|FullyQualifiedName~TaxonomyPickerViewModelTests|FullyQualifiedName~BatchTaxonomyPickerViewModelTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomySelectionViewModel.cs apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ITaxonomyMaintenanceDialogService.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyViewModel.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/TaxonomySelectionViewModelTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/TaxonomyViewModelTests.cs
git commit -m "feat(media-desktop): model taxonomy workspace state"
```

### Task 10: Build the Merge Dialog ViewModel

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialogViewModel.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/TaxonomyMergeDialogViewModelTests.cs`

**Interfaces:**
- Consumes: selected management terms, optional preselected target ID, management query service, and maintenance service.
- Produces: same-kind target suggestions, derived source set, rename toggle/input, preparation, preview rows, apply state, stable status, and retained input after failure.

- [ ] **Step 1: Write failing dialog-state tests**

```csharp
[Fact]
public async Task Selected_target_is_removed_from_sources_and_previewed()
{
    TaxonomyManagementTermDto source = Term("作者甲");
    TaxonomyManagementTermDto target = Term("作者乙");
    var viewModel = Dialog([source, target]);

    await viewModel.SelectTargetAsync(target, TestContext.Current.CancellationToken);

    Assert.Equal(target.Id, viewModel.Target!.Id);
    Assert.Equal([source.Id], viewModel.Sources.Select(term => term.Id));
    Assert.True(viewModel.Preview.CanApply);
}
```

Add external target, same-kind search, zero-source rejection, null rename retaining target, whitespace override blocking apply, third-term collision, target-change cancellation, failed apply input retention, and success tests.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyMergeDialogViewModelTests
```

Expected: FAIL because the dialog ViewModel does not exist.

- [ ] **Step 3: Implement generation-safe target and preview updates**

On target change increment a generation, derive sources by excluding target, reject zero sources, call `PrepareMergeAsync`, ignore stale completions, then call `PreviewMerge(preparation, RenameTarget ? FinalNameText : null)`. Search through management query with the selected kind, `ReferenceFilter.All`, and name ascending; never create terms here.

`ApplyAsync` disables repeat submit and calls the service with the exact preparation behind the current preview. A failure keeps target, sources, rename toggle, text, and preview rows intact.

- [ ] **Step 4: Run dialog and planner tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~TaxonomyMergeDialogViewModelTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyMergePlannerTests|FullyQualifiedName~TaxonomyMergePreviewBuilderTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialogViewModel.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/TaxonomyMergeDialogViewModelTests.cs
git commit -m "feat(media-desktop): model taxonomy merge dialog"
```

### Task 11: Render the Three-Column Workspace and Native Dialogs

**Files:**
- Replace: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialog.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialog.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/WindowsTaxonomyMaintenanceDialogService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs`

**Interfaces:**
- Consumes: Task 9 page state, Task 10 dialog ViewModel, `WindowContext`, and dialog port.
- Produces: approved Explorer-style three columns, large native merge dialog, unused-delete confirmation, focus behavior, and stable automation surface.

- [ ] **Step 1: Add failing XAML structure and composition tests**

Assert `TaxonomyPage.xaml` contains `SelectionMode="Extended"`, Control+A/Escape accelerators, and these exact IDs:

```text
TaxonomyKindList
TaxonomySearchBox
TaxonomyReferenceFilter
TaxonomySortField
TaxonomySortDirection
TaxonomyCreateButton
TaxonomyRefreshButton
TaxonomyTermList
TaxonomyInspectorPane
TaxonomyMergeButton
TaxonomyDeleteButton
TaxonomyDeleteBlockers
TaxonomyUndoBanner
TaxonomyUndoButton
TaxonomyRenameConflictMergeButton
```

Assert `TaxonomyMergeDialog.xaml` contains:

```text
TaxonomyMergeDialog
TaxonomyMergeSources
TaxonomyMergeTargetSearch
TaxonomyMergeTargetResults
TaxonomyMergeRenameToggle
TaxonomyMergeFinalName
TaxonomyMergeAffectedSummary
TaxonomyMergeAffectedFiles
TaxonomyMergeStatus
TaxonomyMergeApplyButton
TaxonomyMergeCancelButton
```

Composition tests resolve one dialog service, `TaxonomyViewModel`, and `TaxonomyPage`.

- [ ] **Step 2: Run tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
```

Expected: FAIL because the workspace/dialog controls and registration are absent.

- [ ] **Step 3: Build the three-column page**

Use compact type navigation, flexible term list, and contextual inspector. Keep the left taxonomy column near 176 px. The center toolbar contains search, reference filter, sort, direction, refresh, and new. The list shows name plus total references.

The right switches among instructional empty state; single detail with inbox/formal/total counts and actions; and multi summary with aggregate counts, all blockers, merge, and all-or-nothing delete. Synchronize `SelectedItems` exactly like `LibraryPage`, suppressing events during refresh. Search/filter clear selection; sorting reconciles. Return focus to the list after dialogs and do not steal active search focus.

- [ ] **Step 4: Build the large merge dialog and delete confirmation**

The header states `仅修改软件资料，不修改实际文件`. Render source list, target AutoSuggestBox, rename toggle/input, 待入库/正式库 counts, and affected before/result rows. Apply follows preview state; failure keeps the dialog open and focuses the failing input or first row.

The Windows adapter resolves main-window `XamlRoot`, constructs the merge ViewModel, and returns its result. `ConfirmDeleteAsync` lists every selected unused term and returns false when `preparation.CanDelete` is false.

- [ ] **Step 5: Register and run Desktop tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Taxonomy|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
```

Expected: PASS. Every action/input has a Chinese accessible name and stable ID.

- [ ] **Step 6: Build the native app to catch XAML compiler errors**

```powershell
dotnet build apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj -c Release --no-restore --disable-build-servers
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Pages/TaxonomyPage.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialog.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/TaxonomyMergeDialog.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/WindowsTaxonomyMaintenanceDialogService.cs apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs
git commit -m "feat(media-desktop): add taxonomy maintenance workspace"
```

---

## Phase D — Package Evidence and Release Gate

### Task 12: Add Stage 5C Package Probe and Human Guide

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeOptions.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeEnvironment.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeRunner.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5CTaxonomyProbeResult.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5CTaxonomyProbeScenario.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/PackageProbeTestSupport.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5CPackageProbeTests.cs`
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Create: `docs/media-desktop-stage5c-taxonomy-test-guide.md`

**Interfaces:**
- Produces: exact scenario string `stage5c-taxonomy`, root prefix `tracefold-media-desktop-stage5c-probe-`, and result file `stage5c-probe-result.json`.
- Produces: `StageCheckpoint 5C` for publish/smoke scripts and guarded package proof.

- [ ] **Step 1: Write failing probe option and scenario tests**

```csharp
[Fact]
public async Task Stage5C_probe_proves_query_merge_delete_undo_and_file_invariance()
{
    using PackageProbeRoot root = PackageProbeRoot.CreateStage5C();
    PackageProbeRequest request = PackageProbeRequests.Stage5CTaxonomy(root);

    int exitCode = await PackageProbeRunner.RunAsync(
        request, TestContext.Current.CancellationToken);
    Stage5CTaxonomyProbeResult result = root.ReadStage5CResult();

    Assert.Equal(0, exitCode);
    Assert.True(result.Succeeded);
    Assert.True(result.QueryPassed);
    Assert.True(result.MergePassed);
    Assert.True(result.MixedDeleteBlocked);
    Assert.True(result.DeleteUndoPassed);
    Assert.True(result.MergeUndoPassed);
    Assert.True(result.VersioningPassed);
    Assert.True(result.PhysicalFilesUnchanged);
    Assert.True(result.InternalStorageClean);
    Assert.Null(result.ErrorCode);
}
```

Add invalid scenario/root/token tests. Assert result JSON contains only booleans, counts, operation IDs, and error code—not names, paths, terms, or hashes.

- [ ] **Step 2: Run probe tests and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage5CPackageProbeTests|FullyQualifiedName~PackageProbeRefactorTests"
```

Expected: FAIL because Stage 5C is not mapped.

- [ ] **Step 3: Implement a guarded data-only Stage 5C scenario**

Use the existing owned temporary root. Seed test-owned inbox and formal records through production services and fixtures. Record physical file length/last-write/hash inside the scenario only for comparison; never write those values to JSON or logs. Execute:

1. referenced and unused management queries;
2. multi-source Author merge affecting one inbox draft and one formal asset;
3. each owner version advancing once;
4. mixed used/unused delete preparation and rejection;
5. successful unused delete and undo;
6. merge undo and exact relationship-order verification;
7. physical-file invariance and internal-storage cleanliness.

Write the result only after owned-tree validation. Add the scenario to contracts, runner, root/result switches, and test support.

- [ ] **Step 4: Extend publish and smoke checkpoints**

Add exact checkpoint `5C` to both scripts. Smoke maps it to `stage5c-taxonomy` and `stage5c-probe-result.json`, validates every boolean above and empty error code, and retains native-window, single-instance, no-browser, offline, FFmpeg, invalid-root, clean-exit, reparse-point, and ownership checks.

Publish artifact names are:

```text
dist/TraceFold-MediaDesktop-Stage5C-win-x64
dist/TraceFold-MediaDesktop-Stage5C-win-x64.zip
```

Never silently fall back to Stage 5B.

- [ ] **Step 5: Write the exact human acceptance guide**

Create `docs/media-desktop-stage5c-taxonomy-test-guide.md` with setup, action, expected result, and failure evidence for all 19 manual cases in design section 18. Include before/after file-hash commands, process cleanup checks, the local-only clean-machine limitation, and a pass/fail table.

- [ ] **Step 6: Run probe, script syntax, and regression tests**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage5CPackageProbeTests|FullyQualifiedName~Stage5BPackageProbeTests|FullyQualifiedName~PackageProbeRefactorTests"
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path scripts/publish-media-desktop.ps1), [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
[System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path scripts/smoke-media-desktop-package.ps1), [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
```

Expected: focused tests PASS and both scripts parse with zero errors.

- [ ] **Step 7: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1 docs/media-desktop-stage5c-taxonomy-test-guide.md
git commit -m "test(media-desktop): add stage 5c taxonomy checkpoint"
```

### Task 13: Full Verification, Review, Package, and Handoff

**Files:**
- Verify: all Stage 5C-A source, tests, scripts, and docs from Tasks 1–12.
- Create later, only after user-reported manual results: `docs/media-desktop-stage5c-taxonomy-acceptance.md`.

**Interfaces:**
- Consumes: completed implementation and local bundled dependency cache.
- Produces: review-clean branch, full automated evidence, offline Stage 5C test package, ZIP hash, and honest manual/clean-machine status.

- [ ] **Step 1: Inspect branch scope before verification**

```powershell
git status --short
git log --oneline --decorate dd8e8f2..HEAD
git diff --stat dd8e8f2..HEAD
git diff --name-only dd8e8f2..HEAD
```

Expected: only Stage 5C-A source/tests/scripts/docs are tracked; no `dist`, database, log, probe root, media binary, or `.superpowers` artifact is present.

- [ ] **Step 2: Run focused privacy, persistence, and Desktop gates**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --disable-build-servers --filter "FullyQualifiedName~TaxonomyMaintenance|FullyQualifiedName~TaxonomyMerge|FullyQualifiedName~TaxonomyManagement"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --disable-build-servers --filter "FullyQualifiedName~TaxonomyManagement|FullyQualifiedName~TaxonomyMaintenance|FullyQualifiedName~TaxonomyMerge|FullyQualifiedName~TaxonomyDelete"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --disable-build-servers --filter "FullyQualifiedName~Taxonomy|FullyQualifiedName~Stage5CPackageProbeTests|FullyQualifiedName~MediaWorkspaceStructureTests"
```

Expected: every focused test passes.

- [ ] **Step 3: Run clean Release build and full suite**

```powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
```

Expected: 0 warnings, 0 errors, and at least the 713-test baseline plus all Stage 5C-A tests.

- [ ] **Step 4: Request code review and close every valid finding**

Use `superpowers:requesting-code-review` against `dd8e8f2..HEAD`. Verify every finding against the approved spec and current code before editing. Add a failing regression test for each accepted behavioral fix, rerun the narrow suite, and make one focused review-fix commit. Repeat review until no actionable finding remains.

- [ ] **Step 5: Publish and smoke the offline package from committed HEAD**

Rebuild a publish-compatible source layout from the already verified Stage 5A package in the primary checkout; the publish script re-verifies both executable sizes and SHA-256 values against the pinned manifest. Do not download. Run:

```powershell
$commonGit = (& git rev-parse --git-common-dir).Trim()
if (-not [IO.Path]::IsPathRooted($commonGit)) {
    $commonGit = [IO.Path]::GetFullPath((Join-Path (Get-Location) $commonGit))
}
$primaryCheckout = Split-Path ([IO.Path]::GetFullPath($commonGit)) -Parent
$verifiedTools = Join-Path $primaryCheckout 'dist\TraceFold-MediaDesktop-Stage5A-win-x64\tools\ffmpeg'
foreach ($name in @('ffmpeg.exe','ffprobe.exe','LICENSE','README.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $verifiedTools $name) -PathType Leaf)) {
        throw "Verified local tool file is missing: $name"
    }
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryName = "tracefold-stage5c-ffmpeg-$([Guid]::NewGuid().ToString('N'))"
$temporaryTools = [IO.Path]::GetFullPath((Join-Path $temporaryBase $temporaryName))
New-Item -ItemType Directory -Path (Join-Path $temporaryTools 'bin') | Out-Null
Copy-Item -LiteralPath (Join-Path $verifiedTools 'ffmpeg.exe') -Destination (Join-Path $temporaryTools 'bin\ffmpeg.exe')
Copy-Item -LiteralPath (Join-Path $verifiedTools 'ffprobe.exe') -Destination (Join-Path $temporaryTools 'bin\ffprobe.exe')
Copy-Item -LiteralPath (Join-Path $verifiedTools 'LICENSE') -Destination (Join-Path $temporaryTools 'LICENSE')
Copy-Item -LiteralPath (Join-Path $verifiedTools 'README.txt') -Destination (Join-Path $temporaryTools 'README.txt')
try {
    powershell -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 5C -FfmpegSourceRoot $temporaryTools
    powershell -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist/TraceFold-MediaDesktop-Stage5C-win-x64' -StageCheckpoint 5C
    Get-FileHash 'dist/TraceFold-MediaDesktop-Stage5C-win-x64.zip' -Algorithm SHA256
}
finally {
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryTools)
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
            (Split-Path $resolvedTemporary -Parent).TrimEnd('\'),
            $temporaryBase.TrimEnd('\')) -or
        -not [StringComparer]::Ordinal.Equals(
            (Split-Path $resolvedTemporary -Leaf), $temporaryName)) {
        throw 'Refusing unsafe temporary FFmpeg cleanup.'
    }
    $temporaryItem = Get-Item -LiteralPath $resolvedTemporary -Force -ErrorAction SilentlyContinue
    if ($null -ne $temporaryItem -and
        ($temporaryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
```

Expected: native window, single instance, offline/no-browser start, Stage 5C query/merge/delete/undo/version/file-invariance probe, internal-storage cleanup, invalid-root failure, and clean exit all report PASS. Record ZIP path, size, SHA-256, product version, and source commit.

- [ ] **Step 6: Hand off manual acceptance without fabricating results**

Give the user the package path and test guide. Do not create the acceptance record until the user reports results. When results arrive, record each pass/fail, automated evidence, ZIP hash, source commit, and the still-pending clean Windows 11 machine gate; commit only that acceptance record.

- [ ] **Step 7: Finish the development branch**

After automated review is clean and the requested acceptance gate is satisfied, use `superpowers:finishing-a-development-branch` to present integration options. Do not merge, remove the worktree, or delete the branch without the user's selected option.
