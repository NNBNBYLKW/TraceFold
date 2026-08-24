# Native Media Desktop Stage 5A Batch Intake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Windows Explorer-style multi-selection, atomic batch metadata editing, generic batch preprocessing queueing, and confirmed sequential batch ingest to the native inbox without creating a second media state machine.

**Architecture:** Desktop owns ephemeral selection, dialogs, focus, and progress presentation; Application owns immutable selection snapshots, batch validation, classification, orchestration, and result mapping; Infrastructure owns SQLite reads and the one-transaction batch metadata write. Batch processing and ingest call the existing single-item services in list order, while package diagnostics are split into guarded common infrastructure and stage-specific scenarios.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, Serilog, xUnit v3, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev, PowerShell packaging scripts.

**Spec:** docs/superpowers/specs/2026-08-24-native-media-desktop-stage5a-batch-intake-design.md

## Global Constraints

- Execute in an isolated `codex/native-media-desktop-stage5a-batch-intake` worktree created with `superpowers:using-git-worktrees`; branch from the approved specification commit `c959969`.
- Target Windows 11 x64; keep `TraceFold.Media.Desktop` unpackaged, self-contained, single-instance, native-windowed, and fully offline.
- Do not run `git pull`, browse, download dependencies, or update pinned packages. Restore only from `apps/media-desktop/.packages` with failed remote sources ignored.
- Preserve the confirmed path `refresh -> optional preprocessing -> metadata -> formal ingest`; no automatic preprocessing or unattended ingest.
- Keep one item state machine, one generic processor registry, one serial `ProcessingQueueWorker`, one conflict pipeline, and one ingest/recovery pipeline.
- Do not add a `Batch` domain aggregate, batch/history tables, a task center, a recovery center, batch conflict decisions, batch deletion, or batch physical-file renaming.
- Batch selection entries contain only inbox item ID, expected metadata version, and list ordinal. Desktop never passes paths, hashes, or conflict target paths.
- `Ctrl+A` selects all current search/status query results, including virtualized items; changing search or status filter clears selection; a scan refresh intersects selection by item ID.
- Zero or one selected item keeps the existing inspector behavior. Two or more items show the batch bar and batch summary, not metadata input fields.
- Display names remain single-item only. Batch metadata supports author/tag Add, Remove, Replace and series Replace/Clear only.
- Every Replace field starts `Unchanged`; `Clear` is explicit and empty input never implies clearing.
- Batch metadata is all-or-nothing in one SQLite transaction. Any missing item, stale metadata version, disallowed action, invalid taxonomy value, constraint failure, or write failure leaves every selected item unchanged.
- New taxonomy names entered in the batch dialog stay deferred until the batch transaction; the existing picker that calls `ITaxonomyService.CreateAsync` immediately must not be reused for this path.
- No-op metadata updates do not increment metadata versions. Changed author/tag order is stable and duplicate IDs/names are removed while preserving first occurrence.
- Batch processing only invokes `IProcessingCommandService.StartAsync` in selection order; media tools still run serially through the existing worker.
- Batch ingest Preview is informative, not authorization. Execute revalidates through existing single-item services, continues after item-level results, never opens conflict dialogs, and stops only before the next item when requested.
- One Desktop window runs at most one batch orchestration command at a time. Application/state/version checks remain authoritative even while buttons are disabled.
- Logs may contain batch correlation ID, item IDs, counts, ordinal, phase, duration, and stable error codes only. Do not log paths, filenames, display names, taxonomy values, hashes, dialog input, or probe owner tokens.
- All filesystem and package-probe tests use test-owned roots under the system temporary directory with an ownership token. Never read, move, rename, or delete user-configured media directories.
- Stage 4's 473 tests are the minimum baseline. Every task follows red-green-refactor, ends with focused verification, and makes one reviewable commit.
- Do not commit `dist`, generated databases, logs, temporary workspaces, probe output, FFmpeg binaries, or ignored `.superpowers` session artifacts.

## Before Task 1

- [ ] Create and enter the isolated worktree with `superpowers:using-git-worktrees`, then confirm its identity:

~~~powershell
git status --short --branch
git rev-parse HEAD
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
~~~

Expected: the tree is clean, `HEAD` is `c959969`, and the worktree Git directory differs from the common Git directory.

- [ ] Restore from local caches and prove the Stage 4 baseline:

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: Release build reports 0 warnings and 0 errors; all 473 baseline tests pass. If the count differs, record the actual count and inspect the difference before editing. If restore, build, or any baseline test fails, stop and report the exact failure.

## File and Responsibility Map

- `Domain/Inbox/InboxActionPolicy.cs`: the only mapping from persisted item facts to `InboxAction`; no EF or Desktop presentation.
- `Application/Intake/InboxSelectionContracts.cs`: immutable ID/version/ordinal selection and current selection snapshots.
- `Application/Intake/InboxBatchSummaryService.cs`: aggregate current facts for the batch bar and right summary without interpreting state in Desktop.
- `Application/Metadata/BatchMetadataContracts.cs`: mode, explicit field operations, deferred taxonomy references, editor coverage, write plan, and result ports.
- `Application/Metadata/BatchMetadataMutation.cs`: pure author/series/tag Add/Remove/Replace/Clear transformation over resolved IDs.
- `Application/Metadata/BatchMetadataService.cs`: request validation, deferred `TaxonomyName` parsing, write-plan construction, and one post-commit notification.
- `Infrastructure/Persistence/EfInboxSelectionStore.cs`: chunked current-fact reads for selected inbox IDs.
- `Infrastructure/Persistence/EfBatchMetadataStore.cs`: prepare coverage and atomically resolve/create taxonomy plus apply all draft changes.
- `Application/Processing/BatchProcessingContracts.cs` and `BatchProcessingService.cs`: sequentially invoke the existing generic start command and map per-item outcomes.
- `Application/Ingest/BatchIngestContracts.cs` and `BatchIngestService.cs`: current-fact preview, sequential single-item execution, stop-before-next semantics, progress, and final categories.
- `Desktop/Pages/InboxSelectionViewModel.cs`: current query-order selection, immutable entry capture, filter clear, and refresh intersection.
- `Desktop/Pages/InboxBatchSummaryViewModel.cs`: batch counts, command visibility, progress, stop command, and final result presentation.
- `Desktop/Dialogs/BatchMetadata*`: deferred picker state, action-tab editor, native dialog, and result conversion only.
- `Desktop/Dialogs/BatchIngestConfirmation*`: one native summary confirmation; execution stays in `InboxViewModel`.
- `Desktop/Pages/InboxViewModel.cs` and `InboxPage.*`: single-vs-batch switching, command gates, selection synchronization, focus, and notification refresh coalescing.
- `Desktop/Diagnostics/PackageProbe/*`: guarded common options/environment/result infrastructure and separate Stage 4 / Stage 5A scenario modules.
- `scripts/publish-media-desktop.ps1` and `scripts/smoke-media-desktop-package.ps1`: Stage 5A package checkpoint and package-level proof.
- `docs/media-desktop-stage5a-test-guide.md`: user-run offline acceptance checklist.
- `docs/media-desktop-stage5a-acceptance.md`: final automated/package/manual evidence; create only after manual validation is reported.

---

## Phase A - Selection Facts and Atomic Metadata

### Task 1: Centralize Inbox Actions and Expose Immutable Selection Facts

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxActionPolicy.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItem.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxQueryContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxSelectionContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxCardViewModel.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Inbox/InboxItemTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/EfInboxQueryStoreRecoveryTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaCardViewModelTests.cs`

**Interfaces:**
- Produces: `InboxAction InboxActionPolicy.For(InboxItemState state, bool isRetryable, string? requiredCapability)`.
- Produces: `InboxSelectionEntry(Guid InboxItemId, long ExpectedMetadataVersion, int Ordinal)`.
- Produces: `InboxSelectionItemDto(Guid InboxItemId, long MetadataVersion, int Ordinal, MediaKind MediaKind, InboxItemState State, InboxAction AllowedActions, string SafeName)`.
- Produces: `IInboxSelectionStore.LoadAsync(IReadOnlyList<InboxSelectionEntry>, CancellationToken)`.
- Extends: `InboxCardDto` with optional trailing `MetadataVersion` and `AllowedActions` values.
- Produces: `InboxCardViewModel.SelectionEntry(int ordinal)`.

- [ ] **Step 1: Write failing centralized-policy and projection tests**

~~~csharp
[Fact]
public void Policy_keeps_conflict_resolution_and_metadata_actions_together()
{
    InboxAction actions = InboxActionPolicy.For(
        InboxItemState.ConflictBlocked,
        isRetryable: false,
        requiredCapability: null);

    Assert.True(actions.HasFlag(InboxAction.EditMetadata));
    Assert.True(actions.HasFlag(InboxAction.ResolveConflict));
    Assert.False(actions.HasFlag(InboxAction.Ingest));
}

[Fact]
public async Task Conflict_card_projects_the_same_actions_and_metadata_version_as_details()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid itemId = await database.SeedInboxItemAsync(
        InboxItemState.ConflictBlocked,
        metadataVersion: 7);
    var store = new EfInboxQueryStore(database.CreateContextFactory());

    InboxCardDto card = Assert.Single(await store.SearchAsync(
        new InboxQuery(null, null), CancellationToken.None));
    InboxDetailsDto details = Assert.NotNull(await store.GetDetailsAsync(
        itemId, CancellationToken.None));

    Assert.Equal(7, card.MetadataVersion);
    Assert.Equal(details.AllowedActions, card.AllowedActions);
    Assert.True(card.AllowedActions.HasFlag(InboxAction.ResolveConflict));
}
~~~

Add this reusable helper to `TestMediaDatabase`; it inserts only a test-owned inbox row and metadata draft into the database created by that helper:

~~~csharp
public async Task<Guid> SeedInboxItemAsync(
    InboxItemState state,
    long metadataVersion,
    MediaKind mediaKind = MediaKind.Image,
    string? displayName = null)
{
    Guid id = Guid.NewGuid();
    DateTimeOffset now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
    string filename = mediaKind == MediaKind.Image
        ? $"{id:N}.jpg"
        : $"{id:N}.mp4";
    string? capability = state == InboxItemState.RequiresProcessing
        ? "merge-mp4-m4a"
        : null;
    await using var context = new MediaDesktopDbContext(_options);
    await context.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO inbox_items
            (id, state, media_kind, actual_filename, grouping_rule,
             required_capability, is_retryable, created_at_utc, updated_at_utc)
        VALUES
            ({id}, {state.ToString().ToLowerInvariant()},
             {mediaKind.ToString().ToLowerInvariant()}, {filename}, {"direct"},
             {capability}, {false}, {now}, {now})
        """);
    await context.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO metadata_drafts
            (inbox_item_id, display_name, series_id, version, updated_at_utc)
        VALUES ({id}, {displayName}, {(Guid?)null}, {metadataVersion}, {now})
        """);
    return id;
}
~~~

- [ ] **Step 2: Run the focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxItemTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EfInboxQueryStoreRecoveryTests"
~~~

Expected: FAIL because `InboxActionPolicy`, `MetadataVersion`, and card-level `AllowedActions` do not exist; the conflict projection also exposes the existing duplicated-policy defect.

- [ ] **Step 3: Add the single action policy and immutable contracts**

~~~csharp
public static class InboxActionPolicy
{
    public static InboxAction For(
        InboxItemState state,
        bool isRetryable,
        string? requiredCapability) => state switch
    {
        InboxItemState.WaitingForStability => InboxAction.Preview,
        InboxItemState.ReadyToTag => InboxAction.Preview | InboxAction.EditMetadata
            | InboxAction.Ingest | InboxAction.OpenFolder,
        InboxItemState.RequiresProcessing => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.StartProcessing,
        InboxItemState.ProcessingQueued => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.CancelProcessing,
        InboxItemState.Processing => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.StopProcessing,
        InboxItemState.ProcessingInterrupted => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.RetryProcessing,
        InboxItemState.ConflictBlocked => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.ResolveConflict,
        InboxItemState.RecoveryBlocked => InboxAction.Preview
            | InboxAction.OpenFolder | InboxAction.RecheckRecovery,
        InboxItemState.CleanupPending => InboxAction.Preview
            | InboxAction.RetryCleanup | InboxAction.OpenFolder,
        InboxItemState.Failed when isRetryable
            && !string.IsNullOrWhiteSpace(requiredCapability) => InboxAction.Preview
            | InboxAction.EditMetadata | InboxAction.OpenFolder
            | InboxAction.RetryProcessing,
        InboxItemState.Failed when isRetryable => InboxAction.Preview
            | InboxAction.Retry | InboxAction.OpenFolder,
        InboxItemState.SourceMissing => InboxAction.ClearMissing,
        _ => InboxAction.None
    };
}

public sealed record InboxSelectionEntry(
    Guid InboxItemId,
    long ExpectedMetadataVersion,
    int Ordinal);

public sealed record InboxSelectionItemDto(
    Guid InboxItemId,
    long MetadataVersion,
    int Ordinal,
    MediaKind MediaKind,
    InboxItemState State,
    InboxAction AllowedActions,
    string SafeName);

public interface IInboxSelectionStore
{
    Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
}
~~~

Replace the switch in `InboxItem.AllowedActions` with `InboxActionPolicy.For(State, IsRetryable, RequiredCapability)`. Delete the private `EfInboxQueryStore.AllowedActions` switch and use the same policy for both cards and details.

- [ ] **Step 4: Extend card projection without breaking existing constructors**

~~~csharp
public sealed record InboxCardDto(
    Guid Id,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    InboxItemState State,
    string StatusText,
    bool CanIngest,
    ProcessingSummaryDto? Processing = null,
    long MetadataVersion = 0,
    InboxAction AllowedActions = InboxAction.None);
~~~

Map `row.MetadataDraft?.Version ?? 0` and `InboxActionPolicy.For(...)` in `EfInboxQueryStore.ToCard`. Add these exact Desktop members:

~~~csharp
public long MetadataVersion => Card.MetadataVersion;
public InboxAction AllowedActions => Card.AllowedActions;

public InboxSelectionEntry SelectionEntry(int ordinal) =>
    new(Id, MetadataVersion, ordinal);
~~~

- [ ] **Step 5: Run focused and neighboring tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-restore
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EfInboxQueryStore"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaCardViewModelTests"
~~~

Expected: PASS; the card and details action flags are identical for every covered state.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Domain/Inbox apps/media-desktop/src/TraceFold.Media.Application/Intake apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxCardViewModel.cs apps/media-desktop/tests
git commit -m "refactor(media-desktop): centralize inbox selection facts"
~~~

### Task 2: Define Explicit Batch Metadata Semantics and Pure Mutation

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/BatchMetadataContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/BatchMetadataMutation.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/BatchMetadataMutationTests.cs`

**Interfaces:**
- Consumes: `InboxSelectionEntry` from Task 1 and existing `MetadataSelection`, `TaxonomyKind`, `TaxonomyName`, and `TaxonomyTermDto`.
- Produces: `BatchMetadataMode`, `BatchMetadataFieldOperation`, `BatchTaxonomyReference`, `BatchMetadataSetEdit`, `BatchMetadataSeriesEdit`, and `BatchMetadataCommand`.
- Produces: `ResolvedBatchMetadataEdits`, `ResolvedBatchSetEdit`, `ResolvedBatchSeriesEdit`.
- Produces: `MetadataSelection BatchMetadataMutation.Apply(MetadataSelection current, ResolvedBatchMetadataEdits edits)`.
- Produces: `BatchTermCoverageDto`, `BatchMetadataEditorDto`, `BatchMetadataWrite`, `BatchMetadataResult`, `IBatchMetadataStore`, and `IBatchMetadataService`.

- [ ] **Step 1: Write failing operation-matrix and mutation tests**

~~~csharp
[Fact]
public void Add_preserves_existing_order_and_appends_only_new_ids()
{
    Guid a = Guid.NewGuid();
    Guid b = Guid.NewGuid();
    MetadataSelection current = MetadataSelection.Create(null, [a], null, []);
    var edits = new ResolvedBatchMetadataEdits(
        new(BatchMetadataFieldOperation.Add, [a, b]),
        ResolvedBatchSeriesEdit.Unchanged,
        ResolvedBatchSetEdit.Unchanged);

    MetadataSelection changed = BatchMetadataMutation.Apply(current, edits);

    Assert.Equal([a, b], changed.AuthorIds);
}

[Fact]
public void Remove_absent_ids_is_a_no_op_and_does_not_reorder()
{
    Guid a = Guid.NewGuid();
    Guid b = Guid.NewGuid();
    MetadataSelection current = MetadataSelection.Create(null, [a, b], null, []);
    var edits = new ResolvedBatchMetadataEdits(
        new(BatchMetadataFieldOperation.Remove, [Guid.NewGuid()]),
        ResolvedBatchSeriesEdit.Unchanged,
        ResolvedBatchSetEdit.Unchanged);

    Assert.Same(current, BatchMetadataMutation.Apply(current, edits));
}

[Fact]
public void Replace_can_explicitly_clear_series_without_touching_other_fields()
{
    Guid author = Guid.NewGuid();
    MetadataSelection current = MetadataSelection.Create(
        null, [author], Guid.NewGuid(), [Guid.NewGuid()]);
    var edits = new ResolvedBatchMetadataEdits(
        ResolvedBatchSetEdit.Unchanged,
        new(BatchMetadataFieldOperation.Clear, null),
        ResolvedBatchSetEdit.Unchanged);

    MetadataSelection changed = BatchMetadataMutation.Apply(current, edits);

    Assert.Null(changed.SeriesId);
    Assert.Equal(current.AuthorIds, changed.AuthorIds);
    Assert.Equal(current.TagIds, changed.TagIds);
}

[Theory]
[InlineData(BatchMetadataMode.Add, BatchMetadataFieldOperation.Replace)]
[InlineData(BatchMetadataMode.Remove, BatchMetadataFieldOperation.Clear)]
public void Command_rejects_operations_outside_the_selected_action_tab(
    BatchMetadataMode mode,
    BatchMetadataFieldOperation operation)
{
    Assert.Throws<ArgumentException>(() => BatchMetadataCommand.Create(
        [new InboxSelectionEntry(Guid.NewGuid(), 0, 0)],
        mode,
        new BatchMetadataSetEdit(operation, []),
        BatchMetadataSeriesEdit.Unchanged,
        BatchMetadataSetEdit.Unchanged));
}
~~~

- [ ] **Step 2: Run the focused test and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataMutationTests"
~~~

Expected: FAIL because no batch metadata contract or mutation policy exists.

- [ ] **Step 3: Add the exact public command and editor records**

~~~csharp
public enum BatchMetadataMode { Add, Remove, Replace }

public enum BatchMetadataFieldOperation
{
    Unchanged,
    Add,
    Remove,
    Replace,
    Clear
}

public sealed record BatchTaxonomyReference(
    TaxonomyKind Kind,
    Guid? ExistingId,
    string? NewName)
{
    public static BatchTaxonomyReference Existing(TaxonomyKind kind, Guid id) =>
        id == Guid.Empty
            ? throw new ArgumentException("A persisted taxonomy id is required.")
            : new(kind, id, null);

    public static BatchTaxonomyReference New(TaxonomyKind kind, string name) =>
        new(kind, null, name);
}

public sealed record BatchMetadataSetEdit(
    BatchMetadataFieldOperation Operation,
    IReadOnlyList<BatchTaxonomyReference> Values)
{
    public static BatchMetadataSetEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, []);
}

public sealed record BatchMetadataSeriesEdit(
    BatchMetadataFieldOperation Operation,
    BatchTaxonomyReference? Value)
{
    public static BatchMetadataSeriesEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, null);
}

public sealed record BatchMetadataCommand(
    IReadOnlyList<InboxSelectionEntry> Selection,
    BatchMetadataMode Mode,
    BatchMetadataSetEdit Authors,
    BatchMetadataSeriesEdit Series,
    BatchMetadataSetEdit Tags)
{
    public static BatchMetadataCommand Create(
        IReadOnlyList<InboxSelectionEntry> selection,
        BatchMetadataMode mode,
        BatchMetadataSetEdit authors,
        BatchMetadataSeriesEdit series,
        BatchMetadataSetEdit tags);
}

public sealed record BatchTermCoverageDto(
    TaxonomyTermDto Term,
    int SelectedCount);

public sealed record BatchMetadataEditorDto(
    int SelectedCount,
    IReadOnlyList<BatchTermCoverageDto> Authors,
    IReadOnlyList<BatchTermCoverageDto> Tags);
~~~

`Create` must reject empty selection, duplicate item IDs/ordinals, negative versions/ordinals, incorrect taxonomy kinds, series operations outside Replace, Remove values that are not existing IDs, and any operation not allowed by the selected mode. It must allow `Unchanged`, `Replace`, or `Clear` independently on each field in Replace mode and require at least one changed field.

- [ ] **Step 4: Add resolved write records and the pure mutation**

~~~csharp
public sealed record ResolvedBatchSetEdit(
    BatchMetadataFieldOperation Operation,
    IReadOnlyList<Guid> Values)
{
    public static ResolvedBatchSetEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, []);
}

public sealed record ResolvedBatchSeriesEdit(
    BatchMetadataFieldOperation Operation,
    Guid? Value)
{
    public static ResolvedBatchSeriesEdit Unchanged { get; } =
        new(BatchMetadataFieldOperation.Unchanged, null);
}

public sealed record ResolvedBatchMetadataEdits(
    ResolvedBatchSetEdit Authors,
    ResolvedBatchSeriesEdit Series,
    ResolvedBatchSetEdit Tags);

public static class BatchMetadataMutation
{
    public static MetadataSelection Apply(
        MetadataSelection current,
        ResolvedBatchMetadataEdits edits)
    {
        IReadOnlyList<Guid> authors = ApplySet(current.AuthorIds, edits.Authors);
        Guid? series = edits.Series.Operation switch
        {
            BatchMetadataFieldOperation.Unchanged => current.SeriesId,
            BatchMetadataFieldOperation.Replace => edits.Series.Value,
            BatchMetadataFieldOperation.Clear => null,
            _ => throw new ArgumentException("Invalid series operation.")
        };
        IReadOnlyList<Guid> tags = ApplySet(current.TagIds, edits.Tags);
        if (authors.SequenceEqual(current.AuthorIds)
            && series == current.SeriesId
            && tags.SequenceEqual(current.TagIds))
        {
            return current;
        }

        return MetadataSelection.Create(current.DisplayName, authors, series, tags);
    }
}
~~~

Implement `ApplySet` as: Unchanged returns the original list; Add concatenates and first-occurrence deduplicates; Remove filters a `HashSet<Guid>`; Replace first-occurrence deduplicates the requested values; Clear returns `[]`. Never sort taxonomy IDs.

- [ ] **Step 5: Add service/store records with signatures used by later tasks**

~~~csharp
public sealed record BatchTaxonomyToken(
    TaxonomyKind Kind,
    Guid? ExistingId,
    TaxonomyName? NewName);

public sealed record BatchMetadataWrite(
    IReadOnlyList<InboxSelectionEntry> Selection,
    BatchMetadataMode Mode,
    BatchMetadataFieldOperation AuthorOperation,
    IReadOnlyList<BatchTaxonomyToken> Authors,
    BatchMetadataFieldOperation SeriesOperation,
    BatchTaxonomyToken? Series,
    BatchMetadataFieldOperation TagOperation,
    IReadOnlyList<BatchTaxonomyToken> Tags);

public sealed record BatchMetadataResult(
    bool Succeeded,
    int ChangedCount,
    int UnchangedCount,
    int AffectedCount,
    string? ErrorCode);

public interface IBatchMetadataStore
{
    Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataWrite write,
        CancellationToken cancellationToken);
}

public interface IBatchMetadataService
{
    Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataCommand command,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 6: Run mutation and full Application tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataMutationTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore
~~~

Expected: PASS with no existing metadata behavior changed.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Metadata apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata
git commit -m "feat(media-desktop): define batch metadata semantics"
~~~

### Task 3: Validate Batch Metadata Commands in Application

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/BatchMetadataService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/BatchMetadataServiceTests.cs`

**Interfaces:**
- Consumes: all Task 2 contracts and existing `IInboxActivityNotifier`.
- Produces: `BatchMetadataService : IBatchMetadataService`.
- Guarantees: no store call for malformed deferred names; exactly one `ItemChanged` notification after a successful changed transaction; no notification for failure or all-no-op success.

- [ ] **Step 1: Write failing service tests with a recording store**

~~~csharp
[Fact]
public async Task Apply_converts_new_names_but_does_not_persist_them_separately()
{
    var store = new RecordingBatchMetadataStore
    {
        ApplyResult = new BatchMetadataResult(true, 2, 0, 2, null)
    };
    var notifier = new RecordingInboxActivityNotifier();
    var service = new BatchMetadataService(store, notifier);
    BatchMetadataCommand command = AddAuthors(
        BatchTaxonomyReference.New(TaxonomyKind.Author, " 作者甲 "));

    BatchMetadataResult result = await service.ApplyAsync(
        command, CancellationToken.None);

    Assert.True(result.Succeeded);
    BatchTaxonomyToken token = Assert.Single(store.LastWrite!.Authors);
    Assert.Equal("作者甲", token.NewName!.Display);
    Assert.Equal("作者甲".Normalize(NormalizationForm.FormKC).ToUpperInvariant(),
        token.NewName.Normalized);
    Assert.Equal([InboxActivityKind.ItemChanged], notifier.Published);
}

[Fact]
public async Task Invalid_new_name_never_reaches_the_atomic_store()
{
    var store = new RecordingBatchMetadataStore();
    var service = new BatchMetadataService(store, new RecordingInboxActivityNotifier());

    BatchMetadataResult result = await service.ApplyAsync(
        AddAuthors(BatchTaxonomyReference.New(TaxonomyKind.Author, "   ")),
        CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Equal("batch.metadata.taxonomy_invalid", result.ErrorCode);
    Assert.Null(store.LastWrite);
}

[Fact]
public async Task No_op_success_does_not_publish_a_refresh_storm()
{
    var notifier = new RecordingInboxActivityNotifier();
    var store = new RecordingBatchMetadataStore
    {
        ApplyResult = new BatchMetadataResult(true, 0, 3, 0, null)
    };
    var service = new BatchMetadataService(store, notifier);

    await service.ApplyAsync(AddAuthors(ExistingAuthor()), CancellationToken.None);

    Assert.Empty(notifier.Published);
}
~~~

The test fake implements only the exact Task 2 store methods and captures `LastWrite`; it must not call `ITaxonomyService`.

- [ ] **Step 2: Run the focused test and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataServiceTests"
~~~

Expected: FAIL because `BatchMetadataService` does not exist.

- [ ] **Step 3: Implement validation and write-plan construction**

~~~csharp
public sealed class BatchMetadataService(
    IBatchMetadataStore store,
    IInboxActivityNotifier notifier) : IBatchMetadataService
{
    public Task<BatchMetadataEditorDto?> PrepareAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken) =>
        store.PrepareAsync(selection, cancellationToken);

    public async Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        BatchMetadataWrite write;
        try
        {
            write = new BatchMetadataWrite(
                command.Selection,
                command.Mode,
                command.Authors.Operation,
                Convert(command.Authors.Values),
                command.Series.Operation,
                command.Series.Value is null ? null : Convert(command.Series.Value),
                command.Tags.Operation,
                Convert(command.Tags.Values));
        }
        catch (ArgumentException)
        {
            return new BatchMetadataResult(
                false, 0, 0, command.Selection.Count,
                "batch.metadata.taxonomy_invalid");
        }

        BatchMetadataResult result = await store.ApplyAsync(write, cancellationToken);
        if (result.Succeeded && result.ChangedCount > 0)
        {
            notifier.Publish(InboxActivityKind.ItemChanged);
        }

        return result;
    }

    private static BatchTaxonomyToken Convert(BatchTaxonomyReference value) =>
        new(
            value.Kind,
            value.ExistingId,
            value.ExistingId.HasValue ? null : TaxonomyName.Create(value.NewName!));
}
~~~

For list conversion, preserve order and first-occurrence deduplicate by `(Kind, ExistingId)` or `(Kind, NewName.Normalized)`. Return `batch.metadata.selection_invalid` if an externally constructed command bypasses `Create` and carries duplicate/empty selection or inconsistent mode operations.

- [ ] **Step 4: Register only the application service**

~~~csharp
services.AddSingleton<IBatchMetadataService, BatchMetadataService>();
~~~

Do not register an infrastructure store in Application and do not change the existing single-item editor registration.

- [ ] **Step 5: Run service tests and the Application suite**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore
~~~

Expected: PASS; the fake store observes normalized deferred names and one coalesced notification.

- [ ] **Step 6: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/tests/TraceFold.Media.Application.Tests/Metadata/BatchMetadataServiceTests.cs
git commit -m "feat(media-desktop): validate batch metadata commands"
~~~

### Task 4: Apply Batch Metadata Atomically in SQLite

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfBatchMetadataStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/BatchMetadataPersistenceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`

**Interfaces:**
- Consumes: `IBatchMetadataStore`, `BatchMetadataWrite`, `BatchMetadataMutation`, `InboxActionPolicy`, and existing EF rows.
- Produces: `EfBatchMetadataStore : IBatchMetadataStore` with a private `const int ReadBatchSize = 200`.
- Guarantees: all selected IDs and versions are checked before any write; every chunk shares one transaction; taxonomy creation/reuse and metadata changes commit together; no migration is added.

- [ ] **Step 1: Write failing prepare, normalization, version, no-op, scale, and rollback tests**

~~~csharp
[Fact]
public async Task Apply_creates_or_reuses_taxonomy_and_commits_every_draft_once()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid[] ids = await database.SeedEditableInboxItemsAsync(3);
    var store = new EfBatchMetadataStore(database.CreateContextFactory());
    BatchMetadataWrite write = AddNewAuthor(ids, "作者甲");

    BatchMetadataResult result = await store.ApplyAsync(write, CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.Equal(3, result.ChangedCount);
    Assert.Equal(3, await database.CountDraftsWithAuthorAsync("作者甲"));
    Assert.Equal(1, await database.CountTaxonomyTermsAsync(
        TaxonomyKind.Author, "作者甲"));
    Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
        Assert.Equal(1, version));
}

[Fact]
public async Task One_stale_version_rolls_back_every_item_and_new_term()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid[] ids = await database.SeedEditableInboxItemsAsync(3);
    await database.SetDraftVersionAsync(ids[1], 4);
    var store = new EfBatchMetadataStore(database.CreateContextFactory());

    BatchMetadataResult result = await store.ApplyAsync(
        AddNewAuthor(ids, "不会落库", expectedVersion: 0),
        CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Equal("batch.metadata.version_conflict", result.ErrorCode);
    Assert.Equal([0L, 4L, 0L], await database.LoadMetadataVersionsAsync(ids));
    Assert.Equal(0, await database.CountTaxonomyTermsAsync(
        TaxonomyKind.Author, "不会落库"));
}

[Fact]
public async Task Trigger_failure_after_first_write_chunk_rolls_back_all_chunks()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid[] ids = await database.SeedEditableInboxItemsAsync(401);
    await database.CreateMetadataUpdateFailureTriggerAsync(ids[250]);
    var store = new EfBatchMetadataStore(database.CreateContextFactory());

    BatchMetadataResult result = await store.ApplyAsync(
        AddNewAuthor(ids, "批量作者"), CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Equal("batch.metadata.persistence_failed", result.ErrorCode);
    Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
        Assert.Equal(0, version));
    Assert.Equal(0, await database.CountTaxonomyTermsAsync(
        TaxonomyKind.Author, "批量作者"));
}

[Fact]
public async Task Six_hundred_items_use_chunked_reads_without_partial_commit()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid[] ids = await database.SeedEditableInboxItemsAsync(600);
    var store = new EfBatchMetadataStore(database.CreateContextFactory());

    BatchMetadataResult result = await store.ApplyAsync(
        AddExistingTag(ids, await database.SeedTaxonomyAsync(
            TaxonomyKind.Tag, "风景")), CancellationToken.None);

    Assert.Equal(600, result.ChangedCount);
    Assert.All(await database.LoadMetadataVersionsAsync(ids), version =>
        Assert.Equal(1, version));
}
~~~

Also assert `PrepareAsync` returns only author/tag terms present in the selection with exact coverage counts, returns `null` for a missing/stale/disallowed item, and a no-op Remove keeps versions unchanged.

Extend `TestMediaDatabase` with these exact helpers used by the test class:

~~~csharp
public Task<Guid[]> SeedEditableInboxItemsAsync(int count);
public Task<IReadOnlyList<long>> LoadMetadataVersionsAsync(
    IReadOnlyList<Guid> inboxItemIds);
public Task<Guid> SeedTaxonomyAsync(TaxonomyKind kind, string displayName);
public Task<int> CountTaxonomyTermsAsync(TaxonomyKind kind, string displayName);
public Task<int> CountDraftsWithAuthorAsync(string displayName);
public Task CreateMetadataUpdateFailureTriggerAsync(Guid inboxItemId);
~~~

`SeedEditableInboxItemsAsync` inserts Ready/image rows plus version-0 drafts in one test transaction. The trigger is named `test_fail_batch_metadata`, runs `BEFORE UPDATE ON metadata_drafts WHEN NEW.inbox_item_id = '<canonical id>'`, and executes `SELECT RAISE(ABORT, 'test batch failure')`; no production fault seam is added.

- [ ] **Step 2: Run the focused persistence tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataPersistenceTests"
~~~

Expected: FAIL because `EfBatchMetadataStore` and the test helpers do not exist.

- [ ] **Step 3: Implement one guarded transaction and validate all items first**

Use this skeleton; every `Chunk(ReadBatchSize)` query and write remains inside the same `IDbContextTransaction`:

~~~csharp
public sealed class EfBatchMetadataStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory) : IBatchMetadataStore
{
    private const int ReadBatchSize = 200;

    public async Task<BatchMetadataResult> ApplyAsync(
        BatchMetadataWrite write,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            IReadOnlyDictionary<Guid, InboxDraftSnapshot> snapshots =
                await LoadAllSnapshotsAsync(context, write.Selection, cancellationToken);
            BatchMetadataResult? rejection = ValidateEverySelection(write.Selection, snapshots);
            if (rejection is not null)
            {
                return rejection;
            }

            ResolvedBatchMetadataEdits edits = await ResolveEditsAsync(
                context, write, cancellationToken);
            int changed = 0;
            foreach (InboxSelectionEntry[] chunk in
                     write.Selection.OrderBy(item => item.Ordinal).Chunk(ReadBatchSize))
            {
                changed += ApplyChunk(context, chunk, snapshots, edits);
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new BatchMetadataResult(
                true, changed, write.Selection.Count - changed,
                write.Selection.Count, null);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new BatchMetadataResult(
                false, 0, 0, write.Selection.Count,
                "batch.metadata.persistence_failed");
        }
    }
}
~~~

Add the stable validation-failure carrier and catch it before `DbUpdateException`:

~~~csharp
private sealed class BatchMetadataStoreException(string errorCode) : Exception
{
    public string ErrorCode { get; } = errorCode;
}

catch (BatchMetadataStoreException error)
{
    await transaction.RollbackAsync(CancellationToken.None);
    return new BatchMetadataResult(
        false, 0, 0, write.Selection.Count, error.ErrorCode);
}
~~~

`LoadAllSnapshotsAsync` must load inbox state, retry/capability facts, draft version/display name/series, ordered author IDs, and ordered tag IDs. `ValidateEverySelection` rejects missing IDs, duplicate selection IDs, non-matching versions, and missing `InboxAction.EditMetadata` before `ResolveEditsAsync` is called.

Use this private immutable snapshot so validation and mutation cannot accidentally reread different facts:

~~~csharp
private sealed record InboxDraftSnapshot(
    Guid InboxItemId,
    InboxItemState State,
    bool IsRetryable,
    string? RequiredCapability,
    string? DisplayName,
    Guid? SeriesId,
    IReadOnlyList<Guid> AuthorIds,
    IReadOnlyList<Guid> TagIds,
    long MetadataVersion);
~~~

- [ ] **Step 4: Resolve deferred taxonomy inside the transaction**

For each `BatchTaxonomyToken`:

~~~csharp
private static async Task<Guid> ResolveTokenAsync(
    MediaDesktopDbContext context,
    BatchTaxonomyToken token,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    string kind = token.Kind.ToString().ToLowerInvariant();
    if (token.ExistingId is Guid id)
    {
        TaxonomyTermRow? existing = await context.TaxonomyTerms
            .SingleOrDefaultAsync(row => row.Id == id && row.Kind == kind,
                cancellationToken);
        return existing?.Id
            ?? throw new BatchMetadataStoreException(
                "batch.metadata.taxonomy_missing");
    }

    TaxonomyName name = token.NewName
        ?? throw new BatchMetadataStoreException(
            "batch.metadata.taxonomy_invalid");
    TaxonomyTermRow? matched = await context.TaxonomyTerms
        .SingleOrDefaultAsync(row => row.Kind == kind
            && row.NormalizedName == name.Normalized, cancellationToken);
    if (matched is not null)
    {
        return matched.Id;
    }

    var created = new TaxonomyTermRow
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        DisplayName = name.Display,
        NormalizedName = name.Normalized,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };
    context.TaxonomyTerms.Add(created);
    return created.Id;
}
~~~

Deduplicate normalized new names before resolution. Catch `BatchMetadataStoreException` and return its stable error code after rollback. Do not call `EfTaxonomyStore.CreateOrGetAsync`, because it creates a separate context and transaction.

- [ ] **Step 5: Apply only changed drafts and preserve order**

Build `MetadataSelection current` from each snapshot, call `BatchMetadataMutation.Apply`, and compare by reference. For changed drafts only: keep `DisplayName`, set `SeriesId`, increment `Version` once, update `UpdatedAtUtc`, replace author/tag link rows with `Ordinal = index`; for unchanged drafts, do not touch tracked entities. `PrepareAsync` uses the same `LoadAllSnapshotsAsync` validation and groups author/tag terms into `BatchTermCoverageDto` ordered by display name.

- [ ] **Step 6: Register the store and run persistence/regression tests**

~~~csharp
services.AddSingleton<IBatchMetadataStore, EfBatchMetadataStore>();
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataPersistenceTests|FullyQualifiedName~MetadataPersistenceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadata"
~~~

Expected: PASS, including the trigger-induced rollback and 600-item case; no EF migration is generated.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests
git commit -m "feat(media-desktop): persist batch metadata atomically"
~~~

---

## Phase B - Reuse Existing Processing and Ingest Pipelines

### Task 5: Orchestrate Generic Batch Processing Commands

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/BatchProcessingContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/BatchProcessingService.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/BatchProcessingServiceTests.cs`

**Interfaces:**
- Consumes: `IInboxSelectionStore.LoadAsync(...)`, `IProcessingCommandService.StartAsync(...)`, `InboxAction.StartProcessing`, and `InboxSelectionEntry`.
- Produces: `BatchProcessingItemOutcome`, `BatchProcessingItemResult`, `BatchProcessingResult`, and `IBatchProcessingService.StartAsync(...)`.
- Guarantees: selection-order calls, no media-tool parallelism, no duplicate queue records, and no direct processor/capability branching.

- [ ] **Step 1: Write failing ordering and classification tests**

~~~csharp
[Fact]
public async Task Start_calls_the_single_item_command_only_for_actionable_items_in_order()
{
    Guid image = Guid.NewGuid();
    Guid paired = Guid.NewGuid();
    Guid active = Guid.NewGuid();
    var facts = new FakeInboxSelectionStore(
        Ready(image, ordinal: 0),
        RequiresProcessing(paired, ordinal: 1),
        ProcessingQueued(active, ordinal: 2));
    var commands = new RecordingProcessingCommandService(
        new ProcessingCommandResult(
            ProcessingCommandOutcome.Queued, Guid.NewGuid(), "queued"));
    var service = new BatchProcessingService(facts, commands);

    BatchProcessingResult result = await service.StartAsync(
        Select(image, paired, active), CancellationToken.None);

    Assert.Equal([paired], commands.StartedIds);
    Assert.Equal(BatchProcessingItemOutcome.NotRequired, result.Items[0].Outcome);
    Assert.Equal(BatchProcessingItemOutcome.Queued, result.Items[1].Outcome);
    Assert.Equal(BatchProcessingItemOutcome.AlreadyActive, result.Items[2].Outcome);
}

[Theory]
[InlineData(ProcessingCommandOutcome.SourceMissing)]
[InlineData(ProcessingCommandOutcome.SourceChanged)]
[InlineData(ProcessingCommandOutcome.ToolUnavailable)]
[InlineData(ProcessingCommandOutcome.Failed)]
public async Task Unsafe_start_results_are_reported_as_needs_attention(
    ProcessingCommandOutcome outcome)
{
    Guid id = Guid.NewGuid();
    var service = ServiceFor(id, outcome);

    BatchProcessingResult result = await service.StartAsync(
        Select(id), CancellationToken.None);

    Assert.Equal(
        BatchProcessingItemOutcome.NeedsAttention,
        Assert.Single(result.Items).Outcome);
}
~~~

- [ ] **Step 2: Run the focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchProcessingServiceTests"
~~~

Expected: FAIL because the batch processing contracts and service do not exist.

- [ ] **Step 3: Add result contracts and the sequential service**

~~~csharp
public enum BatchProcessingItemOutcome
{
    Queued,
    AlreadyActive,
    NotRequired,
    NeedsAttention,
    NotStarted
}

public sealed record BatchProcessingItemResult(
    Guid InboxItemId,
    int Ordinal,
    BatchProcessingItemOutcome Outcome,
    string? ErrorCode);

public sealed record BatchProcessingResult(
    IReadOnlyList<BatchProcessingItemResult> Items)
{
    public int QueuedCount => Items.Count(item =>
        item.Outcome == BatchProcessingItemOutcome.Queued);
}

public interface IBatchProcessingService
{
    Task<BatchProcessingResult> StartAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
}
~~~

The service loads current facts once, indexes them by ID, and loops over `selection.OrderBy(item => item.Ordinal)`. Missing items become `NeedsAttention`; items with `StartProcessing` call `StartAsync`; queued/running actions become `AlreadyActive`; all other current items become `NotRequired`. Map command outcomes exactly:

~~~csharp
private static BatchProcessingItemOutcome Map(ProcessingCommandOutcome value) =>
    value switch
    {
        ProcessingCommandOutcome.Queued => BatchProcessingItemOutcome.Queued,
        ProcessingCommandOutcome.AlreadyActive =>
            BatchProcessingItemOutcome.AlreadyActive,
        ProcessingCommandOutcome.NotEligible =>
            BatchProcessingItemOutcome.NeedsAttention,
        ProcessingCommandOutcome.SourceMissing or
        ProcessingCommandOutcome.SourceChanged or
        ProcessingCommandOutcome.ToolUnavailable or
        ProcessingCommandOutcome.Failed =>
            BatchProcessingItemOutcome.NeedsAttention,
        ProcessingCommandOutcome.ShuttingDown =>
            BatchProcessingItemOutcome.NotStarted,
        _ => BatchProcessingItemOutcome.NeedsAttention
    };
~~~

If shutdown/cancellation prevents another call, append `NotStarted` for every remaining ordinal. Do not use `Task.WhenAll`, inspect `RequiredCapability`, invoke `IMediaTool`, or signal the queue directly.

- [ ] **Step 4: Run focused and existing processing tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchProcessingServiceTests|FullyQualifiedName~ProcessingCommandServiceTests|FullyQualifiedName~MediaProcessorRegistryTests"
~~~

Expected: PASS and `RecordingProcessingCommandService.StartedIds` proves serial selection order.

- [ ] **Step 5: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Processing apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/BatchProcessingServiceTests.cs
git commit -m "feat(media-desktop): orchestrate batch processing queueing"
~~~

### Task 6: Read Current Selection Facts and Build the Batch Summary

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxSelectionStore.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxBatchSummaryService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/EfInboxSelectionStoreTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/InboxBatchSummaryServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 `IInboxSelectionStore`, `InboxActionPolicy`, and current inbox/draft rows.
- Produces: `EfInboxSelectionStore`, `InboxBatchSummaryDto`, `IInboxBatchSummaryService`, and `InboxBatchSummaryService`.
- Registers: `IInboxSelectionStore`, `IInboxBatchSummaryService`, and Task 5 `IBatchProcessingService`.

- [ ] **Step 1: Write failing chunk/order and summary tests**

~~~csharp
[Fact]
public async Task Store_returns_current_facts_in_requested_ordinal_order()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    Guid ready = await database.SeedInboxItemAsync(
        InboxItemState.ReadyToTag, metadataVersion: 3,
        mediaKind: MediaKind.Image, displayName: "图像");
    Guid processing = await database.SeedInboxItemAsync(
        InboxItemState.RequiresProcessing, metadataVersion: 8,
        mediaKind: MediaKind.Video, displayName: "视频");
    var store = new EfInboxSelectionStore(database.CreateContextFactory());

    IReadOnlyList<InboxSelectionItemDto> result = await store.LoadAsync(
        [new(processing, 7, 0), new(ready, 3, 1)], CancellationToken.None);

    Assert.Equal([processing, ready], result.Select(item => item.InboxItemId));
    Assert.Equal([8L, 3L], result.Select(item => item.MetadataVersion));
    Assert.True(result[0].AllowedActions.HasFlag(InboxAction.StartProcessing));
}

[Fact]
public async Task Summary_uses_current_actions_and_exposes_stale_selection()
{
    var store = new FakeInboxSelectionStore(
        Item(MediaKind.Image, InboxItemState.ReadyToTag,
            InboxAction.EditMetadata | InboxAction.Ingest, currentVersion: 2,
            expectedVersion: 2, ordinal: 0),
        Item(MediaKind.Video, InboxItemState.RequiresProcessing,
            InboxAction.EditMetadata | InboxAction.StartProcessing,
            currentVersion: 5, expectedVersion: 4, ordinal: 1));
    var service = new InboxBatchSummaryService(store);

    InboxBatchSummaryDto summary = await service.SummarizeAsync(
        store.Selection, CancellationToken.None);

    Assert.Equal(2, summary.SelectedCount);
    Assert.Equal(1, summary.ImageCount);
    Assert.Equal(1, summary.VideoCount);
    Assert.Equal(1, summary.ReadyCount);
    Assert.Equal(1, summary.RequiresProcessingCount);
    Assert.Equal(1, summary.StaleCount);
    Assert.False(summary.CanEditMetadata);
    Assert.True(summary.CanStartProcessing);
    Assert.True(summary.CanPreviewIngest);
}
~~~

- [ ] **Step 2: Run both focused test classes and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EfInboxSelectionStoreTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxBatchSummaryServiceTests"
~~~

Expected: FAIL because neither implementation exists.

- [ ] **Step 3: Implement the chunked current-fact store**

~~~csharp
public sealed class EfInboxSelectionStore(
    IDbContextFactory<MediaDesktopDbContext> contextFactory)
    : IInboxSelectionStore
{
    private const int ReadBatchSize = 200;

    public async Task<IReadOnlyList<InboxSelectionItemDto>> LoadAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken)
    {
        await using MediaDesktopDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = new Dictionary<Guid, InboxItemRow>();
        foreach (Guid[] ids in selection.Select(item => item.InboxItemId)
                     .Distinct().Chunk(ReadBatchSize))
        {
            foreach (InboxItemRow row in await context.InboxItems.AsNoTracking()
                         .Include(item => item.MetadataDraft)
                         .Where(item => ids.Contains(item.Id)
                             && item.State != InboxItemState.Completed)
                         .ToArrayAsync(cancellationToken))
            {
                rows[row.Id] = row;
            }
        }

        return selection.OrderBy(item => item.Ordinal)
            .Where(item => rows.ContainsKey(item.InboxItemId))
            .Select(item => ToDto(rows[item.InboxItemId], item.Ordinal))
            .ToArray();
    }
}
~~~

`ToDto` uses current draft version, `MediaKind`, state, `InboxActionPolicy.For`, and `MetadataDraft.DisplayName ?? ActualFilename` as `SafeName`. This name may be displayed but must never be logged.

- [ ] **Step 4: Add the summary contract and service**

~~~csharp
public sealed record InboxBatchSummaryDto(
    int SelectedCount,
    int ImageCount,
    int VideoCount,
    int ReadyCount,
    int RequiresProcessingCount,
    int ConflictCount,
    int RecoveryBlockedCount,
    int OtherCount,
    int StaleCount,
    bool CanEditMetadata,
    bool CanStartProcessing,
    bool CanPreviewIngest);

public interface IInboxBatchSummaryService
{
    Task<InboxBatchSummaryDto> SummarizeAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
}
~~~

The service compares returned current versions against the entries by ID. `CanEditMetadata` requires at least two items, no missing/stale item, and `EditMetadata` on every item. `CanStartProcessing` requires at least one current `StartProcessing` action. `CanPreviewIngest` requires at least two current items. `RecoveryBlockedCount` includes only `RecoveryBlocked`; `CleanupPending`, missing, analyzing, ingesting, failed, and waiting states count as `OtherCount`.

- [ ] **Step 5: Register all now-resolvable services**

~~~csharp
// Application/DependencyInjection.cs
services.AddSingleton<IInboxBatchSummaryService, InboxBatchSummaryService>();
services.AddSingleton<IBatchProcessingService, BatchProcessingService>();

// Infrastructure/DependencyInjection.cs
services.AddSingleton<IInboxSelectionStore, EfInboxSelectionStore>();
~~~

- [ ] **Step 6: Run focused, composition, and query tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EfInboxSelectionStoreTests|FullyQualifiedName~EfInboxQueryStore"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxBatchSummaryServiceTests|FullyQualifiedName~BatchProcessingServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DesktopCompositionRootTests"
~~~

Expected: PASS; service resolution has no missing dependency.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests
git commit -m "feat(media-desktop): summarize current batch selection"
~~~

### Task 7: Preview and Execute Sequential Batch Ingest

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/BatchIngestContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/BatchIngestService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/BatchIngestServiceTests.cs`

**Interfaces:**
- Consumes: `IInboxSelectionStore`, `IIngestService`, selection entries, current actions, states, and existing `IngestOutcome`.
- Produces: `BatchIngestPreviewCategory`, `BatchIngestPreviewItemDto`, `BatchIngestPreviewDto`, `BatchIngestItemOutcome`, `BatchIngestItemResult`, `BatchIngestProgress`, `BatchIngestResult`, and `IBatchIngestService`.
- Guarantees: Preview does not mutate; Execute reloads each item before start; the stop token is checked only between items; item-level returned failures do not stop later items; thrown orchestration failures mark remaining items `NotStarted`.

- [ ] **Step 1: Write failing preview/revalidation/order/stop tests**

~~~csharp
[Fact]
public async Task Preview_reclassifies_current_facts_without_calling_ingest()
{
    Guid ready = Guid.NewGuid();
    Guid processing = Guid.NewGuid();
    Guid conflict = Guid.NewGuid();
    var ingest = new RecordingIngestService();
    var service = new BatchIngestService(
        new FakeInboxSelectionStore(
            Ready(ready, 0), RequiresProcessing(processing, 1),
            Conflict(conflict, 2)),
        ingest);

    BatchIngestPreviewDto preview = await service.PreviewAsync(
        Select(ready, processing, conflict), CancellationToken.None);

    Assert.Equal(1, preview.ReadyCount);
    Assert.Equal(1, preview.RequiresProcessingCount);
    Assert.Equal(1, preview.ConflictCount);
    Assert.Empty(ingest.Calls);
}

[Fact]
public async Task Execute_is_serial_continues_after_item_results_and_never_resolves_conflicts()
{
    Guid first = Guid.NewGuid();
    Guid second = Guid.NewGuid();
    Guid third = Guid.NewGuid();
    var ingest = new RecordingIngestService(
        (first, Result(IngestOutcome.Completed)),
        (second, Result(IngestOutcome.ConflictBlocked)),
        (third, Result(IngestOutcome.Completed)));
    var service = new BatchIngestService(
        CurrentReadyStore(first, second, third), ingest);

    BatchIngestResult result = await service.ExecuteAsync(
        Select(first, second, third), progress: null,
        stopAfterCurrent: CancellationToken.None,
        cancellationToken: CancellationToken.None);

    Assert.Equal([first, second, third], ingest.Calls);
    Assert.Equal(2, result.CompletedCount);
    Assert.Equal(1, result.ConflictCount);
}

[Fact]
public async Task Execute_rejects_a_stale_version_before_calling_single_item_ingest()
{
    Guid id = Guid.NewGuid();
    var ingest = new RecordingIngestService();
    var store = CurrentReadyStore(id, currentVersion: 4);
    var service = new BatchIngestService(store, ingest);

    BatchIngestResult result = await service.ExecuteAsync(
        [new InboxSelectionEntry(id, 3, 0)], null,
        CancellationToken.None, CancellationToken.None);

    Assert.Equal(BatchIngestItemOutcome.ChangedOrInvalid,
        Assert.Single(result.Items).Outcome);
    Assert.Empty(ingest.Calls);
}

[Fact]
public async Task Stop_requested_during_first_item_marks_later_items_not_started()
{
    Guid first = Guid.NewGuid();
    Guid second = Guid.NewGuid();
    using var stop = new CancellationTokenSource();
    var ingest = new RecordingIngestService(async id =>
    {
        stop.Cancel();
        await Task.Yield();
        return Result(IngestOutcome.Completed);
    });
    var service = new BatchIngestService(CurrentReadyStore(first, second), ingest);

    BatchIngestResult result = await service.ExecuteAsync(
        Select(first, second), null, stop.Token, CancellationToken.None);

    Assert.Equal([first], ingest.Calls);
    Assert.True(result.StoppedByUser);
    Assert.Equal(BatchIngestItemOutcome.NotStarted, result.Items[1].Outcome);
}
~~~

Add a fifth test where `IIngestService` returns `Failed` for item 1 and completes item 2, and a sixth where the fake throws for item 1 and item 2 becomes `NotStarted`.

- [ ] **Step 2: Run the focused test and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchIngestServiceTests"
~~~

Expected: FAIL because the batch ingest contracts and service do not exist.

- [ ] **Step 3: Add preview and execution contracts**

~~~csharp
public enum BatchIngestPreviewCategory
{
    Ready,
    RequiresProcessing,
    ConflictBlocked,
    RecoveryBlocked,
    ChangedOrInvalid
}

public sealed record BatchIngestPreviewItemDto(
    Guid InboxItemId,
    int Ordinal,
    string SafeName,
    BatchIngestPreviewCategory Category);

public sealed record BatchIngestPreviewDto(
    IReadOnlyList<BatchIngestPreviewItemDto> Items)
{
    public int ReadyCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.Ready);
    public int RequiresProcessingCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.RequiresProcessing);
    public int ConflictCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.ConflictBlocked);
    public int RecoveryBlockedCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.RecoveryBlocked);
    public int ChangedOrInvalidCount => Items.Count(item =>
        item.Category == BatchIngestPreviewCategory.ChangedOrInvalid);
}

public enum BatchIngestItemOutcome
{
    Completed,
    RequiresProcessing,
    ConflictBlocked,
    RecoveryBlocked,
    ChangedOrInvalid,
    Failed,
    NotStarted
}

public sealed record BatchIngestItemResult(
    Guid InboxItemId,
    int Ordinal,
    BatchIngestItemOutcome Outcome,
    string? ErrorCode);

public sealed record BatchIngestProgress(
    int CompletedItems,
    int TotalItems,
    Guid CurrentInboxItemId,
    string SafeName);

public sealed record BatchIngestResult(
    IReadOnlyList<BatchIngestItemResult> Items,
    bool StoppedByUser)
{
    public int CompletedCount => Items.Count(item =>
        item.Outcome == BatchIngestItemOutcome.Completed);
    public int ConflictCount => Items.Count(item =>
        item.Outcome == BatchIngestItemOutcome.ConflictBlocked);
}

public interface IBatchIngestService
{
    Task<BatchIngestPreviewDto> PreviewAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);

    Task<BatchIngestResult> ExecuteAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        IProgress<BatchIngestProgress>? progress,
        CancellationToken stopAfterCurrent,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 4: Implement Preview from current facts**

Load all facts, join by ID, compare current and expected metadata versions, and classify in this priority: missing/stale -> `ChangedOrInvalid`; current `Ingest` action -> `Ready`; `RequiresProcessing`, `ProcessingQueued`, `Processing`, or `ProcessingInterrupted` -> `RequiresProcessing`; `ConflictBlocked` -> `ConflictBlocked`; `RecoveryBlocked` or `CleanupPending` -> `RecoveryBlocked`; everything else -> `ChangedOrInvalid`.

- [ ] **Step 5: Implement Execute as a revalidating serial loop**

~~~csharp
foreach (InboxSelectionEntry entry in selection.OrderBy(item => item.Ordinal))
{
    if (stopAfterCurrent.IsCancellationRequested)
    {
        stopped = true;
        AppendNotStartedFrom(entry.Ordinal);
        break;
    }

    InboxSelectionItemDto? current = AssertSingleOrMissing(
        await selectionStore.LoadAsync([entry], cancellationToken));
    BatchIngestItemResult? rejected = Revalidate(entry, current);
    if (rejected is not null)
    {
        items.Add(rejected);
        continue;
    }

    progress?.Report(new BatchIngestProgress(
        items.Count, selection.Count, entry.InboxItemId, current!.SafeName));
    IngestResult single = await ingest.IngestAsync(
        entry.InboxItemId, cancellationToken);
    items.Add(Map(entry, single));
    progress?.Report(new BatchIngestProgress(
        items.Count, selection.Count, entry.InboxItemId, current.SafeName));
}
~~~

Map `Completed`; `RequiresProcessing`; `ConflictBlocked`; `CleanupPending`/`InProgress` to `RecoveryBlocked`; `SourceMissing`/`SourceChanged`/`NotReady`/`ConflictChanged`/`Skipped` to `ChangedOrInvalid`; `Failed` to `Failed`; `ShuttingDown` to `NotStarted` and stop the remainder. Catch an unexpected non-cancellation exception outside the current call, add `Failed` for the current ordinal with `batch.ingest.orchestration_failed`, append `NotStarted` for the remainder, and stop. Do not inject or call any conflict dialog/query/resolution service.

- [ ] **Step 6: Register and run batch plus single-item ingest tests**

~~~csharp
services.AddSingleton<IBatchIngestService, BatchIngestService>();
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchIngestServiceTests|FullyQualifiedName~IngestServiceTests|FullyQualifiedName~ConflictResolutionServiceTests"
~~~

Expected: PASS; the fake's maximum concurrent call count remains 1 and no conflict service appears in `BatchIngestService` dependencies.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Ingest apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/BatchIngestServiceTests.cs
git commit -m "feat(media-desktop): orchestrate sequential batch ingest"
~~~

---

## Phase C - Native Multi-Selection and Batch UI

### Task 8: Model Explorer-Style Selection Independently of WinUI Controls

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxSelectionViewModel.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxSelectionViewModelTests.cs`

**Interfaces:**
- Consumes: `InboxCardViewModel.SelectionEntry(int ordinal)`.
- Produces: `InboxSelectionViewModel.ReplaceFromGrid(...)`, `Capture()`, `Clear()`, `Reconcile(...)`, `Retain(...)`, `SelectedIds`, `SelectedCount`, `IsSingleSelection`, `IsBatchSelection`, and `SingleSelectedItem`.
- Guarantees: order always follows the current query result, not click order; filter change clears; refresh intersects by ID and resets the range anchor in WinUI.

- [ ] **Step 1: Write failing order, clear, reconcile, and retain tests**

~~~csharp
[Fact]
public void Replace_uses_query_order_even_when_grid_reports_click_order()
{
    InboxCardViewModel a = Card("a.jpg", version: 1);
    InboxCardViewModel b = Card("b.jpg", version: 2);
    InboxCardViewModel c = Card("c.jpg", version: 3);
    var selection = new InboxSelectionViewModel();

    selection.ReplaceFromGrid([c, a], [a, b, c]);

    Assert.Equal([a.Id, c.Id], selection.SelectedIds);
    Assert.Equal([0, 2], selection.Capture().Select(item => item.Ordinal));
}

[Fact]
public void Reconcile_intersects_ids_and_refreshes_versions_from_new_cards()
{
    InboxCardViewModel oldA = Card("a.jpg", version: 1);
    InboxCardViewModel oldB = Card("b.jpg", version: 2);
    var selection = new InboxSelectionViewModel();
    selection.ReplaceFromGrid([oldA, oldB], [oldA, oldB]);
    InboxCardViewModel newA = Card(oldA.Id, "a.jpg", version: 4);

    selection.Reconcile([newA]);

    InboxSelectionEntry remaining = Assert.Single(selection.Capture());
    Assert.Equal(oldA.Id, remaining.InboxItemId);
    Assert.Equal(4, remaining.ExpectedMetadataVersion);
}

[Fact]
public void Retain_keeps_only_unfinished_ids_in_query_order()
{
    InboxCardViewModel a = Card("a.jpg");
    InboxCardViewModel b = Card("b.jpg");
    InboxCardViewModel c = Card("c.jpg");
    var selection = Selected(a, b, c);

    selection.Retain([c.Id, b.Id]);

    Assert.Equal([b.Id, c.Id], selection.SelectedIds);
}
~~~

- [ ] **Step 2: Run the focused test and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxSelectionViewModelTests"
~~~

Expected: FAIL because the selection model does not exist.

- [ ] **Step 3: Implement one ordered selection model**

~~~csharp
public sealed partial class InboxSelectionViewModel : ObservableObject
{
    private readonly List<InboxCardViewModel> _selected = [];

    public IReadOnlyList<Guid> SelectedIds =>
        _selected.Select(item => item.Id).ToArray();
    public int SelectedCount => _selected.Count;
    public bool IsSingleSelection => _selected.Count == 1;
    public bool IsBatchSelection => _selected.Count >= 2;
    public InboxCardViewModel? SingleSelectedItem =>
        _selected.Count == 1 ? _selected[0] : null;

    public void ReplaceFromGrid(
        IEnumerable<InboxCardViewModel> selected,
        IReadOnlyList<InboxCardViewModel> queryOrder);
    public IReadOnlyList<InboxSelectionEntry> Capture();
    public void Clear();
    public void Reconcile(IReadOnlyList<InboxCardViewModel> queryOrder);
    public void Retain(IReadOnlyCollection<Guid> inboxItemIds);
}
~~~

Every mutation raises `PropertyChanged` for all five derived properties. `Capture` resolves ordinal by current query order retained during `ReplaceFromGrid`/`Reconcile`, rejects duplicate IDs, and returns a new immutable array.

- [ ] **Step 4: Run the focused and card tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxSelectionViewModelTests|FullyQualifiedName~MediaCardViewModelTests"
~~~

Expected: PASS with current-version capture after refresh.

- [ ] **Step 5: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxSelectionViewModel.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxSelectionViewModelTests.cs
git commit -m "feat(media-desktop): model ordered inbox multi-selection"
~~~

### Task 9: Switch the Inbox Between Single Inspector and Batch Summary

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxBatchSummaryViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxBatchSummaryViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`

**Interfaces:**
- Consumes: `InboxSelectionViewModel`, `IInboxBatchSummaryService`, `InboxBatchSummaryDto`, and the existing single inspector.
- Produces: `InboxBatchSummaryViewModel.LoadAsync(...)`, `Reset()`, summary labels/visibility, `InboxViewModel.Selection`, `InboxViewModel.BatchSummary`, and `SelectManyAsync(...)`.
- Produces: `InboxViewModel.GridItemsReplacing` / `GridItemsReplaced` events so code-behind can suppress transient selection events while cards are replaced and then reapply selected IDs.
- UI: `GridView.SelectionMode="Extended"`, native Ctrl/Shift semantics, explicit Ctrl+A/Esc accelerators, a selected-count/cancel batch bar, and a right batch summary.

- [ ] **Step 1: Write failing view-model switching tests**

~~~csharp
[Fact]
public async Task Two_selected_items_flush_single_metadata_and_load_batch_summary()
{
    InboxViewModel viewModel = CreateViewModel(out InboxTestContext context);
    await viewModel.LoadAsync(CancellationToken.None);
    InboxCardViewModel first = viewModel.Items[0];
    InboxCardViewModel second = viewModel.Items[1];
    await viewModel.SelectManyAsync([first], CancellationToken.None);

    await viewModel.SelectManyAsync([first, second], CancellationToken.None);

    Assert.Equal(1, context.Metadata.FlushCount);
    Assert.True(viewModel.Selection.IsBatchSelection);
    Assert.True(viewModel.BatchSummary.IsVisible);
    Assert.Null(viewModel.Inspector.InboxItemId);
}

[Fact]
public async Task Filter_change_clears_selection_but_scan_refresh_intersects_ids()
{
    InboxViewModel viewModel = CreateViewModel(out InboxTestContext context);
    await viewModel.LoadAsync(CancellationToken.None);
    await viewModel.SelectManyAsync(viewModel.Items.Take(2).ToArray(),
        CancellationToken.None);
    Guid remaining = viewModel.Items[0].Id;
    context.Query.RemoveAllExcept(remaining);

    await viewModel.RefreshCommand.ExecuteAsync(null);
    Assert.Equal([remaining], viewModel.Selection.SelectedIds);

    viewModel.SearchText = "new filter";
    await context.DrainAsync();
    Assert.Empty(viewModel.Selection.SelectedIds);
}
~~~

- [ ] **Step 2: Add failing XAML structure assertions**

~~~csharp
Assert.Contains("SelectionMode=\"Extended\"", inboxXaml, StringComparison.Ordinal);
Assert.Contains("AutomationProperties.AutomationId=\"InboxBatchBar\"", inboxXaml,
    StringComparison.Ordinal);
Assert.Contains("AutomationProperties.AutomationId=\"InboxBatchSummary\"", inboxXaml,
    StringComparison.Ordinal);
Assert.Contains("AutomationProperties.AutomationId=\"InboxCancelSelectionButton\"",
    inboxXaml, StringComparison.Ordinal);
Assert.Contains("Modifiers=\"Control\" Key=\"A\"", inboxXaml,
    StringComparison.Ordinal);
Assert.Contains("Key=\"Escape\"", inboxXaml, StringComparison.Ordinal);
~~~

- [ ] **Step 3: Run focused Desktop tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxBatchSummaryViewModelTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
~~~

Expected: FAIL because multi-selection, summary, and stable controls do not exist.

- [ ] **Step 4: Implement the batch summary view model**

~~~csharp
public sealed partial class InboxBatchSummaryViewModel(
    IInboxBatchSummaryService summaryService) : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; private set; }
    [ObservableProperty] public partial int SelectedCount { get; private set; }
    [ObservableProperty] public partial string MediaCountText { get; private set; } = "";
    [ObservableProperty] public partial string StateCountText { get; private set; } = "";
    [ObservableProperty] public partial bool CanEditMetadata { get; private set; }
    [ObservableProperty] public partial bool CanStartProcessing { get; private set; }
    [ObservableProperty] public partial bool CanPreviewIngest { get; private set; }

    public async Task LoadAsync(
        IReadOnlyList<InboxSelectionEntry> selection,
        CancellationToken cancellationToken);
    public void Reset();
}
~~~

Format only counts, for example `图片 3 · 视频 2` and `可入库 2 · 需处理 1 · 冲突 1 · 恢复受阻 0 · 其他 1`; do not reproduce filenames or taxonomy values in summary labels.

- [ ] **Step 5: Integrate selection into `InboxViewModel`**

Add constructor dependencies `InboxSelectionViewModel selection` and `InboxBatchSummaryViewModel batchSummary`. `SelectManyAsync` must flush current single metadata before leaving single mode, call `Selection.ReplaceFromGrid(items, Items)`, then: zero -> reset both inspector/summary; one -> load existing single details; two+ -> reset inspector and load summary. `ReloadCardsAsync` calls `Selection.Reconcile(Items)` and then refreshes the appropriate single/batch panel. Search/status change calls `Selection.Clear()` before the query; manual/activity scan reload does not.

Before `Items.Clear()`, raise `GridItemsReplacing`; after repopulating cards, call `Selection.Reconcile(Items)` and raise `GridItemsReplaced` in `finally`. This creates a defined boundary for resetting WinUI's range anchor while retaining selected IDs across scans.

- [ ] **Step 6: Convert the GridView and add working selection controls**

Use `SelectionMode="Extended"`. In `OnSelectionChanged`, return immediately while `_synchronizingGridSelection` is true; otherwise pass `InboxGrid.SelectedItems.Cast<InboxCardViewModel>().ToArray()` to `SelectManyAsync`, then synchronize single pickers only when `Selection.IsSingleSelection`. `GridItemsReplacing` sets the synchronization flag; `GridItemsReplaced` clears the native selection, re-adds cards whose IDs occur in `Selection.SelectedIds`, then releases the flag. Because the Items collection was replaced before re-selection, WinUI's old Shift range anchor is discarded. Add `KeyboardAccelerator` handlers: Ctrl+A calls `InboxGrid.SelectAll()` and Esc calls `InboxGrid.SelectedItems.Clear()`. The top batch bar initially contains selected count and the working Cancel Selection button only; action buttons are added when their commands are implemented in Tasks 11-13. Bind single inspector visibility to `Selection.IsSingleSelection` through explicit `Visibility` properties on `InboxViewModel`, and batch summary visibility to `BatchSummary.IsVisible`.

- [ ] **Step 7: Register the new Desktop view models**

~~~csharp
services.AddSingleton<InboxSelectionViewModel>();
services.AddSingleton<InboxBatchSummaryViewModel>();
~~~

- [ ] **Step 8: Run Desktop and composition tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxBatchSummaryViewModelTests|FullyQualifiedName~InboxSelectionViewModelTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

Expected: PASS; Release build has 0 warnings/errors; existing single-item picker and preview bindings still compile.

- [ ] **Step 9: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): add native inbox multi-selection"
~~~

### Task 10: Build a Deferred Batch Metadata Editor

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/MetadataContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchTaxonomyPickerViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialogViewModel.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/BatchTaxonomyPickerViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/BatchMetadataDialogViewModelTests.cs`

**Interfaces:**
- Produces: narrow `ITaxonomySearchService.SearchAsync(...)`; existing `ITaxonomyService` extends it.
- Produces: `BatchTaxonomyPickerViewModel` that returns `BatchTaxonomyReference` values without creating terms.
- Produces: `BatchTermCoverageOptionViewModel(BatchTermCoverageDto Coverage, int TotalSelected)` with `DisplayText` formatted as `名称 · 覆盖数/总数`.
- Produces: `BatchMetadataDialogViewModel.Load(...)`, action-tab state, explicit Replace enable/clear state, `TryBuildCommand(out BatchMetadataCommand?, out string?)`, `ApplyResult(...)`, and retained input after failure.
- Consumes: Task 2 `BatchMetadataEditorDto` and command records.

- [ ] **Step 1: Write failing deferred-picker tests**

~~~csharp
[Fact]
public async Task Free_text_becomes_a_deferred_reference_without_create_call()
{
    var taxonomy = new RecordingTaxonomySearchService();
    var picker = new BatchTaxonomyPickerViewModel(taxonomy, TaxonomyKind.Author);
    picker.InputText = " 作者甲 ";

    await picker.CommitInputAsync(CancellationToken.None);

    BatchTaxonomyReference value = Assert.Single(picker.SelectedValues);
    Assert.Null(value.ExistingId);
    Assert.Equal("作者甲", value.NewName);
    Assert.Equal(0, taxonomy.CreateCallCount);
}

[Fact]
public async Task Existing_normalized_match_is_referenced_by_id()
{
    TaxonomyTermDto existing = Term(TaxonomyKind.Tag, "风景");
    var picker = new BatchTaxonomyPickerViewModel(
        new RecordingTaxonomySearchService(existing), TaxonomyKind.Tag);
    picker.InputText = "風景";

    await picker.CommitInputAsync(CancellationToken.None);

    Assert.Equal(existing.Id, Assert.Single(picker.SelectedValues).ExistingId);
}
~~~

The second test's fake returns a term whose `NormalizedName` equals `TaxonomyName.Create("風景").Normalized`; the displayed spelling may differ.

- [ ] **Step 2: Write failing explicit Replace and Remove tests**

~~~csharp
[Fact]
public void Replace_fields_start_unchanged_and_blank_input_does_not_clear()
{
    BatchMetadataDialogViewModel viewModel = CreateEditor();
    viewModel.Load(EditorForThreeItems(), Selection(3));
    viewModel.Mode = BatchMetadataMode.Replace;

    bool built = viewModel.TryBuildCommand(out _, out string? error);

    Assert.False(viewModel.ReplaceAuthorsEnabled);
    Assert.False(viewModel.ReplaceSeriesEnabled);
    Assert.False(viewModel.ReplaceTagsEnabled);
    Assert.False(built);
    Assert.Equal("请先启用至少一个要替换的字段。", error);
}

[Fact]
public void Explicit_clear_builds_clear_while_disabled_field_stays_unchanged()
{
    BatchMetadataDialogViewModel viewModel = CreateEditor();
    viewModel.Load(EditorForThreeItems(), Selection(3));
    viewModel.Mode = BatchMetadataMode.Replace;
    viewModel.ReplaceSeriesEnabled = true;
    viewModel.ClearSeries = true;

    Assert.True(viewModel.TryBuildCommand(
        out BatchMetadataCommand? command, out _));
    Assert.Equal(BatchMetadataFieldOperation.Unchanged,
        command!.Authors.Operation);
    Assert.Equal(BatchMetadataFieldOperation.Clear, command.Series.Operation);
}

[Fact]
public void Remove_page_exposes_only_terms_present_in_the_editor_coverage()
{
    BatchMetadataDialogViewModel viewModel = CreateEditor();
    BatchMetadataEditorDto editor = EditorWithAuthorCoverage("作者甲", 2, 3);
    viewModel.Load(editor, Selection(3));
    viewModel.Mode = BatchMetadataMode.Remove;

    BatchTermCoverageOptionViewModel option = Assert.Single(viewModel.RemoveAuthors);
    Assert.Equal("作者甲 · 2/3", option.DisplayText);
    Assert.False(viewModel.RemoveAuthorPicker.AllowsFreeText);
}
~~~

- [ ] **Step 3: Run both focused test classes and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchTaxonomyPickerViewModelTests|FullyQualifiedName~BatchMetadataDialogViewModelTests"
~~~

Expected: FAIL because the narrow search port and editor view models do not exist.

- [ ] **Step 4: Narrow the read-only taxonomy dependency**

~~~csharp
public interface ITaxonomySearchService
{
    Task<IReadOnlyList<TaxonomyTermDto>> SearchAsync(
        TaxonomyKind kind,
        string? query,
        CancellationToken cancellationToken);
}

public interface ITaxonomyService : ITaxonomySearchService
{
    Task<TaxonomyTermDto> CreateAsync(
        TaxonomyKind kind,
        string name,
        CancellationToken cancellationToken);
    Task<TaxonomyMutationResult> RenameAsync(
        Guid id,
        string name,
        CancellationToken cancellationToken);
    Task<TaxonomyMutationResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken);
}
~~~

Register `ITaxonomySearchService` to the existing singleton `TaxonomyService`. Do not change the existing single-item picker, whose immediate-create behavior remains correct for single-item editing.

- [ ] **Step 5: Implement the deferred picker**

The picker calls only `ITaxonomySearchService.SearchAsync`. On commit, parse with `TaxonomyName.Create`, reuse a returned exact normalized/kind match as `BatchTaxonomyReference.Existing`, otherwise append `BatchTaxonomyReference.New(kind, parsed.Display)`. Deduplicate selected values by persisted ID or normalized new name and preserve first occurrence. `Remove` mode uses `SetAvailableExisting(...)` and sets `AllowsFreeText = false`.

- [ ] **Step 6: Implement the action-centric editor state**

The view model owns separate Add, Remove, and Replace picker instances so switching tabs cannot reinterpret prior input. `TryBuildCommand` maps:

~~~csharp
BatchMetadataFieldOperation SetReplaceOperation(
    bool enabled,
    bool clear,
    int selectedCount) =>
    !enabled ? BatchMetadataFieldOperation.Unchanged
    : clear ? BatchMetadataFieldOperation.Clear
    : selectedCount > 0 ? BatchMetadataFieldOperation.Replace
    : throw new BatchMetadataEditorValidationException(
        "已启用的替换字段必须选择资料或明确清空。");

private sealed class BatchMetadataEditorValidationException(string message)
    : InvalidOperationException(message);
~~~

Add requires at least one author/tag value; Remove requires at least one checked existing author/tag; Replace requires at least one enabled field. Series appears only in Replace and accepts zero-or-one selected reference. `ApplyResult` sets a stable user-facing error and leaves every picker/toggle unchanged when `Succeeded` is false.

- [ ] **Step 7: Run focused and existing taxonomy tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchTaxonomyPickerViewModelTests|FullyQualifiedName~BatchMetadataDialogViewModelTests|FullyQualifiedName~TaxonomyPickerViewModelTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TaxonomyServiceTests|FullyQualifiedName~BatchMetadata"
~~~

Expected: PASS; the recording search service has no mutation method to call.

- [ ] **Step 8: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs apps/media-desktop/tests
git commit -m "feat(media-desktop): add deferred batch metadata editor"
~~~

### Task 11: Show the Native Batch Metadata Dialog and Submit In Place

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialog.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialog.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/IBatchMetadataDialogService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/WindowsBatchMetadataDialogService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchMetadataDialogViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`

**Interfaces:**
- Produces: `BatchMetadataDialogResult(bool Applied, BatchMetadataResult? Result)`.
- Produces: `IBatchMetadataDialogService.ShowAsync(BatchMetadataEditorDto editor, IReadOnlyList<InboxSelectionEntry> selection, Func<BatchMetadataCommand, CancellationToken, Task<BatchMetadataResult>> submit, CancellationToken cancellationToken)`.
- Consumes: `IBatchMetadataService.PrepareAsync/ApplyAsync` and Task 10 editor.
- UI: action tabs, explicit field enable/clear controls, in-dialog validation/error, submit lock, and `InboxBatchMetadataButton`.

- [ ] **Step 1: Write a failing Inbox command test that proves retry remains in the dialog**

~~~csharp
[Fact]
public async Task Batch_metadata_command_passes_atomic_submit_to_one_dialog_and_reloads_on_success()
{
    InboxViewModel viewModel = CreateBatchSelectedViewModel(
        out InboxTestContext context);
    context.BatchMetadata.PrepareResult = EditorForTwoItems();
    context.BatchMetadata.ApplyResults.Enqueue(
        new BatchMetadataResult(false, 0, 0, 2,
            "batch.metadata.version_conflict"));
    context.BatchMetadata.ApplyResults.Enqueue(
        new BatchMetadataResult(true, 2, 0, 2, null));
    context.BatchMetadataDialog.SubmitTwiceBeforeClosing = true;

    await viewModel.BatchEditMetadataCommand.ExecuteAsync(null);

    Assert.Equal(1, context.BatchMetadataDialog.ShowCount);
    Assert.Equal(2, context.BatchMetadata.ApplyCount);
    Assert.True(context.BatchMetadataDialog.InputWasRetainedAfterFailure);
    Assert.True(context.Query.SearchCount >= 2);
}
~~~

- [ ] **Step 2: Add failing native-dialog structure assertions**

~~~csharp
foreach (string id in new[]
{
    "BatchMetadataDialog",
    "BatchMetadataAddTab",
    "BatchMetadataRemoveTab",
    "BatchMetadataReplaceTab",
    "BatchReplaceAuthorsEnabled",
    "BatchReplaceAuthorsClear",
    "BatchReplaceSeriesEnabled",
    "BatchReplaceSeriesClear",
    "BatchReplaceTagsEnabled",
    "BatchReplaceTagsClear",
    "BatchMetadataSubmitButton",
    "InboxBatchMetadataButton"
})
{
    Assert.Contains($"AutomationProperties.AutomationId=\"{id}\"",
        batchDialogXaml + inboxXaml, StringComparison.Ordinal);
}
~~~

- [ ] **Step 3: Run focused Desktop tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
~~~

Expected: FAIL because the dialog service, command, and controls do not exist.

- [ ] **Step 4: Implement the native dialog service contract**

~~~csharp
public sealed record BatchMetadataDialogResult(
    bool Applied,
    BatchMetadataResult? Result);

public interface IBatchMetadataDialogService
{
    Task<BatchMetadataDialogResult> ShowAsync(
        BatchMetadataEditorDto editor,
        IReadOnlyList<InboxSelectionEntry> selection,
        Func<BatchMetadataCommand, CancellationToken,
            Task<BatchMetadataResult>> submit,
        CancellationToken cancellationToken);
}
~~~

`WindowsBatchMetadataDialogService` creates one `BatchMetadataDialogViewModel`, assigns `WindowContext.XamlRoot`, and passes the submit delegate into `BatchMetadataDialog`. Use a custom in-content submit button so it owns stable AutomationId `BatchMetadataSubmitButton`; its async click handler disables duplicate submission, calls `TryBuildCommand`, awaits `submit`, calls `ApplyResult`, and hides/returns applied only on success. On failure, keep the same dialog instance and editor state open. The native close button and Esc return `Applied=false` and never invoke submit.

- [ ] **Step 5: Build the action-tab XAML**

Use a native `ContentDialog` containing `TabView` or `NavigationView`-free `Pivot` with Add/Remove/Replace pages. Add and Replace use deferred picker controls bound to Task 10; Remove renders coverage options such as `作者甲 · 2/3`. Each Replace field has an enable checkbox and a separate clear checkbox/radio; the selected-value control is disabled while clear is selected. Set an accessible name on every button/toggle and focus the first Add author input on load.

- [ ] **Step 6: Wire the application command into `InboxViewModel`**

~~~csharp
[RelayCommand(CanExecute = nameof(CanBatchEditMetadata))]
private async Task BatchEditMetadataAsync(CancellationToken cancellationToken)
{
    IReadOnlyList<InboxSelectionEntry> selection = Selection.Capture();
    BatchMetadataEditorDto? editor = await batchMetadata.PrepareAsync(
        selection, cancellationToken);
    if (editor is null)
    {
        OperationMessage = "选择已变化，请刷新后重新选择。";
        await ReloadCardsAsync(cancellationToken);
        return;
    }

    BatchMetadataDialogResult dialogResult = await batchMetadataDialog.ShowAsync(
        editor, selection, batchMetadata.ApplyAsync, cancellationToken);
    if (dialogResult.Applied)
    {
        OperationMessage = $"已修改 {dialogResult.Result!.ChangedCount} 项资料，"
            + $"{dialogResult.Result.UnchangedCount} 项无变化。";
        await ReloadCardsAsync(cancellationToken);
    }
}
~~~

Gate on `Selection.IsBatchSelection`, `BatchSummary.CanEditMetadata`, and `!IsBusy`. The command sets the shared batch busy gate for its full lifetime.

- [ ] **Step 7: Add the top action button and DI registration**

Add `InboxBatchMetadataButton` to the existing batch bar, bound to `BatchEditMetadataCommand`. Register `IBatchMetadataDialogService` as `WindowsBatchMetadataDialogService`; the service resolves `ITaxonomySearchService` and `WindowContext` only.

- [ ] **Step 8: Run Desktop, metadata, and Release build gates**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadata|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchMetadataPersistenceTests"
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

Expected: PASS, 0 warnings/errors; a failed atomic submit does not close the dialog.

- [ ] **Step 9: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): wire native batch metadata dialog"
~~~

### Task 12: Add the Independent Batch Preprocessing Command

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxBatchSummaryViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`

**Interfaces:**
- Consumes: `IBatchProcessingService.StartAsync(...)` and `BatchProcessingResult`.
- Produces: `InboxViewModel.BatchStartProcessingCommand`, result summary text, and `InboxBatchStartProcessingButton`.
- Guarantees: only the Application service chooses applicable items; Desktop does not inspect capability names or `Mp4M4a`.

- [ ] **Step 1: Write a failing mixed-selection command test**

~~~csharp
[Fact]
public async Task Batch_processing_passes_the_complete_snapshot_and_keeps_selection()
{
    InboxViewModel viewModel = CreateBatchSelectedViewModel(
        out InboxTestContext context);
    context.BatchProcessing.Result = new BatchProcessingResult(
    [
        new(viewModel.Selection.SelectedIds[0], 0,
            BatchProcessingItemOutcome.NotRequired, null),
        new(viewModel.Selection.SelectedIds[1], 1,
            BatchProcessingItemOutcome.Queued, null)
    ]);

    await viewModel.BatchStartProcessingCommand.ExecuteAsync(null);

    Assert.Equal(2, context.BatchProcessing.LastSelection!.Count);
    Assert.Contains("已排队 1 项", viewModel.OperationMessage,
        StringComparison.Ordinal);
    Assert.Equal(2, viewModel.Selection.SelectedCount);
}
~~~

- [ ] **Step 2: Add a failing XAML assertion and run focused tests**

~~~csharp
Assert.Contains(
    "AutomationProperties.AutomationId=\"InboxBatchStartProcessingButton\"",
    inboxXaml, StringComparison.Ordinal);
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
~~~

Expected: FAIL because the command/button do not exist.

- [ ] **Step 3: Add the command and result presentation**

~~~csharp
[RelayCommand(CanExecute = nameof(CanBatchStartProcessing))]
private async Task BatchStartProcessingAsync(CancellationToken cancellationToken)
{
    IsBusy = true;
    try
    {
        BatchProcessingResult result = await batchProcessing.StartAsync(
            Selection.Capture(), cancellationToken);
        OperationMessage =
            $"已排队 {Count(result, BatchProcessingItemOutcome.Queued)} 项，"
            + $"已在队列 {Count(result, BatchProcessingItemOutcome.AlreadyActive)} 项，"
            + $"无需处理 {Count(result, BatchProcessingItemOutcome.NotRequired)} 项，"
            + $"需要检查 {Count(result, BatchProcessingItemOutcome.NeedsAttention)} 项。";
        await ReloadCardsAsync(cancellationToken);
    }
    finally
    {
        IsBusy = false;
    }
}
~~~

Gate on batch selection, `BatchSummary.CanStartProcessing`, `ProcessingAvailable`, and the shared busy state. Do not automatically invoke this command after scanning or metadata submission.

- [ ] **Step 4: Add the top button and run focused regressions**

Bind `InboxBatchStartProcessingButton` to `BatchStartProcessingCommand`, content `开始前置处理`, with an accessible name explaining that only applicable items enter the queue.

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchProcessingServiceTests|FullyQualifiedName~ProcessingCommandServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcessingQueueWorkerTests|FullyQualifiedName~Mp4M4aProcessingFlowTests"
~~~

Expected: PASS; existing queue worker seriality tests remain green.

- [ ] **Step 5: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages
git commit -m "feat(media-desktop): expose batch preprocessing command"
~~~

### Task 13: Confirm, Run, Stop, and Summarize Batch Ingest

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchIngestConfirmationViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchIngestConfirmationDialog.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/BatchIngestConfirmationDialog.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/IBatchIngestConfirmationService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/WindowsBatchIngestConfirmationService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxBatchSummaryViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/BatchIngestConfirmationViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`

**Interfaces:**
- Produces: `IBatchIngestConfirmationService.ConfirmAsync(BatchIngestPreviewDto preview, CancellationToken cancellationToken)` returning `bool`.
- Extends: `InboxBatchSummaryViewModel` with `BeginIngest`, `Report`, `Complete`, overall progress, current safe name, stop visibility, and final counts.
- Produces: `BatchIngestCommand`, `StopBatchIngestCommand`, `ShowBatchIncompleteOnlyCommand`, and native controls for confirm/progress/stop/result.

- [ ] **Step 1: Write failing confirmation and command tests**

~~~csharp
[Fact]
public void Confirmation_shows_all_categories_and_disables_confirm_without_ready_items()
{
    BatchIngestPreviewDto preview = Preview(
        ready: 0, processing: 2, conflicts: 1, recovery: 1, changed: 3);
    var viewModel = new BatchIngestConfirmationViewModel(preview);

    Assert.False(viewModel.CanConfirm);
    Assert.Equal("可直接入库 0", viewModel.ReadyText);
    Assert.Equal("需要前置处理 2", viewModel.RequiresProcessingText);
    Assert.Equal("存在冲突 1", viewModel.ConflictText);
    Assert.Equal("恢复受阻 1", viewModel.RecoveryText);
    Assert.Equal("变化或不可操作 3", viewModel.ChangedText);
}

[Fact]
public async Task Confirmed_batch_ingest_reports_progress_and_retains_only_unfinished_ids()
{
    InboxViewModel viewModel = CreateBatchSelectedViewModel(
        out InboxTestContext context, itemCount: 3);
    context.BatchIngest.Preview = Preview(ready: 2, conflicts: 1);
    context.BatchIngest.Result = Result(
        completedIds: viewModel.Selection.SelectedIds.Take(2),
        conflictIds: viewModel.Selection.SelectedIds.Skip(2));
    context.BatchConfirmation.Confirmed = true;

    await viewModel.BatchIngestCommand.ExecuteAsync(null);

    Assert.Equal(1, context.BatchConfirmation.ShowCount);
    Assert.Equal(1, viewModel.Selection.SelectedCount);
    Assert.Equal(2, viewModel.BatchSummary.CompletedCount);
    Assert.Equal(1, viewModel.BatchSummary.ConflictCount);
    Assert.Equal(0, context.ConflictDialog.ShowCount);
}

[Fact]
public async Task Stop_command_cancels_only_the_between_item_token()
{
    InboxViewModel viewModel = CreateBatchSelectedViewModel(
        out InboxTestContext context, itemCount: 2);
    context.BatchConfirmation.Confirmed = true;
    context.BatchIngest.OnFirstProgress = () =>
        viewModel.StopBatchIngestCommand.Execute(null);

    await viewModel.BatchIngestCommand.ExecuteAsync(null);

    Assert.True(context.BatchIngest.StopTokenWasCancelled);
    Assert.False(context.BatchIngest.OperationTokenWasCancelled);
}
~~~

- [ ] **Step 2: Add failing stable-control assertions**

~~~csharp
foreach (string id in new[]
{
    "InboxBatchIngestButton",
    "BatchIngestConfirmationDialog",
    "BatchIngestConfirmButton",
    "InboxBatchIngestProgress",
    "InboxBatchStopAfterCurrentButton",
    "InboxBatchIngestResult",
    "InboxBatchShowIncompleteButton"
})
{
    Assert.Contains($"AutomationProperties.AutomationId=\"{id}\"",
        inboxXaml + confirmationXaml, StringComparison.Ordinal);
}
~~~

- [ ] **Step 3: Run focused Desktop tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchIngestConfirmationViewModelTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
~~~

Expected: FAIL because confirmation, progress, stop, result, and incomplete filter do not exist.

- [ ] **Step 4: Implement the one-summary confirmation dialog**

~~~csharp
public interface IBatchIngestConfirmationService
{
    Task<bool> ConfirmAsync(
        BatchIngestPreviewDto preview,
        CancellationToken cancellationToken);
}
~~~

The native dialog lists five category counts only, has `确认入库` and `取消`, and enables confirm only when `preview.ReadyCount > 0`. It does not list paths and does not expose per-item conflict choices.

- [ ] **Step 5: Extend the batch summary with progress and result state**

~~~csharp
public void BeginIngest(int totalItems);
public void Report(BatchIngestProgress progress);
public void Complete(BatchIngestResult result);
~~~

Expose `IsIngestRunning`, `CompletedItems`, `TotalItems`, `ProgressPercent`, `CurrentItemText`, `CanStopAfterCurrent`, `CompletedCount`, `ConflictCount`, `RequiresProcessingCount`, `FailedOrChangedCount`, `NotStartedCount`, `HasFinalResult`, and `HasIncompleteItems`. `CurrentItemText` may show the DTO `SafeName`; no logging call receives it.

- [ ] **Step 6: Implement the Inbox orchestration and stop-after-current token**

~~~csharp
[RelayCommand(CanExecute = nameof(CanBatchIngest))]
private async Task BatchIngestAsync(CancellationToken cancellationToken)
{
    IReadOnlyList<InboxSelectionEntry> selection = Selection.Capture();
    BatchIngestPreviewDto preview = await batchIngest.PreviewAsync(
        selection, cancellationToken);
    if (!await batchConfirmation.ConfirmAsync(preview, cancellationToken))
    {
        return;
    }

    IsBusy = true;
    using var stopAfterCurrent = new CancellationTokenSource();
    _batchStopAfterCurrent = stopAfterCurrent;
    BatchSummary.BeginIngest(selection.Count);
    try
    {
        var progress = new Progress<BatchIngestProgress>(BatchSummary.Report);
        BatchIngestResult result = await batchIngest.ExecuteAsync(
            selection, progress, stopAfterCurrent.Token, cancellationToken);
        _batchIncompleteIds = result.Items
            .Where(item => item.Outcome != BatchIngestItemOutcome.Completed)
            .Select(item => item.InboxItemId)
            .ToHashSet();
        await ReloadCardsAsync(cancellationToken);
        Selection.Retain(_batchIncompleteIds);
        BatchSummary.Complete(result);
    }
    finally
    {
        _batchStopAfterCurrent = null;
        IsBusy = false;
    }
}

[RelayCommand(CanExecute = nameof(CanStopBatchIngest))]
private void StopBatchIngest() => _batchStopAfterCurrent?.Cancel();
~~~

The lifecycle `cancellationToken` is passed as the fourth argument and is never cancelled by the Stop button. The command does not call `ShowConflictAsync` for any result.

- [ ] **Step 7: Add incomplete-only presentation and controls**

`ShowBatchIncompleteOnlyCommand` sets a Desktop-local `_batchIncompleteFilterEnabled` flag and reloads current query cards filtered by `_batchIncompleteIds`; changing search/status or starting a new batch clears this result filter. `InboxBatchSummaryViewModel.Reset` clears current selection counts but does not erase `HasFinalResult`; `BeginIngest` explicitly clears the prior result. Place the final result block outside the single-vs-batch inspector switch so it remains visible even when only one unfinished item remains. Add the top `InboxBatchIngestButton`, right-pane overall `ProgressBar`, Stop After Current button, final count summary, and Show Incomplete button. Successful items disappear naturally after reload; selection retains unfinished IDs that still exist.

- [ ] **Step 8: Register the confirmation service and run integration-focused tests**

~~~csharp
services.AddSingleton<IBatchIngestConfirmationService,
    WindowsBatchIngestConfirmationService>();
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchIngest|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~BatchIngestServiceTests|FullyQualifiedName~IngestServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~OrdinaryMediaFlowTests|FullyQualifiedName~ProcessedMediaIngestFlowTests|FullyQualifiedName~ConflictResolutionFlowTests|FullyQualifiedName~IngestCrashRecoveryFlowTests"
~~~

Expected: PASS; conflict dialog show count stays zero in batch tests; single-item conflict tests remain green.

- [ ] **Step 9: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): add confirmed batch ingest UI"
~~~

### Task 14: Coalesce Refreshes and Finish Focus, Accessibility, and Privacy Gates

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxFocusRequest.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Metadata/BatchMetadataService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Processing/BatchProcessingService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/BatchIngestService.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/ArchitectureTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage5ALoggingPrivacyTests.cs`

**Interfaces:**
- Consumes: existing `IDebounceScheduler`, `IUiDispatcher`, and batch result IDs.
- Produces: 100 ms activity-notification debounce, `InboxFocusRequest`, and a `FocusRequested` event consumed only by `InboxPage`.
- Guarantees: summary updates do not steal focus; dialogs return focus to list; completion focuses first incomplete item or the inbox title; stable `AutomationId` and accessible names exist; no sensitive batch log values.

- [ ] **Step 1: Write failing notification-coalescing and focus tests**

~~~csharp
[Fact]
public async Task Burst_item_notifications_trigger_one_debounced_query_reload()
{
    InboxViewModel viewModel = CreateViewModel(out InboxTestContext context);
    await viewModel.LoadAsync(CancellationToken.None);
    int baseline = context.Query.SearchCount;

    context.Activity.Publish(InboxActivityKind.ItemChanged);
    context.Activity.Publish(InboxActivityKind.ItemChanged);
    context.Activity.Publish(InboxActivityKind.ItemChanged);
    context.Debounce.ReleaseLatest();
    await context.DrainAsync();

    Assert.Equal(baseline + 1, context.Query.SearchCount);
}

[Fact]
public async Task Completed_batch_requests_focus_for_first_incomplete_item()
{
    InboxViewModel viewModel = CreateBatchSelectedViewModel(
        out InboxTestContext context, itemCount: 3);
    Guid incomplete = viewModel.Selection.SelectedIds[1];
    InboxFocusRequest? request = null;
    viewModel.FocusRequested += (_, value) => request = value;
    context.ConfigureCompletedBatchWithIncomplete(incomplete);

    await viewModel.BatchIngestCommand.ExecuteAsync(null);

    Assert.Equal(InboxFocusTarget.Item, request!.Target);
    Assert.Equal(incomplete, request.InboxItemId);
}
~~~

- [ ] **Step 2: Write failing accessibility, boundary, and privacy tests**

Extend the structure test to require `AutomationProperties.Name` beside every Stage 5A `AutomationId`, an `InboxTitle` target, and focus-return handler names in code-behind. Extend `ArchitectureTests` with:

~~~csharp
[Fact]
public void Stage5A_desktop_does_not_know_specific_processor_or_batch_persistence_types()
{
    string desktopRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "TraceFold.Media.Desktop"));
    string[] desktopFiles = Directory.GetFiles(
        desktopRoot, "*.cs", SearchOption.AllDirectories);
    string desktop = string.Join('\n', desktopFiles.Select(File.ReadAllText));

    Assert.DoesNotContain("Mp4M4a", desktop, StringComparison.Ordinal);
    Assert.DoesNotContain("BatchJobRow", desktop, StringComparison.Ordinal);
    Assert.DoesNotContain("BatchIngestOperationRow", desktop, StringComparison.Ordinal);
}
~~~

In `Stage5ALoggingPrivacyTests`, attach a collecting Serilog sink, run representative batch metadata failure, batch processing, and batch ingest results using sentinel values `C:\\PRIVATE\\video.mp4`, `秘密显示名`, `秘密作者`, and a 64-character hash, then assert none occur in rendered logs while stable batch error codes and counts do.

- [ ] **Step 3: Run the focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release --no-restore
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage5ALoggingPrivacyTests"
~~~

Expected: FAIL because debouncing, focus events, complete accessible names, and Stage 5A privacy proof are absent.

- [ ] **Step 4: Coalesce only UI reload work**

Add a private activity CTS protected by a lock. Each activity notification cancels/disposes the prior CTS and schedules:

~~~csharp
private async Task ReloadAfterActivityDelayAsync(CancellationToken token)
{
    try
    {
        await debounceScheduler.DelayAsync(TimeSpan.FromMilliseconds(100), token);
        await ReloadCardsAsync(token);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
}
~~~

Dispatch the scheduling entry through `IUiDispatcher`; persisted item transitions remain immediate. Explicit command completion still calls `ReloadCardsAsync` directly and cancels any pending activity reload first.

- [ ] **Step 5: Add focus requests and implement code-behind focus**

~~~csharp
public enum InboxFocusTarget { List, Item, Title }

public sealed record InboxFocusRequest(
    InboxFocusTarget Target,
    Guid? InboxItemId = null);
~~~

Raise List after the metadata dialog closes, Item for the first incomplete item after batch ingest, and Title when no item remains. `InboxPage` handles the event on its dispatcher: focus `InboxGrid`; or call `ScrollIntoView(item)`, obtain the `GridViewItem`, then focus it; or focus `InboxTitle`. The batch summary's property changes never call focus APIs.

- [ ] **Step 6: Complete accessible names and privacy-safe logs**

Add concise Chinese accessible names to every Stage 5A control. Add optional trailing constructor parameters `ILogger<BatchMetadataService>? logger = null`, `ILogger<BatchProcessingService>? logger = null`, and `ILogger<BatchIngestService>? logger = null` so direct unit-test construction remains source-compatible while production DI supplies loggers. Create one in-memory `BatchId` per command and log one start/finish record using only `BatchId`, selected/changed/outcome counts, ordinal, phase, duration, and stable error code. Never pass `SafeName`, paths, hashes, or taxonomy/display values to a logger. The privacy test must observe count/error-code fields and prove all sentinel values are absent.

- [ ] **Step 7: Run Desktop, architecture, privacy, and Release build gates**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release --no-restore
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LoggingPrivacyTests|FullyQualifiedName~Stage5ALoggingPrivacyTests"
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

Expected: PASS and 0 warnings/errors.

- [ ] **Step 8: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests
git commit -m "test(media-desktop): harden stage five batch interaction"
~~~

---

## Phase D - Package Diagnostics and Offline Acceptance

### Task 15: Split the Stage 4 Package Probe Before Adding New Scenarios

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeOptions.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeEnvironment.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeRunner.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage4ConflictProbeScenario.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage4RecoveryProbeScenario.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage4PackageProbe.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage4PackageProbeOptions.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/PackageProbeRefactorTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage4PackageProbeOptionsTests.cs`

**Interfaces:**
- Produces: `PackageProbeOptionState`, `PackageProbeScenarioKind`, `PackageProbeRequest`, `PackageProbeOptionResult`, `IPackageProbeScenario`, `PackageProbeEnvironment`, and `PackageProbeRunner`.
- Produces: `PackageProbeRunner.RunAsync(PackageProbeRequest request, CancellationToken cancellationToken = default)`.
- Preserves: every existing Stage 4 public type, CLI argument, fixed hash, root prefix, result filename, exit code, and JSON field consumed by current tests/scripts.
- Guarantees: `Stage4PackageProbe.cs` becomes a compatibility facade rather than a 1,068-line scenario container; normal desktop startup still takes no diagnostic dependency path.

- [ ] **Step 1: Add characterization tests before moving code**

~~~csharp
[Fact]
public void Legacy_stage4_arguments_map_to_the_common_request_without_value_changes()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures();

    PackageProbeOptionResult common = PackageProbeOptions.Parse(
    [
        Stage4PackageProbeOptions.RootArgument, root.Path,
        Stage4PackageProbeOptions.TokenArgument, root.TokenText,
        Stage4PackageProbeOptions.ModeArgument,
        Stage4PackageProbeOptions.ConflictMode
    ]);

    Assert.Equal(PackageProbeOptionState.Ready, common.State);
    Assert.Equal(PackageProbeScenarioKind.Stage4Conflict,
        common.Request!.Scenario);
    Assert.Equal(root.Path, common.Request.Root);
    Assert.Equal(root.TokenText, common.Request.OwnerToken);
}

[Fact]
public async Task Common_runner_preserves_the_stage4_conflict_result_schema()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures();
    PackageProbeRequest request = Request.Stage4Conflict(root);

    int exitCode = await PackageProbeRunner.RunAsync(
        request, TestContext.Current.CancellationToken);
    JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(
        Path.Combine(root.Path, Stage4PackageProbeOptions.ResultFilename)));

    Assert.Equal(0, exitCode);
    foreach (string property in new[]
    {
        "Succeeded", "RegisteredReplace", "KeepBoth",
        "UnmanagedReplace", "SkipReopen", "ProcessedConflict",
        "AssetCount", "ErrorCode"
    })
    {
        Assert.True(result.RootElement.TryGetProperty(property, out _), property);
    }
}
~~~

Keep the existing real five-case `Stage4PackageProbeOptionsTests.RunAsync_completes_all_five_real_conflict_probe_cases` unchanged as the strongest characterization.

- [ ] **Step 2: Run the characterization tests before refactoring**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage4PackageProbeOptionsTests"
~~~

Expected: PASS for existing tests. The new `PackageProbeRefactorTests` do not compile until the common types are added.

- [ ] **Step 3: Define common request and scenario interfaces**

~~~csharp
public enum PackageProbeOptionState { NotRequested, Ready, Invalid }

public enum PackageProbeScenarioKind
{
    Stage4Conflict,
    Stage4CrashSeed,
    Stage4CrashResume,
    Stage5ABatch
}

public sealed record PackageProbeRequest(
    string Root,
    string OwnerToken,
    PackageProbeScenarioKind Scenario,
    IngestFaultPoint? FaultPoint,
    Stage4CrashProbeManifest? CrashManifest);

public sealed record PackageProbeOptionResult(
    PackageProbeOptionState State,
    PackageProbeRequest? Request,
    string? ErrorCode);

public interface IPackageProbeScenario
{
    PackageProbeScenarioKind Kind { get; }
    Task<int> RunAsync(
        PackageProbeRequest request,
        PackageProbeEnvironment environment,
        CancellationToken cancellationToken);
}
~~~

`PackageProbeOptions.Parse` initially maps all legacy Stage 4 argument sets exactly. It recognizes no generic Stage 5A arguments until Task 16. `Stage4PackageProbeOptions.Parse` converts the common result back to the existing records so existing call sites and tests remain source-compatible.

- [ ] **Step 4: Extract the guarded environment by method responsibility**

Move, without weakening checks:

- `BuildHost`, `StartHostAsync`, and `StopHostAsync` to host lifecycle methods on `PackageProbeEnvironment`.
- `CopyAndObserveAsync`, `ObserveAsync`, `FindCardAsync`, `WaitForProcessingAsync`, and `FileSha256Async` to fixture/observation methods.
- `ValidateOwnedProbeTree`, `ValidateRootOwnership`, `ProcessingWorkspaceIsEmpty`, `InternalStorageIsClean`, result preexistence checks, and fixed-fixture validation to guard methods.
- `WriteFailureAsync`, `WriteResultAsync`, `TryWriteCrashFailureAsync`, and `WriteCrashResultAsync` to result writers that accept a filename and typed payload.

The environment exposes controlled `InboxDirectory`, `LibraryDirectory`, `WorkDirectory`, `DataDirectory`, and `FixturesDirectory` computed under validated `request.Root`; it never accepts arbitrary child paths from a scenario.

- [ ] **Step 5: Extract Stage 4 scenario modules exactly by method group**

Move the existing conflict `RunAsync` body, `SeedAssetAsync`, `ResolveAsync`, and `CountAssetsWithFilenameAsync` to `Stage4ConflictProbeScenario`. Move `RunCrashSeedAsync`, `RunCrashResumeAsync`, crash manifest checks, and recovery result construction to `Stage4RecoveryProbeScenario`. Keep `DiagnosticIngestFaultInjector` wired only in crash seed mode. Do not alter scenario ordering, timeout values, result records, or cleanup rules.

- [ ] **Step 6: Make the old classes thin facades**

`Stage4PackageProbe.RunAsync(Stage4PackageProbeOptions)` converts to `PackageProbeRequest` and calls `PackageProbeRunner.RunAsync`. Keep `Stage4CrashProbeManifest` in the compatibility file because `DiagnosticIngestFaultInjector` and old tests consume it. Target both old files below 250 lines each; common/environment/scenario files each own one responsibility.

- [ ] **Step 7: Run old and new probe tests plus Stage 4 smoke compatibility**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage4PackageProbeOptionsTests|FullyQualifiedName~PackageProbeRefactorTests"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'T:\Windows\Documents\GitHub\TraceFold\dist\TraceFold-MediaDesktop-Stage4B-win-x64' -StageCheckpoint 4B
~~~

Expected: all probe tests pass against the existing main-workspace Stage 4B package. The smoke output and Stage 4 JSON schema remain unchanged.

- [ ] **Step 8: Commit the behavior-preserving split**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics
git commit -m "refactor(media-desktop): split package probe scenarios"
~~~

### Task 16: Add the Stage 5A Package Scenario and Publish Checkpoint

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5ABatchProbeScenario.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeOptions.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeRunner.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs`
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5APackageProbeTests.cs`

**Interfaces:**
- Produces: strict generic arguments `--package-probe-root`, `--package-probe-token`, and `--package-probe-scenario stage5a-batch`.
- Produces: `Stage5ABatchProbeResult` JSON in `stage5a-probe-result.json`.
- Extends: publish/smoke `StageCheckpoint` validation with `5A` while preserving `4A` and `4B`.
- Verifies: four-item batch metadata, generic processing queueing, serial processing completion, sequential partial ingest, conflict retention, file cleanup, and no residual internal work.

Use this result schema in both the scenario and smoke script:

~~~csharp
public sealed record Stage5ABatchProbeResult(
    bool Succeeded,
    bool AtomicMetadata,
    bool GenericProcessingQueued,
    bool ProcessingCompleted,
    int CompletedIngestCount,
    int ConflictCount,
    bool ConflictSourceRetained,
    bool InternalStorageClean,
    string? ErrorCode);
~~~

- [ ] **Step 1: Write failing option and real-scenario tests**

~~~csharp
[Fact]
public void Stage5A_arguments_require_exact_owned_root_token_and_scenario()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
        prefix: "tracefold-media-desktop-stage5a-probe-");

    PackageProbeOptionResult result = PackageProbeOptions.Parse(
    [
        "--package-probe-root", root.Path,
        "--package-probe-token", root.TokenText,
        "--package-probe-scenario", "stage5a-batch"
    ]);

    Assert.Equal(PackageProbeOptionState.Ready, result.State);
    Assert.Equal(PackageProbeScenarioKind.Stage5ABatch,
        result.Request!.Scenario);
}

[Fact]
public async Task Stage5A_scenario_completes_metadata_processing_and_partial_ingest()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateWithFixtures(
        prefix: "tracefold-media-desktop-stage5a-probe-");
    PackageProbeRequest request = Request.Stage5ABatch(root);

    int exitCode = await PackageProbeRunner.RunAsync(
        request, TestContext.Current.CancellationToken);
    Stage5ABatchProbeResult result = await ReadResultAsync(root.Path);

    Assert.Equal(0, exitCode);
    Assert.True(result.Succeeded);
    Assert.True(result.AtomicMetadata);
    Assert.True(result.GenericProcessingQueued);
    Assert.Equal(3, result.CompletedIngestCount);
    Assert.Equal(1, result.ConflictCount);
    Assert.True(result.ConflictSourceRetained);
    Assert.True(result.InternalStorageClean);
}
~~~

Add invalid cases for mixed legacy/generic arguments, duplicate keys, wrong root prefix, changed owner token, reparse points, extra root entries, changed fixture hashes, unknown scenario, and any fault argument in Stage 5A mode.

- [ ] **Step 2: Run the focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage5APackageProbeTests"
~~~

Expected: FAIL because the generic Stage 5A parser branch and scenario are absent.

- [ ] **Step 3: Extend strict parsing without creating a normal-run backdoor**

The generic parser accepts exactly three key/value pairs in any order and no environment-variable alternative. Validate a direct child of system temp whose name starts `tracefold-media-desktop-stage5a-probe-`, exact `probe-owner.txt` content, no reparse point at or below the root, exactly `fixtures` plus the owner file before start, and the existing pinned hashes for `photo.jpg`, `paired.mp4`, and `paired.m4a`. Normal empty args return `NotRequested`; partial/unknown/mixed args return exit-code-2 `Invalid`.

- [ ] **Step 4: Implement the Stage 5A scenario through public application services**

The scenario performs this exact order inside the owned root:

1. Copy `photo.jpg` to inbox as `photo.jpg` and `photo-copy.jpg`; copy `paired.mp4` once as standalone `ordinary.mp4`; also copy `paired.mp4` and `paired.m4a` under the same `paired` stem.
2. Scan/observe until four inbox items exist: two images, one ordinary Ready video, and one `RequiresProcessing` video.
3. Capture IDs/versions/ordinals, apply one Add author and one Add tag with `IBatchMetadataService`, and read all four documents to prove the same term IDs and version 1.
4. Call `IBatchProcessingService.StartAsync` on all four; assert one `Queued`, three `NotRequired`; let the existing single worker finish and assert the paired item becomes `ReadyToTag`.
5. Preview all four and assert four Ready at that instant.
6. Execute in order `photo.jpg`, `photo-copy.jpg`, `ordinary.mp4`, `paired.mp4`. The first identical photo completes, the second becomes a content conflict and stays in inbox, and both videos complete; assert three Completed and one ConflictBlocked.
7. Prove original paired MP4/M4A inputs and verified processing output are cleaned according to existing ingest behavior; prove the duplicate-photo conflict source remains; prove the formal library has one photo asset, one ordinary video asset, and one paired video asset; prove processing/internal storage has no unowned residue.

Use `IBatchMetadataService`, `IBatchProcessingService`, and `IBatchIngestService`; do not duplicate EF writes or call a Desktop dialog.

- [ ] **Step 5: Make `Program` use the common runner**

~~~csharp
PackageProbeOptionResult probe = PackageProbeOptions.Parse(args);
if (probe.State == PackageProbeOptionState.Invalid)
{
    return 2;
}
if (probe.State == PackageProbeOptionState.Ready)
{
    return PackageProbeRunner.RunAsync(probe.Request!)
        .GetAwaiter().GetResult();
}
~~~

The WinUI/single-instance startup path remains byte-for-byte after this gate.

- [ ] **Step 6: Extend publish and smoke scripts for `5A`**

Change both `ValidateSet` declarations to `('4A','4B','5A')`. For 5A smoke, create an owned Stage 5A root, copy the same three fixed fixtures from `apps/media-api/tests/fixtures/media`, invoke the generic Stage 5A arguments, require exit code 0, parse `stage5a-probe-result.json`, and assert every result field from Step 1. Keep all existing restricted-`PATH`, native-window, no-browser, single-instance, clean-exit, package-local FFmpeg, reparse/ownership cleanup, and residual-process checks. Run Stage 4 crash recovery only when checkpoint is `4B`.

- [ ] **Step 7: Run probe, script syntax, and regression tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage5APackageProbeTests|FullyQualifiedName~Stage4PackageProbeOptionsTests|FullyQualifiedName~PackageProbeRefactorTests"
@('scripts\publish-media-desktop.ps1','scripts\smoke-media-desktop-package.ps1') | ForEach-Object { [void][scriptblock]::Create((Get-Content -LiteralPath $_ -Raw)) }
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
~~~

Expected: PASS, PowerShell parses both scripts, and Release build has 0 warnings/errors.

- [ ] **Step 8: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1
git commit -m "test(media-desktop): add stage five batch package probe"
~~~

### Task 17: Create the Offline Release Candidate and Manual Test Guide

**Files:**
- Create: `docs/media-desktop-stage5a-test-guide.md`
- Generated but never committed: `dist/TraceFold-MediaDesktop-Stage5A-win-x64/`
- Generated but never committed: `dist/TraceFold-MediaDesktop-Stage5A-win-x64.zip`

**Interfaces:**
- Produces: a user-runnable Chinese checklist covering selection, atomic metadata, generic processing, batch ingest, stopping, refresh/focus, shutdown/restart, and package startup.
- Produces: an offline Windows x64 ZIP whose `package-version.json` identifies checkpoint `5A` and the committed guide source commit.

- [ ] **Step 1: Write the manual checklist before stamping the package commit**

The guide must provide exact setup folders and test files, then these result fields for each item: `通过 / 不通过`, observed message, and screenshot filename.

1. Launch the extracted EXE with network disconnected; verify native window, no browser, single instance.
2. Put two images, one ordinary video, and one same-stem MP4/M4A pair in the configured inbox; refresh.
3. Verify click, Ctrl-click, Shift range, Ctrl+A current filter, and Esc clear.
4. Change search/status and verify selection clears; refresh after removing one source and verify selection intersects by ID.
5. Add/remove/replace authors and tags; replace/clear series; verify display names and physical filenames are unchanged.
6. Leave a Replace field disabled and verify it is unchanged; test explicit Clear separately.
7. Confirm the package exposes no concurrency/fault-injection UI. Mark atomic concurrency as `自动化证据` rather than a manual step, citing the stale-version, trigger-rollback, and 600-item `BatchMetadataPersistenceTests`; do not add a production fault UI for this checklist.
8. Select mixed ordinary/processing items and start preprocessing; verify only the pair queues and actual FFmpeg work stays serial.
9. Batch ingest mixed ready/processing/conflict items; verify one summary confirmation, safe sequential progress, no cascade of conflict dialogs, successes leave the inbox, and conflict/invalid items remain.
10. Start a batch and click Stop After Current; verify the current item finishes safely and later items remain.
11. Use Show Incomplete, verify focus returns to the first unfinished item, then clear/change filters.
12. Close during a running batch, restart, and verify no lost source, duplicate asset, unknown formal file, or unexplained backup.
13. Exit and verify no `TraceFold.Media` or package-local `ffmpeg` process remains.

- [ ] **Step 2: Commit the guide so the package stamps a clean source commit**

~~~powershell
git add docs/media-desktop-stage5a-test-guide.md
git commit -m "docs(media-desktop): add stage five batch test guide"
git status --short
~~~

Expected: the guide is committed and the worktree is clean.

- [ ] **Step 3: Run the complete offline verification suite**

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
git diff --check c959969...HEAD
~~~

Expected: restore uses local cache only; Release build is 0 warnings/errors; all 473 baseline tests plus every Stage 5A test pass; `git diff --check` reports nothing. Record the actual per-project and total counts for Task 18.

- [ ] **Step 4: Publish from the committed source and run the package smoke gate**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 5A -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage5A-win-x64' -StageCheckpoint 5A
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage5A-win-x64.zip'
~~~

Expected: Stage 5A probe reports atomic metadata, one generic processing queue, three completed ingests, one retained conflict, clean internal storage; restricted-path/no-browser/single-instance/clean-exit checks pass; a SHA-256 is printed. If `C:\Program Files\ffmpeg` is absent, stop and report the missing pinned local packaging dependency; do not download or substitute tools.

- [ ] **Step 5: Inspect package hygiene and residual processes**

~~~powershell
$package = 'dist\TraceFold-MediaDesktop-Stage5A-win-x64'
Get-ChildItem -LiteralPath $package -Recurse -File | Measure-Object Length -Sum
Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Extension -in '.pdb','.cs','.csproj','.db','.log','.dmp' }
Get-Process -Name 'TraceFold.Media','ffmpeg' -ErrorAction SilentlyContinue | Select-Object Id,ProcessName,Path
git status --short
~~~

Expected: no forbidden development/runtime artifact is listed; no package process remains; only ignored `dist` output exists and Git is clean.

- [ ] **Step 6: Hand the ZIP, hash, and guide to the user and pause**

Provide the absolute ZIP path, SHA-256, source commit, actual automated test total, and clickable guide path. Do not create an acceptance record or declare Stage 5A accepted until the user reports the checklist result.

### Task 18: Record Manual Acceptance and Finish the Branch

**Files:**
- Create after explicit user pass: `docs/media-desktop-stage5a-acceptance.md`

**Interfaces:**
- Consumes: Task 17 automated counts, build output, package inventory/hash/source commit, package smoke result, and the user's manual result.
- Produces: durable Stage 5A acceptance evidence and a clean completed feature branch ready for integration.

- [ ] **Step 1: Interpret manual feedback before writing acceptance**

If any item fails or differs materially from the spec, do not mark acceptance. Reproduce it, use `superpowers:systematic-debugging`, add a failing automated regression, implement the minimal fix with `superpowers:test-driven-development`, rerun focused/full/package gates, publish a new ZIP from the fix commit, and ask the user to retest the affected checklist items. Continue only after explicit pass.

- [ ] **Step 2: Write the acceptance record from actual evidence**

Include:

- date/time and Windows 11 x64 platform;
- feature branch, merge base `c959969`, final functional/package source commit, and acceptance-doc commit relationship;
- per-project and total test counts, 0-warning/0-error build result, and exact commands;
- focused atomic rollback/600-item, batch processing, batch ingest, privacy, architecture, probe, and Stage 4 regression results;
- package directory file count/bytes, ZIP bytes/SHA-256, `package-version.json`, FFmpeg version/hashes, and smoke output;
- each manual checklist item and the user's reported result;
- explicit statement that no clean Windows VM was available if that remains true;
- a requirement matrix proving no second state machine/table, no batch conflict dialog, no automatic processing/ingest, one series, no display/physical filename batch change, and no sensitive logging.

- [ ] **Step 3: Recheck evidence and commit only the acceptance document**

~~~powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
git diff --check c959969...HEAD
git status --short
git add docs/media-desktop-stage5a-acceptance.md
git commit -m "docs(media-desktop): record stage five batch acceptance"
git status --short --branch
~~~

Expected: all tests pass, build is clean, diff check is empty, the acceptance document is the only staged file, and the final worktree is clean.

- [ ] **Step 4: Use the finishing skill for integration options**

Invoke `superpowers:finishing-a-development-branch`, present the verified branch/base/head and the available merge/keep/discard choices, and take no destructive Git action without the user's choice. Do not commit or merge `dist`.

---

## Final Definition of Done

- Explorer-style click/Ctrl/Shift/Ctrl+A/Esc behavior matches current-query semantics and survives scan refresh by ID only.
- Zero/one/many selection switches cleanly between empty, existing single inspector, and batch summary without duplicating single-item logic.
- Add/Remove/Replace/Clear metadata semantics are explicit; no display name or physical filename is batch-edited.
- Deferred new taxonomy terms are created/reused only inside the same all-or-nothing SQLite transaction as every selected draft update.
- A stale/missing/disallowed item, invalid taxonomy reference, constraint failure, or later write-chunk failure leaves all selected metadata and new terms unchanged.
- Batch preprocessing is a selection-order wrapper over the generic existing command and existing serial worker.
- Batch ingest Preview and Execute revalidate independently; item results can partially succeed; conflicts stay in the inbox without dialog cascades; Stop affects only later items.
- Selection contracts never carry paths or hashes; logs exclude filenames, display names, taxonomy values, hashes, dialog input, and probe owner tokens.
- Stage 4 probe behavior remains compatible after modularization, and Stage 5A has a separate guarded scenario instead of expanding the old monolith.
- Full offline Release build/tests, Stage 5A package smoke, package hygiene, residual-process checks, and user manual acceptance all pass before the acceptance document is committed.
