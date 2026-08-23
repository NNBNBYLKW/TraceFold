# Native Media Desktop Stage 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent Windows-style conflict comparison with Skip, Keep Both, and single-target Replace, then harden the existing three-stage ingest pipeline with verified replacement backups, deterministic startup recovery, fault injection, and delayed cleanup retries.

**Architecture:** Persist every conflict as a `ConflictCase`, revalidate the source and selected target after the user decides, and resume one shared `IngestOperation` pipeline through stage, file commit, database commit, and cleanup. Infrastructure owns SQLite and guarded file mutations; Application owns detection, decisions, orchestration, recovery, and cleanup scheduling; Desktop binds DTOs and commands without making path or overwrite decisions.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, xUnit v3, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev.

**Spec:** docs/superpowers/specs/2026-08-23-native-media-desktop-stage4-design.md

## Global Constraints

- Execute in an isolated `codex/native-media-desktop-stage4` worktree created with `superpowers:using-git-worktrees`; branch from `998aa7588769cfc8468791495a40e5596e2df2e5`.
- Target Windows 11 x64; keep `TraceFold.Media.Desktop` unpackaged, self-contained, single-instance, native-windowed, and fully offline.
- Do not run `git pull`, browse, download dependencies, or update pinned packages. Restore only from `apps/media-desktop/.packages` with failed remote sources ignored.
- Apply conflict and recovery behavior to every image and video type already accepted by Stage 3; do not add media formats in Stage 4.
- Keep one ingest pipeline: conflict resolution produces an execution plan for the existing `Created → Staged → FileCommitted → DatabaseCommitted → CleanupPending → Completed` operation.
- Keep at most one active conflict case and at most one active ingest operation per inbox item.
- Closing the conflict dialog or pressing `Esc` performs no mutation; the case remains open and the item remains `ConflictBlocked`.
- `KeepBoth` creates a new physical file and asset. Use `name (2).ext`, then the first higher available suffix, while preserving the metadata display name.
- SHA-256 is a non-unique lookup index after migration. Duplicate hashes remain detectable and may be stored as independent assets.
- `Replace` always targets one explicit match. A same-name target is selected by default; content-only matches require an explicit user selection and never replace all matches.
- Replacing a registered asset preserves its asset ID and physical filename but replaces content, display name, authors, zero-or-one series, and tags from the latest saved inbox metadata snapshot.
- Replacing an unmanaged same-name file creates a new asset only after a verified backup exists.
- Never overwrite a formal target before its exact size and SHA-256 are persisted and a verified backup exists. Keep at least one verified old or new copy throughout replacement.
- Recovery may proceed automatically only when operation ID, guarded path, size, SHA-256, and database version agree. Contradictions become `RecoveryBlocked` without further file mutation.
- Cleanup removes only persisted, guarded entries whose size and SHA-256 still match. Failure does not roll back a committed asset or block other items.
- Automatic cleanup delays are exactly 30 seconds, 2 minutes, 10 minutes, and 1 hour; afterward wait for next startup or an explicit retry.
- Do not add a task center, recovery center, batch conflict UI, batch ingest, advanced filtering, long-term version history, external plugins, scripts, telemetry, or networking.
- Production code receives a no-op fault injector. Crash injection is available only to tests and the ownership-validated Stage 4 diagnostic probe.
- Structured logs may contain operation/case IDs, stages, stable error codes, and counts; they must not contain full paths, filenames, display names, taxonomy values, hashes, or probe owner tokens.
- All automated filesystem tests use test-owned temporary roots. Never read, move, rename, or delete user-configured media directories.
- Every task follows red-green-refactor, ends with focused verification, and makes one reviewable commit. Do not commit `dist`, generated databases, logs, temporary workspaces, probe output, or FFmpeg binaries.

## Before Task 1

- [ ] Confirm the checkout is the isolated feature worktree and starts at the approved specification commit:

~~~powershell
git status --short --branch
git rev-parse HEAD
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
~~~

Expected: the working tree is clean; `HEAD` is `998aa7588769cfc8468791495a40e5596e2df2e5`; the worktree Git directory differs from the common Git directory.

- [ ] Restore from local caches and prove the Stage 3.1 baseline:

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: Release build reports 0 warnings and 0 errors; all 287 baseline tests pass. If the count differs, record the actual count and inspect the difference before editing. If restore, build, or any baseline test fails, stop and report the exact failure.

## File and Responsibility Map

- `Domain/Ingest/Conflict*`: conflict-case transitions and single-target decision invariants only; no EF, paths, or WinUI.
- `Domain/Ingest/IngestOperation*`: durable stage and recovery-blocked transitions.
- `Domain/Inbox`: visible state and allowed actions for conflict and recovery.
- `Application/Ingest/ConflictContracts.cs`: cross-layer records and store/service interfaces.
- `Application/Ingest/ConflictDetectionService.cs`: combine registered assets with guarded on-disk targets and revalidate snapshots.
- `Application/Ingest/ConflictResolutionService.cs`: persist Skip/KeepBoth/Replace decisions and hand a validated plan to one executor.
- `Application/Ingest/IngestExecutionService.cs`: the only stage/file/database/cleanup orchestration path.
- `Application/Ingest/IngestRecoveryDecision.cs`: pure evidence-to-action matrix.
- `Application/Ingest/IngestRecoveryService.cs`: execute safe recovery actions and stop on ambiguity.
- `Application/Ingest/IngestCleanup*`: per-entry cleanup, exact backoff schedule, due-work queries, and manual retry.
- `Infrastructure/Persistence/Conflict*Row.cs` and `EfConflictCaseStore.cs`: active-case uniqueness, match snapshots, decisions, and case state changes.
- `Infrastructure/Persistence/EfIngestStore.cs`: operation snapshots, single-target asset create/update, idempotent asset IDs, backup evidence, and retry schedule.
- `Infrastructure/Filesystem/ControlledMediaStorage.cs`: guarded inspection, suffix allocation, same-volume backup/commit, restore, verification, and deletion.
- `Infrastructure/Workers/IngestCleanupWorker.cs`: isolated delayed cleanup without UI or file rules.
- `Desktop/Dialogs`: native comparison dialog and its presentation-only view model.
- `Desktop/Pages/Inbox*`: open/reopen conflict, project recovery status, and refresh after every operation change.
- `Desktop/Diagnostics/Stage4PackageProbe*`: ownership-validated headless conflict and representative crash/restart package checks.
- `scripts`: Stage 4A/4B offline publish and smoke gates.

---

## Phase A — Stage 4A Conflict Handling

### Task 1: Add Conflict and Recovery Domain Invariants

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/ConflictCaseStatus.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/ConflictResolution.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/ConflictReason.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/ConflictCase.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/IngestBackupState.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/IngestOperationStage.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Ingest/IngestOperation.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItemState.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxAction.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItem.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Ingest/ConflictCaseTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Ingest/IngestOperationTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Inbox/InboxItemTests.cs`

**Interfaces:**
- Produces: `ConflictCaseStatus.Open`, `Chosen`, `Resolved`, `Invalidated`, and `Skipped`.
- Produces: `ConflictResolution.Skip`, `KeepBoth`, and `Replace`.
- Produces: flags `ConflictReason.Name` and `Content`.
- Produces: `ConflictCase.Open(...)`, `Choose(...)`, `Resolve()`, `Invalidate(string)`, and `Skip()`.
- Produces: `InboxAction.ResolveConflict = 2048` and `RecheckRecovery = 4096`.
- Produces: `InboxItemState.RecoveryBlocked` and `IngestOperationStage.RecoveryBlocked`.
- Produces: `IngestBackupState.NotRequired`, `Pending`, `Created`, `Restored`, and `Deleted`.

- [ ] **Step 1: Write failing conflict-case tests**

~~~csharp
[Fact]
public void Replace_requires_one_match_and_can_resolve_once()
{
    Guid matchId = Guid.NewGuid();
    ConflictCase conflict = ConflictCase.Open(
        Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), "photo.jpg", [matchId]);

    Assert.Throws<InvalidOperationException>(() =>
        conflict.Choose(ConflictResolution.Replace, null));

    conflict.Choose(ConflictResolution.Replace, matchId);
    conflict.Resolve();

    Assert.Equal(ConflictCaseStatus.Resolved, conflict.Status);
    Assert.Equal(matchId, conflict.SelectedMatchId);
    Assert.Throws<InvalidOperationException>(() => conflict.Skip());
}

[Fact]
public void Keep_both_and_skip_never_carry_a_replace_target()
{
    Guid matchId = Guid.NewGuid();
    ConflictCase conflict = ConflictCase.Open(
        Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), "photo.jpg", [matchId]);

    Assert.Throws<InvalidOperationException>(() =>
        conflict.Choose(ConflictResolution.KeepBoth, matchId));
    conflict.Skip();

    Assert.Equal(ConflictCaseStatus.Skipped, conflict.Status);
    Assert.Null(conflict.SelectedMatchId);
}
~~~

- [ ] **Step 2: Write failing inbox and operation tests**

~~~csharp
[Fact]
public void Conflict_item_can_reopen_or_return_to_ready_without_ingesting()
{
    InboxItem item = ReadyItem();
    item.BlockConflict("ingest.conflict");

    Assert.True(item.AllowedActions.HasFlag(InboxAction.ResolveConflict));
    Assert.False(item.AllowedActions.HasFlag(InboxAction.Ingest));

    item.ReturnConflictToReady();
    Assert.Equal(InboxItemState.ReadyToTag, item.State);
}

[Fact]
public void Recovery_block_remembers_the_last_durable_stage()
{
    var operation = new IngestOperation(Guid.NewGuid(), Guid.NewGuid());
    operation.AdvanceTo(IngestOperationStage.Staged);

    operation.BlockRecovery();

    Assert.Equal(IngestOperationStage.RecoveryBlocked, operation.Stage);
    Assert.Equal(IngestOperationStage.Staged, operation.BlockedFromStage);
}
~~~

Use the existing ready-item setup pattern in `InboxItemTests`; do not add a public state setter.

- [ ] **Step 3: Run Domain tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictCaseTests|FullyQualifiedName~IngestOperationTests|FullyQualifiedName~InboxItemTests"
~~~

Expected: FAIL because the new types, actions, states, and transitions do not exist.

- [ ] **Step 4: Implement the conflict aggregate**

Use this public shape and reject all transitions not represented here:

~~~csharp
public enum ConflictCaseStatus { Open, Chosen, Resolved, Invalidated, Skipped }
public enum ConflictResolution { Skip, KeepBoth, Replace }

[Flags]
public enum ConflictReason { None = 0, Name = 1, Content = 2 }

public sealed class ConflictCase
{
    public Guid Id { get; }
    public Guid InboxItemId { get; }
    public string InputSha256 { get; }
    public string TargetFilename { get; }
    public ConflictCaseStatus Status { get; private set; }
    public ConflictResolution? Resolution { get; private set; }
    public Guid? SelectedMatchId { get; private set; }
    public string? InvalidationReason { get; private set; }

    public static ConflictCase Open(
        Guid id,
        Guid inboxItemId,
        string inputSha256,
        string targetFilename,
        IReadOnlyCollection<Guid> matchIds);

    public void Choose(ConflictResolution resolution, Guid? selectedMatchId);
    public void Resolve();
    public void Invalidate(string reason);
    public void Skip();
}
~~~

`Choose(Replace, id)` requires `id` to belong to the immutable match-ID set. `Choose(Skip, null)` calls `Skip()` directly. `Choose(KeepBoth, null)` moves `Open → Chosen`. `Resolve()` accepts only `Chosen`; `Invalidate()` accepts `Open` or `Chosen`; every terminal state rejects further mutation.

Calling `Choose` again on `Chosen` is idempotent only when resolution and selected match are identical. This covers a process interruption after the choice was persisted but before an ingest operation was created; a different second choice is rejected and requires reopening/revalidation through the application service.

- [ ] **Step 5: Implement visible conflict and recovery actions**

Append enum values without renumbering current values. Extend `InboxItem.AllowedActions` exactly as follows:

~~~csharp
InboxItemState.ConflictBlocked =>
    InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder |
    InboxAction.ResolveConflict,
InboxItemState.RecoveryBlocked =>
    InboxAction.Preview | InboxAction.OpenFolder | InboxAction.RecheckRecovery,
~~~

Add `ReturnConflictToReady()` for Skip/invalidation, `BeginResolvedIngest()` for a chosen case, and `BlockRecovery(string)` from ingesting/cleanup/failed ingest states. `ResetForSourceChange()` must accept `RecoveryBlocked`. Do not expose `Ingest` from either blocked state.

Extend `IngestOperation` with `BlockedFromStage`; `BlockRecovery()` records the current nonterminal stage and moves to `RecoveryBlocked`. `ResumeRecovery()` restores the recorded stage and clears it. A completed or failed operation cannot be blocked or resumed.

- [ ] **Step 6: Run focused Domain tests and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictCaseTests|FullyQualifiedName~IngestOperationTests|FullyQualifiedName~InboxItemTests"
git add apps/media-desktop/src/TraceFold.Media.Domain apps/media-desktop/tests/TraceFold.Media.Domain.Tests
git commit -m "feat(media-desktop): model conflict decisions"
~~~

Expected: focused Domain tests pass.

---

### Task 2: Define Conflict, Execution, and Recovery Contracts

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/ConflictContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Storage/FileStorageContracts.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/ConflictContractTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`

**Interfaces:**
- Produces: immutable conflict input/match/case records and comparison DTOs.
- Produces: `IConflictCaseStore`, `IConflictDetectionService`, `IConflictQueryService`, and `IConflictResolutionService`.
- Produces: `IngestDisposition.Create`, `KeepBoth`, `ReplaceRegistered`, and `ReplaceUnregistered`.
- Produces: `IngestExecutionPlan` and `IIngestExecutionService`.
- Produces: guarded library snapshots and replacement backup storage operations.
- Changes: `IngestResult` includes `Guid? ConflictCaseId` without overloading `OperationId`.

- [ ] **Step 1: Write failing contract-shape tests**

~~~csharp
[Fact]
public void Replace_plan_requires_a_target_snapshot()
{
    Assert.Throws<ArgumentException>(() => IngestExecutionPlan.ReplaceRegistered(
        Guid.NewGuid(), Guid.NewGuid(), replacementMetadataVersion: 0, target: null!));
}

[Fact]
public void Conflict_result_keeps_case_and_operation_ids_distinct()
{
    Guid caseId = Guid.NewGuid();
    var result = new IngestResult(
        IngestOutcome.ConflictBlocked, null, null, caseId, "ingest.conflict");

    Assert.Null(result.OperationId);
    Assert.Equal(caseId, result.ConflictCaseId);
}
~~~

- [ ] **Step 2: Run the Application test and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ConflictContractTests
~~~

Expected: FAIL because Stage 4 contracts do not exist.

- [ ] **Step 3: Add immutable persistence and presentation records**

Define these shapes in `ConflictContracts.cs`; use `IReadOnlyList<T>` at every collection boundary:

~~~csharp
public sealed record ConflictInputSnapshot(
    string FullPath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    string TargetFilename,
    long MetadataVersion);

public sealed record ConflictMetadataSnapshot(
    string? DisplayName,
    IReadOnlyList<string> Authors,
    string? Series,
    IReadOnlyList<string> Tags);

public sealed record ConflictTechnicalMetadata(
    string Format,
    int? PixelWidth,
    int? PixelHeight,
    TimeSpan? Duration);

public sealed record RegisteredConflictCandidate(
    Guid AssetId,
    long AssetMetadataVersion,
    string RelativePath,
    string ActualFilename,
    string? DisplayName,
    MediaKind MediaKind,
    long StoredSize,
    string StoredSha256,
    ConflictReason DatabaseReason,
    ConflictMetadataSnapshot Metadata);

public sealed record ConflictMatchRecord(
    Guid Id,
    ConflictReason Reason,
    bool IsRegistered,
    Guid? AssetId,
    long? AssetMetadataVersion,
    string RelativePath,
    string ActualFilename,
    string? DisplayName,
    MediaKind MediaKind,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    ConflictMetadataSnapshot Metadata,
    ConflictTechnicalMetadata Technical);

public sealed record ConflictCaseRecord(
    Guid Id,
    Guid InboxItemId,
    ConflictCaseStatus Status,
    ConflictInputSnapshot Input,
    ConflictResolution? Resolution,
    Guid? SelectedMatchId,
    string? InvalidationReason,
    IReadOnlyList<ConflictMatchRecord> Matches);

public sealed record ConflictMediaDto(
    Guid Id,
    bool IsIncoming,
    bool IsRegistered,
    Guid? AssetId,
    string ActualFilename,
    string DisplayName,
    MediaKind MediaKind,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    ConflictReason Reason,
    ConflictMetadataSnapshot Metadata,
    ConflictTechnicalMetadata Technical,
    PreviewDescriptor? Preview);

public sealed record ConflictCaseDto(
    Guid Id,
    Guid InboxItemId,
    ConflictMediaDto Incoming,
    IReadOnlyList<ConflictMediaDto> Matches,
    ConflictResolution? PersistedResolution,
    Guid? PersistedSelectedMatchId,
    Guid? DefaultReplaceMatchId,
    bool CanReplace);
~~~

`ConflictCaseDto` never carries a writable absolute target path. A trusted preview descriptor is produced only by an application resolver after guarded path validation. An active `Chosen` case is queryable after restart; the dialog restores its persisted selection but still requires the user to confirm again before execution resumes.

Add these result and execution types in `IngestContracts.cs`:

~~~csharp
public sealed record ConflictRevalidationResult(
    bool IsValid,
    string? ErrorCode,
    IReadOnlyList<ConflictMatchRecord> CurrentMatches)
{
    public static ConflictRevalidationResult Valid(
        IReadOnlyList<ConflictMatchRecord> matches) => new(true, null, matches);

    public static ConflictRevalidationResult Invalid(string errorCode) =>
        new(false, errorCode, []);
}

public enum IngestDisposition
{
    Create,
    KeepBoth,
    ReplaceRegistered,
    ReplaceUnregistered
}

public sealed record IngestExecutionPlan
{
    private IngestExecutionPlan(
        Guid? conflictCaseId,
        IngestDisposition disposition,
        string requestedTargetFilename,
        Guid? replacementAssetId,
        long? replacementMetadataVersion,
        LibraryFileSnapshot? target)
    {
        ConflictCaseId = conflictCaseId;
        Disposition = disposition;
        RequestedTargetFilename = requestedTargetFilename;
        ReplacementAssetId = replacementAssetId;
        ReplacementMetadataVersion = replacementMetadataVersion;
        Target = target;
    }

    public Guid? ConflictCaseId { get; }
    public IngestDisposition Disposition { get; }
    public string RequestedTargetFilename { get; }
    public Guid? ReplacementAssetId { get; }
    public long? ReplacementMetadataVersion { get; }
    public LibraryFileSnapshot? Target { get; }

    public static IngestExecutionPlan Create(string requestedTargetFilename) =>
        new(null, IngestDisposition.Create, RequireFilename(requestedTargetFilename),
            null, null, null);

    public static IngestExecutionPlan KeepBoth(
        Guid conflictCaseId, string requestedTargetFilename) =>
        new(RequireId(conflictCaseId), IngestDisposition.KeepBoth,
            RequireFilename(requestedTargetFilename), null, null, null);

    public static IngestExecutionPlan ReplaceRegistered(
        Guid conflictCaseId,
        Guid replacementAssetId,
        long replacementMetadataVersion,
        LibraryFileSnapshot target) =>
        replacementMetadataVersion < 0
            ? throw new ArgumentOutOfRangeException(nameof(replacementMetadataVersion))
            : new(
                RequireId(conflictCaseId),
                IngestDisposition.ReplaceRegistered,
                RequireTarget(target).RelativePath,
                RequireId(replacementAssetId),
                replacementMetadataVersion,
                target);

    public static IngestExecutionPlan ReplaceUnregistered(
        Guid conflictCaseId, LibraryFileSnapshot target) =>
        new(
            RequireId(conflictCaseId),
            IngestDisposition.ReplaceUnregistered,
            RequireTarget(target).RelativePath,
            null,
            null,
            target);

    private static Guid RequireId(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("A persisted id is required.")
        : value;

    private static LibraryFileSnapshot RequireTarget(LibraryFileSnapshot? target) =>
        target ?? throw new ArgumentException("A replacement target is required.");

    private static string RequireFilename(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || !StringComparer.Ordinal.Equals(Path.GetFileName(value), value)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A safe target filename is required.");
        }

        return value;
    }
}

public interface IIngestExecutionService
{
    Task<IngestResult> ExecuteAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}
~~~

Factories reject blank or unsafe filenames, missing case IDs, empty replacement asset IDs, negative metadata versions, or null target snapshots. Registered replacement requires an asset ID and version; unmanaged replacement forbids both.

- [ ] **Step 4: Add store and service interfaces**

~~~csharp
public interface IConflictCaseStore
{
    Task<IReadOnlyList<RegisteredConflictCandidate>> FindRegisteredCandidatesAsync(
        string targetFilename, string sha256, CancellationToken cancellationToken);
    Task<ConflictCaseRecord> ReplaceOpenAsync(
        Guid inboxItemId,
        ConflictInputSnapshot input,
        IReadOnlyList<ConflictMatchRecord> matches,
        CancellationToken cancellationToken);
    Task<ConflictCaseRecord?> LoadAsync(Guid conflictCaseId, CancellationToken cancellationToken);
    Task<ConflictCaseRecord?> LoadOpenForInboxAsync(Guid inboxItemId, CancellationToken cancellationToken);
    Task ChooseAsync(Guid conflictCaseId, ConflictResolution resolution, Guid? matchId, CancellationToken cancellationToken);
    Task SkipAsync(Guid conflictCaseId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid conflictCaseId, string reason, CancellationToken cancellationToken);
}

public interface IConflictDetectionService
{
    Task<ConflictCaseRecord?> DetectAsync(
        IngestPreparation preparation, LibraryFolders folders, CancellationToken cancellationToken);
    Task<ConflictRevalidationResult> RevalidateAsync(
        ConflictCaseRecord conflict,
        IngestPreparation current,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}

public interface IConflictQueryService
{
    Task<ConflictCaseDto?> GetOpenAsync(Guid inboxItemId, CancellationToken cancellationToken);
}

public interface IConflictResolutionService
{
    Task<IngestResult> ResolveAsync(
        Guid conflictCaseId,
        ConflictResolution resolution,
        Guid? selectedMatchId,
        CancellationToken cancellationToken);
}
~~~

Add `IngestOutcome.Skipped` and `ConflictChanged`. Change `IngestResult` to `(Outcome, OperationId, AssetId, ConflictCaseId, Message)` and update every existing construction site and test fake in the same task.

- [ ] **Step 5: Add guarded storage value records**

~~~csharp
public sealed record LibraryFileSnapshot(
    string FullPath,
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed record ReplacementBackup(
    string FullPath,
    string RelativePath,
    long Size,
    string Sha256);
~~~

This task adds the immutable value records because `IngestExecutionPlan` consumes them. Do not add unimplemented interface members or production stubs here; Task 5 adds each method together with its red/green storage test and implementation.

- [ ] **Step 6: Make the contract tests pass and commit**

Update the existing `IngestResult` construction sites with a null conflict-case ID. Do not alter `IngestOperationRecord` in this task; Task 3 changes the application record and persistence row atomically.

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ConflictContractTests
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/tests/TraceFold.Media.Application.Tests apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs
git commit -m "feat(media-desktop): define conflict contracts"
~~~

Expected: contract tests and the full Release build pass. No production stub or throwing interface member is introduced.

---

### Task 3: Add the Stage 4 SQLite Schema and Duplicate-Hash Migration

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/ConflictCaseRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/ConflictMatchRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/IngestOperationRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/IngestCleanupEntryRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260823230000_Stage4ConflictRecovery.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260823230000_Stage4ConflictRecovery.Designer.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/MediaDesktopDbContextModelSnapshot.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestRecoveryServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/Stage4MigrationTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`

**Interfaces:**
- Produces tables `conflict_cases` and `conflict_matches`.
- Changes `media_assets.sha256` from unique to non-unique indexed lookup.
- Persists operation disposition, final asset ID, replacement target identity, backup identity, recovery origin, and cleanup schedule.
- Persists per-cleanup-entry attempt/error timestamps without changing existing ordered ownership.

- [ ] **Step 1: Write failing migration tests**

Create a Stage 3 database with two different assets prepared for migration, apply all migrations, then assert:

~~~csharp
[Fact]
public async Task Stage4_allows_duplicate_hashes_but_keeps_paths_unique()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync("20260823120000_Stage3ProcessingPipeline");
    await database.MigrateAsync();

    await InsertAssetAsync(database, "one.jpg", SharedHash);
    await InsertAssetAsync(database, "two.jpg", SharedHash);

    Assert.Equal(2, await database.ScalarAsync<long>(
        $"SELECT COUNT(*) FROM media_assets WHERE sha256 = '{SharedHash}'"));
    await Assert.ThrowsAsync<SqliteException>(() =>
        InsertAssetAsync(database, "one.jpg", new string('B', 64)));
}

[Fact]
public async Task Stage4_adds_conflict_backup_and_retry_columns()
{
    IReadOnlySet<string> tables = await database.ReadTableNamesAsync();
    Assert.Contains("conflict_cases", tables);
    Assert.Contains("conflict_matches", tables);
    Assert.Contains("backup_state", await database.ReadColumnNamesAsync("ingest_operations"));
    Assert.Contains("next_cleanup_attempt_at_utc", await database.ReadColumnNamesAsync("ingest_operations"));
}
~~~

Add a migration-from-Stage-3 assertion that all pre-existing asset IDs, paths, display names, authors, series, tags, processing rows, and ingest cleanup entries are unchanged.

Add generic test helpers `ScalarAsync<T>(string sql)` and `ReadColumnNamesAsync(string table)` to `TestMediaDatabase`; the latter permits only a hard-coded test table name before executing `PRAGMA table_info`. Change `ReadSingleIngestAsync` to join `media_assets a ON a.id = o.asset_id`, never by SHA, so the shared test support remains deterministic after duplicates are legal.

- [ ] **Step 2: Run the migration test and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Stage4MigrationTests
~~~

Expected: FAIL because the Stage 4 migration and tables do not exist and duplicate hashes still violate the unique index.

- [ ] **Step 3: Add row shapes**

`ConflictCaseRow` stores:

~~~csharp
internal sealed class ConflictCaseRow
{
    public Guid Id { get; set; }
    public Guid InboxItemId { get; set; }
    public InboxItemRow InboxItem { get; set; } = null!;
    public Guid? ActiveInboxItemId { get; set; }
    public ConflictCaseStatus Status { get; set; }
    public required string InputPath { get; set; }
    public long InputSize { get; set; }
    public DateTimeOffset InputLastWriteTimeUtc { get; set; }
    public required string InputSha256 { get; set; }
    public required string TargetFilename { get; set; }
    public long MetadataVersion { get; set; }
    public ConflictResolution? Resolution { get; set; }
    public Guid? SelectedMatchId { get; set; }
    public Guid? OperationId { get; set; }
    public string? InvalidationReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public ICollection<ConflictMatchRow> Matches { get; } = [];
}
~~~

`ConflictMatchRow` stores reason flags as an integer, registered/asset identity, asset metadata version, relative path, filename, display name, media kind, size, last-write time, SHA-256, `MetadataSnapshotJson`, and `TechnicalSnapshotJson`.

Extend `IngestOperationRow` with nullable `ConflictCaseId`, required `Disposition`, nullable `AssetId`, `ReplacementAssetId`, `ReplacementAssetMetadataVersion`, `ExpectedTargetRelativePath`, `ExpectedTargetSize`, `ExpectedTargetLastWriteTimeUtc`, `ExpectedTargetSha256`, required `BackupState`, nullable backup path/size/hash, nullable `RecoveryBlockedFromStage`, `OldAssetSnapshotJson`, integer `CleanupAttemptCount`, and nullable `NextCleanupAttemptAtUtc`.

Extend `IngestCleanupEntryRow` with nullable `LastAttemptAtUtc` and `LastErrorCode`.

Migration defaults for existing Stage 3 operations are exact: `disposition = 'create'`, `backup_state = 'notrequired'`, and `cleanup_attempt_count = 0`. Existing completed/failed rows receive no active conflict or backup identity.

Replace the Application record and update `EfIngestStore.ToRecord` plus existing unit-test constructors in the same step:

~~~csharp
public sealed record IngestOperationRecord(
    Guid Id,
    Guid InboxItemId,
    IngestOperationStage Stage,
    IngestDisposition Disposition,
    Guid? ConflictCaseId,
    string SourcePath,
    long SourceSize,
    DateTimeOffset SourceLastWriteTimeUtc,
    string SourceSha256,
    string TargetFilename,
    string? StagedPath,
    Guid? AssetId,
    Guid? ReplacementAssetId,
    long? ReplacementAssetMetadataVersion,
    string? ExpectedTargetRelativePath,
    long? ExpectedTargetSize,
    DateTimeOffset? ExpectedTargetLastWriteTimeUtc,
    string? ExpectedTargetSha256,
    IngestBackupState BackupState,
    string? BackupRelativePath,
    long? BackupSize,
    string? BackupSha256,
    IngestOperationStage? RecoveryBlockedFromStage,
    string? ErrorCode,
    int CleanupAttemptCount,
    DateTimeOffset? NextCleanupAttemptAtUtc);
~~~

Map `AssetId` directly from the new operation column. Remove the old `ToRecordAsync` SHA lookup during this task; it is invalid once duplicate hashes are legal.

- [ ] **Step 4: Configure keys, conversions, and indexes**

Add these exact relational invariants:

~~~csharp
row.HasIndex(item => item.ActiveInboxItemId).IsUnique();
row.HasIndex(item => item.InputSha256);
row.HasIndex(item => item.OperationId).IsUnique();
row.HasMany(item => item.Matches)
   .WithOne(item => item.ConflictCase)
   .HasForeignKey(item => item.ConflictCaseId)
   .OnDelete(DeleteBehavior.Cascade);

mediaAsset.HasIndex(item => item.NormalizedRelativePath).IsUnique();
mediaAsset.HasIndex(item => item.Sha256).IsUnique(false);

cleanup.HasIndex(item => new { item.OperationId, item.Ordinal }).IsUnique();
operation.HasIndex(item => item.ActiveInboxItemId).IsUnique();
operation.HasIndex(item => item.ConflictCaseId).IsUnique();
operation.HasIndex(item => item.NextCleanupAttemptAtUtc);
operation.HasOne<MediaAssetRow>()
    .WithMany()
    .HasForeignKey(item => item.AssetId)
    .OnDelete(DeleteBehavior.Restrict);
operation.HasOne<MediaAssetRow>()
    .WithMany()
    .HasForeignKey(item => item.ReplacementAssetId)
    .OnDelete(DeleteBehavior.Restrict);
~~~

Store enums as lowercase strings except the flags value, which is stored as an integer. Apply the existing `UseSnakeCaseColumns` helper to both new rows.

- [ ] **Step 5: Create and inspect the migration**

The migration must drop the unique SHA index and recreate it non-unique without rebuilding or deleting `media_assets` rows:

~~~csharp
migrationBuilder.DropIndex(
    name: "IX_media_assets_sha256",
    table: "media_assets");
migrationBuilder.CreateIndex(
    name: "IX_media_assets_sha256",
    table: "media_assets",
    column: "sha256");
~~~

The `Down` migration may recreate uniqueness only after deleting Stage 4 duplicate rows is explicitly rejected; therefore document that downgrade fails if duplicates exist rather than silently deleting assets.

- [ ] **Step 6: Run migration and persistence regression tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestServiceTests|FullyQualifiedName~IngestRecoveryServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Stage2MigrationTests|FullyQualifiedName~Stage3MigrationTests|FullyQualifiedName~Stage4MigrationTests|FullyQualifiedName~IngestPersistenceTests|FullyQualifiedName~MetadataPersistenceTests"
~~~

Expected: all selected migration and persistence tests pass.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs
git commit -m "feat(media-desktop): migrate conflict recovery state"
~~~

---

### Task 4: Persist Conflict Cases and All Registered Matches

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfConflictCaseStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/ConflictPersistenceTests.cs`

**Interfaces:**
- Implements: every method of `IConflictCaseStore` from Task 2.
- Consumes: Stage 4 tables and the non-unique SHA index from Task 3.
- Produces: complete registered candidates including ordered author/tag names and zero-or-one series; Application combines them with current guarded file identities.
- Guarantees: one active case per inbox item and transactional item-state changes.

- [ ] **Step 1: Write failing persistence tests**

Add these scenarios with real SQLite:

~~~csharp
[Fact]
public async Task FindRegisteredCandidates_returns_same_name_first_and_every_hash_candidate()
{
    await InsertAssetAsync("same.jpg", HashA, display: "same");
    await InsertAssetAsync("copy-1.jpg", HashA, display: "copy 1");
    await InsertAssetAsync("copy-2.jpg", HashA, display: "copy 2");

    IReadOnlyList<RegisteredConflictCandidate> matches = await store
        .FindRegisteredCandidatesAsync("same.jpg", HashA, Token);

    Assert.Equal(3, matches.Count);
    Assert.True(matches[0].DatabaseReason.HasFlag(ConflictReason.Name));
    Assert.All(matches, item =>
        Assert.True(item.DatabaseReason.HasFlag(ConflictReason.Content)));
}

[Fact]
public async Task ReplaceOpen_invalidates_old_case_and_keeps_one_active_case()
{
    ConflictCaseRecord first = await SaveCaseAsync(HashA);
    ConflictCaseRecord second = await SaveCaseAsync(HashB);

    Assert.NotEqual(first.Id, second.Id);
    Assert.Equal(ConflictCaseStatus.Invalidated, (await store.LoadAsync(first.Id, Token))!.Status);
    Assert.Equal(second.Id, (await store.LoadOpenForInboxAsync(ItemId, Token))!.Id);
}

[Fact]
public async Task Skip_returns_item_to_ready_and_preserves_sources()
{
    ConflictCaseRecord conflict = await SaveCaseAsync(HashA);
    await store.SkipAsync(conflict.Id, Token);

    Assert.Equal(InboxItemState.ReadyToTag, await ReadInboxStateAsync());
    Assert.NotEmpty(await ReadSourceRowsAsync());
}
~~~

Also test chosen single target persistence, invalidation reason, unknown case/match rejection, and concurrent `ReplaceOpenAsync` calls producing one active row. Resolution-to-operation atomicity is tested with asset commit in Task 7.

- [ ] **Step 2: Run the persistence tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ConflictPersistenceTests
~~~

Expected: FAIL because `EfConflictCaseStore` is missing.

- [ ] **Step 3: Implement registered match projection**

Query name and hash in one database context, include authors/tags and taxonomy terms, union by asset ID, and order same-name first:

~~~csharp
MediaAssetRow[] assets = await context.MediaAssets
    .AsNoTracking()
    .Include(asset => asset.Authors).ThenInclude(link => link.Author)
    .Include(asset => asset.Tags).ThenInclude(link => link.Tag)
    .Include(asset => asset.Series)
    .Where(asset => asset.NormalizedRelativePath == normalizedTarget
                 || asset.Sha256 == normalizedSha)
    .ToArrayAsync(cancellationToken);

return assets
    .Select(asset => ToCandidate(asset, normalizedTarget, normalizedSha))
    .OrderByDescending(match => match.DatabaseReason.HasFlag(ConflictReason.Name))
    .ThenBy(match => match.ActualFilename, StringComparer.OrdinalIgnoreCase)
    .ToArray();
~~~

Never use `SingleAsync` for SHA queries after the migration. The candidate contains database size/hash but no invented file timestamp; Task 6 calls guarded storage inspection for each candidate and discards a content-only candidate whose current formal file is missing or no longer has the source hash.

- [ ] **Step 4: Implement case state transactions**

`ReplaceOpenAsync` must, in one transaction:

1. mark the previous active case `Invalidated` and clear `ActiveInboxItemId`;
2. insert the new case and all immutable match rows;
3. set the inbox item to `ConflictBlocked` with `ingest.conflict`;
4. save and commit.

`LoadOpenForInboxAsync` returns the one active `Open` or unattached `Chosen` case. `ChooseAsync` rehydrates the Domain aggregate, calls `Choose`, and writes its result. `SkipAsync` and `InvalidateAsync` follow the same aggregate-first pattern and return the item to `ReadyToTag`. Task 7 resolves a chosen case atomically inside the linked asset-commit transaction.

Inject the already registered `TimeProvider` into `EfConflictCaseStore` and use `GetUtcNow()` for every case timestamp so concurrency and persistence tests can use a fixed clock.

- [ ] **Step 5: Register and verify**

~~~csharp
services.AddSingleton<IConflictCaseStore, EfConflictCaseStore>();
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictPersistenceTests|FullyQualifiedName~IngestPersistenceTests"
git add apps/media-desktop/src/TraceFold.Media.Infrastructure apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests
git commit -m "feat(media-desktop): persist conflict cases"
~~~

Expected: conflict and existing ingest persistence tests pass.

---

### Task 5: Add Guarded Inspection, Unique Commit, Backup, Replace, and Restore

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Storage/FileStorageContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledMediaStorage.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledMediaStorageTests.cs`

**Interfaces:**
- Adds and implements: guarded inspection, unique commit, backup, replacement, restore, and backup-deletion methods on `IControlledMediaStorage`.
- Produces internal per-operation paths below `<library>/.tracefold/commit/<operation-id>` and `<library>/.tracefold/recovery/<operation-id>`.
- Guarantees: final rename and old-target backup move occur on the formal-library volume; no internal path participates in media matching.

- [ ] **Step 1: Write failing inspection and unique-name tests**

~~~csharp
[Fact]
public async Task InspectTarget_returns_verified_identity_without_mutating_file()
{
    string target = folders.WriteLibrary("photo.jpg", [1, 2, 3]);

    LibraryFileSnapshot? snapshot = await storage.InspectTargetAsync(
        folders.Value, "photo.jpg", Token);

    Assert.NotNull(snapshot);
    Assert.Equal("photo.jpg", snapshot.RelativePath);
    Assert.Equal(await HashAsync(target), snapshot.Sha256);
    Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(target, Token));
}

[Fact]
public async Task CommitUnique_advances_suffix_when_an_external_file_wins()
{
    folders.WriteLibrary("photo.jpg", [9]);
    folders.WriteLibrary("photo (2).jpg", [8]);
    StagedFile staged = await StageAsync("photo.jpg", [1, 2, 3]);

    CommittedFile result = await storage.CommitUniqueAsync(
        OperationId, folders.Value, staged, "photo.jpg", Token);

    Assert.Equal("photo (3).jpg", result.RelativePath);
    Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(result.FullPath, Token));
}
~~~

Add tests for extensionless names, names already ending in `(2)`, unsafe names, reparse-point targets, and a race fake that creates the predicted target immediately before the first move.

- [ ] **Step 2: Write failing replacement safety tests**

~~~csharp
[Fact]
public async Task Replacement_keeps_verified_backup_until_explicit_cleanup()
{
    string target = folders.WriteLibrary("photo.jpg", [7, 8]);
    LibraryFileSnapshot expected = (await storage.InspectTargetAsync(
        folders.Value, "photo.jpg", Token))!;
    StagedFile staged = await StageAsync("photo.jpg", [1, 2, 3]);

    ReplacementBackup backup = await storage.BackupTargetAsync(
        OperationId, folders.Value, expected, Token);
    CommittedFile committed = await storage.CommitReplacementAsync(
        OperationId, folders.Value, staged, expected, Token);

    Assert.True(File.Exists(backup.FullPath));
    Assert.Equal([7, 8], await File.ReadAllBytesAsync(backup.FullPath, Token));
    Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(committed.FullPath, Token));
}

[Fact]
public async Task Restore_refuses_when_target_contains_unknown_content()
{
    ReplacementFixture replacement = await ReplacedAsync();
    await File.WriteAllBytesAsync(replacement.Target.FullPath, [6, 6], Token);

    await Assert.ThrowsAsync<StorageIntegrityException>(() =>
        storage.RestoreBackupAsync(
            OperationId, folders.Value, replacement.Target,
            replacement.Backup, Token));
}
~~~

Also assert backup rejects a changed size/hash, repeated backup is idempotent only for the exact persisted backup, repeated replacement recognizes the expected new target, and no sibling operation directory is touched.

- [ ] **Step 3: Run storage tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ControlledMediaStorageTests
~~~

Expected: FAIL because Stage 4 storage methods are missing.

- [ ] **Step 4: Implement guarded internal paths and inspection**

First add these exact interface members, then implement them in `ControlledMediaStorage` in the same red/green step:

~~~csharp
Task<LibraryFileSnapshot?> InspectTargetAsync(
    LibraryFolders folders, string targetFilename, CancellationToken cancellationToken);
Task<CommittedFile> CommitUniqueAsync(
    Guid operationId, LibraryFolders folders, StagedFile staged,
    string requestedFilename, CancellationToken cancellationToken);
Task<ReplacementBackup> BackupTargetAsync(
    Guid operationId, LibraryFolders folders, LibraryFileSnapshot expected,
    CancellationToken cancellationToken);
Task<CommittedFile> CommitReplacementAsync(
    Guid operationId, LibraryFolders folders, StagedFile staged,
    LibraryFileSnapshot expectedTarget, CancellationToken cancellationToken);
Task RestoreBackupAsync(
    Guid operationId, LibraryFolders folders, LibraryFileSnapshot target,
    ReplacementBackup backup, CancellationToken cancellationToken);
Task<SourceDeleteResult> DeleteBackupIfMatchesAsync(
    LibraryFolders folders, ReplacementBackup backup,
    CancellationToken cancellationToken);
~~~

Resolve internal paths only through `ControlledPathGuard.RequireDescendant` and reject every existing candidate with `ControlledReparseGuard`. The target remains a direct child of the library. `InspectTargetAsync` returns null for no file, rejects a directory/reparse point, then records size, UTC last-write time, and SHA-256.

Use this suffix split rule:

~~~csharp
string extension = Path.GetExtension(requestedFilename);
string stem = requestedFilename[..^extension.Length];
for (int suffix = 2; ; suffix++)
{
    string candidate = $"{stem} ({suffix}){extension}";
    try { return await CommitCandidateAsync(candidate); }
    catch (IOException) when (File.Exists(ResolveTarget(folders, candidate))) { }
}
~~~

Create one verified commit payload below `.tracefold/commit/<operation-id>` and reuse it after a losing move; never recopy or delete an external winner.

- [ ] **Step 5: Implement backup, replacement, restore, and deletion**

`BackupTargetAsync` re-inspects target size/hash/last-write, creates only the operation recovery directory, atomically moves the target to `original<extension>`, verifies backup, and returns its relative identity. If the exact backup already exists and target is absent, verify and return it; any other combination throws `StorageIntegrityException`.

`CommitReplacementAsync` requires a verified backup and an absent target, then moves the verified commit payload to the original target filename. `RestoreBackupAsync` permits only an absent target or the persisted new hash; it never overwrites unknown content. `DeleteBackupIfMatchesAsync` quarantines, re-verifies, deletes, and removes only empty operation directories.

- [ ] **Step 6: Run storage regression and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ControlledMediaStorageTests|FullyQualifiedName~ControlledPathGuardTests|FullyQualifiedName~ControlledProcessingWorkspaceTests"
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledMediaStorage.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledMediaStorageTests.cs
git commit -m "feat(media-desktop): add guarded replacement storage"
~~~

---

### Task 6: Detect, Project, and Revalidate Conflict Cases

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/ConflictDetectionService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/ConflictQueryService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/ConflictDetectionServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/ConflictQueryServiceTests.cs`

**Interfaces:**
- Implements: `IConflictDetectionService` and `IConflictQueryService`.
- Consumes: registered matches from `IConflictCaseStore`, unmanaged direct-child identity from `IControlledMediaStorage`, current folders, and controlled preview resolution.
- Produces: one persisted, de-duplicated case and a DTO with same-name default target.

- [ ] **Step 1: Write failing detection tests**

~~~csharp
[Fact]
public async Task Detector_combines_registered_name_and_all_content_matches()
{
    catalog.Registered =
    [
        Registered("same.jpg", HashA, ConflictReason.Name | ConflictReason.Content),
        Registered("copy.jpg", HashA, ConflictReason.Content)
    ];
    storage.Target = File("same.jpg", HashA);

    ConflictCaseRecord? result = await service.DetectAsync(
        Preparation("same.jpg", HashA), Folders, Token);

    Assert.NotNull(result);
    Assert.Equal(2, result.Matches.Count);
    Assert.Equal("same.jpg", result.Matches[0].ActualFilename);
}

[Fact]
public async Task Detector_adds_unmanaged_same_name_match()
{
    catalog.Registered = [];
    storage.Target = File("same.jpg", HashB);

    ConflictCaseRecord? result = await service.DetectAsync(
        Preparation("same.jpg", HashA), Folders, Token);

    ConflictMatchRecord match = Assert.Single(result!.Matches);
    Assert.False(match.IsRegistered);
    Assert.Equal(ConflictReason.Name, match.Reason);
}
~~~

Also test no conflict returns null without a case, registered same-name is not duplicated as unmanaged, and multiple duplicate hashes remain separate matches.

- [ ] **Step 2: Write failing revalidation and query tests**

~~~csharp
[Theory]
[InlineData("source_hash_changed")]
[InlineData("target_hash_changed")]
[InlineData("target_registration_changed")]
[InlineData("asset_version_changed")]
public async Task Revalidation_invalidates_any_identity_change(string change)
{
    ConflictCaseRecord conflict = ExistingCase();
    ApplyChange(change);

    ConflictRevalidationResult result = await service.RevalidateAsync(
        conflict, CurrentPreparation(), Folders, Token);

    Assert.False(result.IsValid);
    Assert.Equal($"conflict.{change}", result.ErrorCode);
}

[Fact]
public async Task Query_defaults_same_name_but_requires_selection_for_content_only()
{
    ConflictCaseDto named = await query.GetOpenAsync(NamedItemId, Token);
    ConflictCaseDto contentOnly = await query.GetOpenAsync(ContentItemId, Token);

    Assert.NotNull(named.DefaultReplaceMatchId);
    Assert.Null(contentOnly.DefaultReplaceMatchId);
    Assert.True(contentOnly.CanReplace);
}
~~~

Add a query test that an unattached `Chosen` case survives a host rebuild and returns `PersistedResolution` plus `PersistedSelectedMatchId`. Metadata version changes on the inbox item do not invalidate a file conflict; resolution later freezes the latest saved metadata. Technical metadata always includes the lowercase extension without the dot as `Format`; use the existing package-local media probe to fill video duration when available, and keep width/height or duration null when the current probe cannot prove them. Probe failure must not suppress conflict detection.

- [ ] **Step 3: Run Application tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictDetectionServiceTests|FullyQualifiedName~ConflictQueryServiceTests"
~~~

Expected: FAIL because detection and query services are missing.

- [ ] **Step 4: Implement detection and de-duplication**

Build a dictionary keyed by registered asset ID; for an unmanaged target use `unmanaged:<normalized-relative-path>`. Merge reason flags rather than duplicating rows:

~~~csharp
foreach (RegisteredConflictCandidate candidate in registeredCandidates)
{
    LibraryFileSnapshot? file = await storage.InspectTargetAsync(
        folders, candidate.ActualFilename, cancellationToken);
    ConflictMatchRecord? match = ToVerifiedMatch(candidate, file, preparation.SourceSha256);
    if (match is not null)
    {
        matches[$"asset:{candidate.AssetId:D}"] = match;
    }
}

if (target is not null && !matches.Values.Any(item =>
        item.AssetId.HasValue &&
        StringComparer.OrdinalIgnoreCase.Equals(item.RelativePath, target.RelativePath)))
{
    matches[$"unmanaged:{target.RelativePath.ToUpperInvariant()}"] =
        UnmanagedMatch(target, preparation.MediaKind);
}
~~~

Before building that dictionary, turn every `RegisteredConflictCandidate` into a final match by calling `storage.InspectTargetAsync(folders, candidate.ActualFilename, ...)`. Recompute `Content` from the current on-disk SHA rather than trusting `StoredSha256`. Exclude a missing/changed content-only candidate; retain a same-name candidate using its current verified on-disk identity and only the reasons that remain true. This prevents a stale database hash from authorizing replacement of a different file.

Persist only after the complete list is known. Case input uses the effective processed output when present because `IngestPreparation.From(candidate, input)` already resolves that boundary.

- [ ] **Step 5: Implement exact revalidation**

Re-resolve the input, re-run detection without saving a new case, and compare:

- input full path, size, last-write time, and SHA-256;
- every registered asset ID and metadata version;
- every target relative path, size, last-write time, SHA-256, and registration state;
- the selected match still exists.

Return `ConflictRevalidationResult.Valid(currentMatches)` only when all identities agree. The caller persists invalidation and performs the new scan; the detector itself does not overwrite the old case during comparison.

- [ ] **Step 6: Implement DTO projection and preview fallback**

Resolve incoming preview by `MetadataOwner(InboxItem, id)`, registered target preview by `MetadataOwner(MediaAsset, assetId)`, and unmanaged preview by re-running `storage.InspectTargetAsync` for the persisted direct-child filename and constructing a descriptor only when size/hash still match. Catch `PreviewNotRegisteredException` and media decode errors and set `Preview = null`; do not fail the case. Project the persisted technical snapshot so the dialog can show format and every duration/dimension value that was actually proven.

- [ ] **Step 7: Register, verify, and commit**

~~~csharp
services.AddSingleton<IConflictDetectionService, ConflictDetectionService>();
services.AddSingleton<IConflictQueryService, ConflictQueryService>();
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictDetectionServiceTests|FullyQualifiedName~ConflictQueryServiceTests"
git add apps/media-desktop/src/TraceFold.Media.Application apps/media-desktop/tests/TraceFold.Media.Application.Tests
git commit -m "feat(media-desktop): detect and revalidate conflicts"
~~~

---

### Task 7: Execute Skip, Keep Both, and Single-Target Replace Through One Pipeline

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestExecutionService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/ConflictResolutionService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestCleanupService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/IngestOperationRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/ConflictResolutionServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestExecutionServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/IngestPersistenceTests.cs`

**Interfaces:**
- Implements: `IConflictResolutionService` and `IIngestExecutionService`.
- Changes: `IIngestStore.CreateAsync(IngestPreparation, IngestExecutionPlan, ...)`.
- Adds: `MarkBackupCreatedAsync`, `MarkBackupRestoredAsync`, and idempotent operation `AssetId` persistence.
- Removes: legacy single-value `ConflictKind`, `ConflictCheck`, `IIngestStore.CheckConflictsAsync`, and `BlockConflictAsync` after all callers move to `IConflictDetectionService`/`IConflictCaseStore`.
- Changes: `CommitAssetAsync` creates or updates based on persisted disposition, never by unique SHA lookup.
- Guarantees: one singleton execution gate protects final target selection and commit within the process.

- [ ] **Step 1: Write failing first-ingest and Skip tests**

~~~csharp
[Fact]
public async Task Initial_ingest_returns_persisted_case_before_staging()
{
    fixture.Detector.Result = fixture.Conflict;

    IngestResult result = await fixture.Ingest.IngestAsync(fixture.ItemId, Token);

    Assert.Equal(IngestOutcome.ConflictBlocked, result.Outcome);
    Assert.Equal(fixture.Conflict.Id, result.ConflictCaseId);
    Assert.Null(result.OperationId);
    Assert.Equal(0, fixture.Storage.StageCallCount);
}

[Fact]
public async Task Skip_closes_case_and_keeps_every_input_file()
{
    IngestResult result = await fixture.Resolver.ResolveAsync(
        fixture.CaseId, ConflictResolution.Skip, null, Token);

    Assert.Equal(IngestOutcome.Skipped, result.Outcome);
    Assert.Equal(InboxItemState.ReadyToTag, fixture.Store.ItemState);
    Assert.All(fixture.InputPaths, path => Assert.True(File.Exists(path)));
}
~~~

- [ ] **Step 2: Write failing Keep Both and Replace tests**

~~~csharp
[Fact]
public async Task Keep_both_uses_storage_selected_filename_and_keeps_display_name()
{
    fixture.Storage.UniqueResult = Committed("photo (3).jpg", HashA);

    IngestResult result = await fixture.Resolver.ResolveAsync(
        fixture.CaseId, ConflictResolution.KeepBoth, null, Token);

    Assert.Equal(IngestOutcome.Completed, result.Outcome);
    Assert.Equal("photo (3).jpg", fixture.Store.CommittedActualFilename);
    Assert.Equal("My display", fixture.Store.CommittedDisplayName);
}

[Fact]
public async Task Replace_registered_preserves_id_and_physical_name_but_replaces_metadata()
{
    Guid existingId = fixture.RegisteredMatch.AssetId!.Value;

    IngestResult result = await fixture.Resolver.ResolveAsync(
        fixture.CaseId, ConflictResolution.Replace,
        fixture.RegisteredMatch.Id, Token);

    Assert.Equal(existingId, result.AssetId);
    Assert.Equal(fixture.RegisteredMatch.ActualFilename, fixture.Store.CommittedActualFilename);
    Assert.Equal(fixture.LatestMetadata, fixture.Store.CommittedMetadata);
    Assert.True(fixture.Storage.BackupWasVerifiedBeforeReplacement);
}
~~~

Add tests for unmanaged replacement creating a new ID, content-only replacement requiring a selected match, selecting only one of multiple duplicate hashes, changed source/target returning `ConflictChanged`, ordinary no-conflict regression, and processed-input cleanup manifest preservation.

- [ ] **Step 3: Run Application tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestServiceTests|FullyQualifiedName~ConflictResolutionServiceTests|FullyQualifiedName~IngestExecutionServiceTests"
~~~

Expected: FAIL because the executor and resolver do not exist and `IngestService` still blocks with only `ConflictKind`.

- [ ] **Step 4: Extract the existing execution pipeline**

Move stage/file/database/verify/cleanup orchestration from `IngestService` into one singleton `IngestExecutionService`. Use this entry point:

~~~csharp
public interface IIngestExecutionService
{
    Task<IngestResult> ExecuteAsync(
        IngestPreparation preparation,
        IngestExecutionPlan plan,
        LibraryFolders folders,
        CancellationToken cancellationToken);
}
~~~

`IngestService` retains pre-ingest scan, candidate validation, folder lookup, input resolution, and initial conflict detection. With no conflict it calls `ExecuteAsync(..., IngestExecutionPlan.Create(...))`; with a conflict it returns the case ID. Preserve the `IMediaOperationCoordinator` lease and move the `SemaphoreSlim` commit gate into the singleton executor.

After the new path is green, delete the legacy one-conflict enum/check/store methods and their fake implementations. Do not keep both conflict APIs behind compatibility wrappers; the persisted match set is the only source of truth.

- [ ] **Step 5: Implement resolution orchestration**

Use this order:

~~~csharp
ConflictCaseRecord conflict = await RequireActiveAsync(caseId);
if (resolution == ConflictResolution.Skip)
{
    await store.SkipAsync(caseId, cancellationToken);
    activity.Publish(InboxActivityKind.ItemChanged);
    return Result(IngestOutcome.Skipped, caseId: caseId);
}

IngestCandidate candidate = await RequireCandidateAsync(conflict.InboxItemId);
PreparedIngestInput input = await inputResolver.ResolveAsync(candidate, cancellationToken);
IngestPreparation current = IngestPreparation.From(candidate, input);
ConflictRevalidationResult validation = await detector.RevalidateAsync(
    conflict, current, folders, cancellationToken);
if (!validation.IsValid)
{
    await store.InvalidateAsync(caseId, validation.ErrorCode!, cancellationToken);
    ConflictCaseRecord? replacement = await detector.DetectAsync(current, folders, cancellationToken);
    return Result(
        replacement is null ? IngestOutcome.ConflictChanged : IngestOutcome.ConflictBlocked,
        caseId: replacement?.Id);
}

await store.ChooseAsync(caseId, resolution, selectedMatchId, cancellationToken);
return await executor.ExecuteAsync(
    current,
    await BuildPlanAsync(
        conflict, resolution, selectedMatchId, folders, cancellationToken),
    folders,
    cancellationToken);
~~~

Build the plan from the persisted selected match, not from a Desktop-supplied path or asset ID. For Replace, call `storage.InspectTargetAsync(folders, selected.ActualFilename, ...)` again and require the returned relative path, size, last-write time, and SHA-256 to equal the revalidated match before constructing the plan. Freeze the latest candidate metadata only after revalidation.

- [ ] **Step 6: Implement four execution dispositions**

- `Create`: current `StageAsync → CommitAsync` path.
- `KeepBoth`: `StageAsync → CommitUniqueAsync`; persist the returned filename before asset commit.
- `ReplaceRegistered`: stage, backup exact selected target, persist backup evidence and old asset snapshot, commit replacement, update the selected asset.
- `ReplaceUnregistered`: stage, backup exact selected target, persist backup evidence, commit replacement, create one new asset.

`EfIngestStore.CreateAsync` accepts `ReadyToTag` only for `Create`. A plan linked to a chosen conflict case requires the matching `ConflictBlocked` inbox item and active case; it transitions the item through `BeginResolvedIngest()` and records the case ID on the operation. Any case/item/plan mismatch fails before staging.

If replacement file commit throws after backup, synchronously attempt `RestoreBackupAsync`. Persist `Restored` on success. If restore cannot be proven safe, persist `RecoveryBlocked`; never mark the operation as an ordinary retryable failure that could start a second operation.

If the replacement file is committed but the database transaction throws, first confirm the database still has the expected old asset version, then restore the verified old file and persist `Restored`. If either database or file identity is uncertain, keep both copies and enter `RecoveryBlocked`. Add this exact ordinary-exception case to `IngestExecutionServiceTests` so 4A is safe even before process-termination recovery is added in 4B.

Add `IngestCleanupKind.ReplacementBackup`. `MarkBackupCreatedAsync` appends the backup as the last cleanup entry. The existing cleanup service routes that kind to `DeleteBackupIfMatchesAsync`; it is called only after target verification and database commit.

- [ ] **Step 7: Make EF asset commit idempotent and disposition-aware**

Store `AssetId` directly on `IngestOperationRow`. Replace the current hash-based replay lookup:

~~~csharp
if (operation.Stage is IngestOperationStage.DatabaseCommitted
    or IngestOperationStage.CleanupPending
    or IngestOperationStage.Completed)
{
    return operation.AssetId
        ?? throw new InvalidOperationException("Committed operation has no asset id.");
}
~~~

For `Create` and `KeepBoth`, create a new asset using `committed.RelativePath` as physical filename/path. For `ReplaceRegistered`, load exactly `ReplacementAssetId`, verify `MetadataVersion`, keep `Id`, `ActualFilename`, `RelativePath`, `NormalizedRelativePath`, and `CreatedAtUtc`, then replace size/hash/display/series and ordered author/tag rows. Set the asset metadata version to `checked(existing.MetadataVersion + 1)`; do not copy the inbox draft version into the existing asset concurrency token. For `ReplaceUnregistered`, create a new asset at the selected target path. Persist operation stage and asset ID in the same transaction as the asset mutation. When `ConflictCaseId` is present, rehydrate the chosen case, call `Resolve()`, set its `OperationId`, clear `ActiveInboxItemId`, and persist those changes in this same transaction; there is no window where the asset is committed but the case remains open.

Before staging a registered replacement, serialize an `OldAssetSnapshot` containing asset ID, physical/relative names, media kind, display name, size, SHA-256, series ID, ordered author IDs, ordered tag IDs, metadata version, and created/updated times. This immutable JSON and the verified file backup together constitute the recoverable old version; never reconstruct old relations from current taxonomy queries during recovery.

- [ ] **Step 8: Run focused Application and persistence tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestServiceTests|FullyQualifiedName~ConflictResolutionServiceTests|FullyQualifiedName~IngestExecutionServiceTests|FullyQualifiedName~IngestCleanupServiceTests|FullyQualifiedName~IngestInputResolverTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestPersistenceTests|FullyQualifiedName~ConflictPersistenceTests|FullyQualifiedName~ControlledMediaStorageTests"
~~~

Expected: all selected tests pass; existing ordinary and processed ingest behavior remains green.

- [ ] **Step 9: Register and commit**

~~~csharp
services.AddSingleton<IIngestExecutionService, IngestExecutionService>();
services.AddSingleton<IConflictResolutionService, ConflictResolutionService>();
~~~

~~~powershell
git add apps/media-desktop/src apps/media-desktop/tests
git commit -m "feat(media-desktop): resolve ingest conflicts"
~~~

---

### Task 8: Add the Native Conflict Comparison Dialog and Reopen Flow

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ConflictDialogResult.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/IConflictDialogService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ConflictDialogViewModel.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ConflictComparisonDialog.xaml`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/ConflictComparisonDialog.xaml.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Dialogs/WindowsConflictDialogService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxInspectorViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Dialogs/ConflictDialogViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`

**Interfaces:**
- Produces: `IConflictDialogService.ShowAsync(ConflictCaseDto, CancellationToken)` returning nullable `ConflictDialogResult`.
- Consumes: only conflict DTOs and `IConflictResolutionService`; no filesystem or EF reference enters Desktop.
- Adds: `InboxViewModel.ResolveConflictCommand` and inspector visibility for `ResolveConflict`.

- [ ] **Step 1: Write failing dialog-view-model tests**

~~~csharp
[Fact]
public void Same_name_match_is_selected_by_default()
{
    var viewModel = new ConflictDialogViewModel(CaseWithNamedAndContentMatches());

    Assert.Equal(viewModel.Case.DefaultReplaceMatchId, viewModel.SelectedMatch?.Id);
    Assert.True(viewModel.CanReplace);
}

[Fact]
public void Content_only_case_requires_explicit_selection_for_replace()
{
    var viewModel = new ConflictDialogViewModel(ContentOnlyCase());

    Assert.Null(viewModel.SelectedMatch);
    Assert.False(viewModel.CanReplace);
    viewModel.SelectedMatch = viewModel.Matches[1];
    Assert.True(viewModel.CanReplace);
}

[Fact]
public void Persisted_choice_wins_after_interrupted_confirmation()
{
    ConflictCaseDto restored = ChosenCase(
        ConflictResolution.Replace, ContentMatchId);

    var viewModel = new ConflictDialogViewModel(restored);

    Assert.Equal(ContentMatchId, viewModel.SelectedMatch?.Id);
}
~~~

- [ ] **Step 2: Write failing Inbox command tests**

~~~csharp
[Fact]
public async Task Closing_automatic_conflict_dialog_does_not_resolve_case()
{
    dialog.NextResult = null;
    ingest.Next = ConflictResult(CaseId);

    await viewModel.IngestCommand.ExecuteAsync(null);

    Assert.Equal(0, resolution.CallCount);
    Assert.Equal(InboxItemState.ConflictBlocked, viewModel.Inspector.State);
}

[Fact]
public async Task Reopen_command_resolves_selected_single_target_and_refreshes_details()
{
    dialog.NextResult = new ConflictDialogResult(
        ConflictResolution.Replace, MatchId);

    await viewModel.ResolveConflictCommand.ExecuteAsync(null);

    Assert.Equal((CaseId, ConflictResolution.Replace, MatchId), resolution.LastCall);
    Assert.True(query.DetailsReloaded);
}
~~~

- [ ] **Step 3: Run Desktop tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictDialogViewModelTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
~~~

Expected: FAIL because dialog types and reopen command are absent.

- [ ] **Step 4: Implement a dialog that distinguishes Skip from close/Esc**

Do not map Skip to `ContentDialog.CloseButtonText`, because clicking close and pressing `Esc` both produce `ContentDialogResult.None`. Put three explicit buttons inside dialog content; each click sets a nullable result and calls `Hide()`. Window close or `Esc` leaves result null.

Use these stable automation IDs:

~~~xml
AutomationProperties.AutomationId="ConflictComparisonDialog"
AutomationProperties.AutomationId="ConflictIncomingPreview"
AutomationProperties.AutomationId="ConflictMatchList"
AutomationProperties.AutomationId="ConflictSkipButton"
AutomationProperties.AutomationId="ConflictKeepBothButton"
AutomationProperties.AutomationId="ConflictReplaceButton"
~~~

Layout: incoming item on the left; selectable matches on the right; preview, physical/display names, registered state, conflict reasons, size, time, media properties, authors, series, and tags. Use existing `MediaPreviewControl`; null preview displays its normal empty-state graphic.

- [ ] **Step 5: Implement the dialog service and ViewModel flow**

~~~csharp
public sealed record ConflictDialogResult(
    ConflictResolution Resolution,
    Guid? SelectedMatchId);

public interface IConflictDialogService
{
    Task<ConflictDialogResult?> ShowAsync(
        ConflictCaseDto conflict,
        CancellationToken cancellationToken);
}
~~~

The Windows service obtains the main window `XamlRoot` from `WindowContext`, creates a fresh dialog for each call, and never caches paths or DTOs.

After `IngestAsync` returns `ConflictBlocked`, flush metadata has already occurred; load the case and open the dialog. If it returns null, reload cards/details only. If it returns a decision, call `IConflictResolutionService.ResolveAsync`, display the outcome, and reload selected details. The explicit right-pane command follows the same method.

- [ ] **Step 6: Bind inspector action and register the service**

Add a right-pane button with `AutomationId="ResolveInboxConflictButton"`, content “处理冲突”, and visibility based on `InboxAction.ResolveConflict`. Register:

~~~csharp
services.AddSingleton<IConflictDialogService, WindowsConflictDialogService>();
~~~

- [ ] **Step 7: Verify Desktop and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ConflictDialogViewModelTests|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests
git commit -m "feat(media-desktop): add conflict comparison dialog"
~~~

---

### Task 9: Prove and Package the Stage 4A Checkpoint

**Files:**
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration/ConflictResolutionFlowTests.cs`
- Move: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage3PackageProbe.cs` → `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage4PackageProbe.cs`
- Move: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage3PackageProbeOptions.cs` → `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage4PackageProbeOptions.cs`
- Move: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage3PackageProbeOptionsTests.cs` → `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage4PackageProbeOptionsTests.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs`
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Modify: `apps/media-desktop/README.md`
- Create: `docs/media-desktop-stage4a-test-guide.md`

**Interfaces:**
- Produces probe mode `conflict` through `--stage4-package-probe-root`, `--stage4-package-probe-token`, and `--stage4-package-probe-mode conflict`.
- Produces package `dist/TraceFold-MediaDesktop-Stage4A-win-x64.zip` when publish receives `-StageCheckpoint 4A`.
- Proves: registered replace, unmanaged replace, duplicate-content Keep Both, close/reopen persistence, and processed-output conflict.

- [ ] **Step 1: Write failing real integration flows**

Using `TestMediaFolders`, real `Sha256FileHasher`, real `ControlledMediaStorage`, and migrated SQLite, add:

~~~csharp
[Fact]
public async Task Registered_replace_preserves_id_filename_and_updates_all_metadata()
{
    Guid oldId = await SeedRegisteredAssetAsync("photo.jpg", OldBytes, OldMetadata);
    Guid itemId = await ScanReadySourceAsync("photo.jpg", NewBytes, NewMetadata);
    ConflictCaseRecord conflict = await RequestConflictAsync(itemId, oldId);

    IngestResult result = await resolution.ResolveAsync(
        conflict.Id, ConflictResolution.Replace,
        conflict.Matches.Single(match => match.AssetId == oldId).Id, Token);

    Assert.Equal(oldId, result.AssetId);
    AssertAsset(oldId, "photo.jpg", NewBytes, NewMetadata);
    Assert.False(File.Exists(SourcePath(itemId)));
    AssertNoInternalRecoveryFiles();
}

[Fact]
public async Task Keep_both_allows_duplicate_hash_and_uses_next_available_suffix()
{
    await SeedRegisteredAssetAsync("photo.jpg", SharedBytes, OldMetadata);
    folders.WriteLibrary("photo (2).jpg", OccupierBytes);
    Guid itemId = await ScanReadySourceAsync("photo.jpg", SharedBytes, NewMetadata);

    IngestResult result = await ResolveKeepBothAsync(itemId);

    Assert.Equal(IngestOutcome.Completed, result.Outcome);
    AssertFile("photo (3).jpg", SharedBytes);
    Assert.Equal(2, await CountAssetsByHashAsync(Hash(SharedBytes)));
}
~~~

Add unmanaged replacement, source-change invalidation, target-change invalidation, combined name/content conflict, multiple content targets replacing only one, image flow, video flow, and processed MP4/M4A output conflict while both originals remain until successful resolution.

- [ ] **Step 2: Run integration tests and verify red or expose defects**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ConflictResolutionFlowTests
~~~

Expected before completing Task 9: every scenario passes. If a test reveals a defect, keep the failing test, fix only the owning Task 3–8 component, rerun its focused tests, and make a `fix(media-desktop): <specific conflict behavior>` commit before continuing.

- [ ] **Step 3: Rename and extend the safe package probe**

Keep Stage 3 fixture ownership and hash checks and add repository fixture `photo.jpg` with SHA-256 `7D1BD6791F1D40FFAFE6C7704FB2E71B6FBF6CBD26B71F9A45E62460D61312A6`. `Stage4PackageProbeOptions.Parse` accepts exactly six ordered-or-paired arguments for root, token, and mode; rejects extra entries, reparse points, a non-temp direct child, changed owner token, changed fixture hashes, or pre-existing result.

Conflict mode creates isolated data/inbox/library/work, then runs these headless cases with copied `photo.jpg` bytes and the existing paired MP4/M4A fixtures:

1. registered same-name Replace preserves asset ID;
2. same-content Keep Both produces `(2)` and two asset rows;
3. unmanaged same-name Replace creates one asset;
4. an open conflict is loaded after rebuilding the host once;
5. MP4/M4A processing still completes and ingests.

Write `stage4-probe-result.json` with booleans for all five cases, final asset counts, reported FFmpeg path, and one stable error code. Invalid probe arguments return 2 and never create a window.

- [ ] **Step 4: Parameterize offline publish and smoke for checkpoint 4A**

~~~powershell
param(
    [Parameter(Mandatory)][ValidateSet('4A','4B')][string] $StageCheckpoint,
    [Parameter(Mandatory)][string] $FfmpegSourceRoot
)
$packageName = "TraceFold-MediaDesktop-Stage$StageCheckpoint-win-x64"
~~~

Preserve all existing tool hash/license, package hygiene, no-browser, native-window, single-instance, and clean-exit checks. Stage 4A smoke invokes only probe mode `conflict` and verifies no `.tracefold/recovery` or `.tracefold/commit` content remains after successful cases.

- [ ] **Step 5: Update README and write the 4A manual guide**

The Chinese guide uses copied files and isolated folders and covers:

1. registered name conflict;
2. content-only conflict with multiple selectable matches;
3. combined conflict and same-name default;
4. close/`Esc`, restart, and reopen;
5. Skip source retention;
6. Keep Both suffix and unchanged display name;
7. registered Replace identity/metadata behavior;
8. unmanaged Replace;
9. source and target change invalidation;
10. image and processed-video paths.

Each step contains setup, action, expected UI, expected formal file, expected source retention/deletion, and a pass/fail box.

- [ ] **Step 6: Run full 4A source verification**

~~~powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: build and all tests pass. Record the actual test count.

- [ ] **Step 7: Commit the verified 4A source**

~~~powershell
git add apps/media-desktop scripts docs/media-desktop-stage4a-test-guide.md
git commit -m "test(media-desktop): package stage four conflict checkpoint"
git status --short --branch
~~~

Expected: source worktree is clean, so the package version can identify the exact commit that contains the tested source.

- [ ] **Step 8: Publish, smoke, and stop for the 4A acceptance checkpoint**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 4A -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage4A-win-x64' -StageCheckpoint 4A
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage4A-win-x64.zip'
~~~

Expected: package version identifies the Step 7 commit; smoke reports native window, single instance, no browser, all five conflict probe cases, package-local FFmpeg, clean exit, and no residual package/FFmpeg process. Record the ZIP hash; do not commit the package. Hand the 4A ZIP, hash, automatic evidence, and manual guide to the user. Do not begin Task 10 until the user accepts 4A or explicitly asks to continue without manual acceptance.

---

## Phase B — Stage 4B Recovery Hardening

### Task 10: Add Durable Fault Points and Storage Evidence Inspection

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestFaultContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestExecutionService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Storage/FileStorageContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledMediaStorage.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestFaultInjectionTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledMediaStorageTests.cs`

**Interfaces:**
- Produces: `IngestFaultPoint` and `IIngestFaultInjector` with production `NoOpIngestFaultInjector`.
- Produces: immutable `IngestStorageEvidence` describing source, staged, target, and backup identity without mutation.
- Adds deterministic checkpoints after every persisted mutation boundary.

- [ ] **Step 1: Write failing fault-order tests**

~~~csharp
[Fact]
public async Task Replacement_reports_fault_points_only_after_corresponding_evidence_is_persisted()
{
    await executor.ExecuteAsync(Preparation, ReplacePlan, Folders, Token);

    Assert.Equal(
    [
        IngestFaultPoint.AfterOperationCreated,
        IngestFaultPoint.AfterStaged,
        IngestFaultPoint.AfterBackupCreated,
        IngestFaultPoint.AfterFileCommitted,
        IngestFaultPoint.BeforeDatabaseCommit,
        IngestFaultPoint.AfterDatabaseCommit,
        IngestFaultPoint.BeforeCleanup,
        IngestFaultPoint.AfterCleanup
    ], injector.Seen);
    Assert.True(injector.StoreEvidenceWasPersistedAtEveryPoint);
}
~~~

Add a throwing injector test at every point. Before database commit, source and backup remain; after database commit, committed target and operation asset ID remain even if cleanup never starts.

- [ ] **Step 2: Write failing evidence-inspection tests**

~~~csharp
[Fact]
public async Task InspectOperation_reports_exact_verified_and_unknown_identities()
{
    IngestStorageEvidence evidence = await storage.InspectOperationAsync(
        folders.Value, operation, Token);

    Assert.True(evidence.Staged.MatchesExpected);
    Assert.True(evidence.Backup.MatchesExpected);
    Assert.Equal(FileIdentityKind.Unknown, evidence.Target.Kind);
}
~~~

Cover absent, expected old, expected new, exact backup, wrong hash, and reparse-point evidence. Inspection never moves or deletes a file.

- [ ] **Step 3: Run focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~IngestFaultInjectionTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ControlledMediaStorageTests
~~~

Expected: FAIL because fault and evidence contracts are absent.

- [ ] **Step 4: Implement test-only checkpoints**

~~~csharp
public enum IngestFaultPoint
{
    AfterOperationCreated,
    AfterStaged,
    AfterBackupCreated,
    AfterFileCommitted,
    BeforeDatabaseCommit,
    AfterDatabaseCommit,
    BeforeCleanup,
    AfterCleanupEntry,
    AfterCleanup
}

public interface IIngestFaultInjector
{
    Task ReachAsync(IngestFaultPoint point, Guid operationId, CancellationToken cancellationToken);
}

public sealed class NoOpIngestFaultInjector : IIngestFaultInjector
{
    public Task ReachAsync(IngestFaultPoint point, Guid operationId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
~~~

Register only `NoOpIngestFaultInjector` in normal composition. Tests inject a throwing/recording fake. Diagnostic process termination is added in Task 14 and never selected by environment variables.

- [ ] **Step 5: Implement read-only evidence inspection**

Add the storage inspection method and exact evidence shapes:

~~~csharp
public enum FileIdentityKind
{
    Absent,
    ExpectedSource,
    ExpectedStaged,
    ExpectedOld,
    ExpectedNew,
    ExpectedBackup,
    Unknown
}

public sealed record ObservedFileIdentity(
    FileIdentityKind Kind,
    string? FullPath,
    long? Size,
    string? Sha256)
{
    public bool MatchesExpected => Kind is
        FileIdentityKind.ExpectedSource or
        FileIdentityKind.ExpectedStaged or
        FileIdentityKind.ExpectedOld or
        FileIdentityKind.ExpectedNew or
        FileIdentityKind.ExpectedBackup;
}

public sealed record IngestStorageEvidence(
    ObservedFileIdentity Source,
    ObservedFileIdentity Staged,
    ObservedFileIdentity Target,
    ObservedFileIdentity Backup);

Task<IngestStorageEvidence> InspectOperationAsync(
    LibraryFolders folders,
    IngestOperationRecord operation,
    CancellationToken cancellationToken);
~~~

Resolve only persisted guarded paths. Source must be a registered inbox direct child or processing output descendant; staged files must belong to the operation work directory; target must be a formal-library direct child; backup must be below that operation's recovery directory. Unknown or reparse identities are reported, not normalized into a known state. Inspection never creates an internal directory.

- [ ] **Step 6: Verify and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestFaultInjectionTests|FullyQualifiedName~IngestExecutionServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ControlledMediaStorageTests
git add apps/media-desktop/src apps/media-desktop/tests
git commit -m "test(media-desktop): add ingest fault evidence"
~~~

---

### Task 11: Implement the Pure Recovery Matrix and Idempotent Startup Reconciliation

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestRecoveryDecision.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestRecoveryService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestRecoveryServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestRecoveryDecisionTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration/IngestCrashRecoveryFlowTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Startup/StartupCoordinatorTests.cs`

**Interfaces:**
- Produces: `IngestRecoveryAction.ResumeExecution`, `RestoreBackup`, `CommitDatabase`, `Cleanup`, `AbandonForRetry`, and `Block`.
- Adds store methods `MarkRecoveryBlockedAsync`, `ResumeRecoveryAsync`, and `MarkBackupRestoredAsync`.
- Extends `IIngestExecutionService` with `ResumeAsync(IngestOperationRecord, LibraryFolders, CancellationToken)`.
- Guarantees: `ReconcileAsync` checks every incomplete operation before the runtime gate opens and does not let one blocked item prevent other safe recoveries.

- [ ] **Step 1: Write the failing decision-table tests**

Use one theory row per approved matrix case:

~~~csharp
[Theory]
[MemberData(nameof(Cases))]
public void Evidence_maps_to_one_safe_action(
    IngestOperationStage stage,
    IngestDisposition disposition,
    FileIdentityKind source,
    FileIdentityKind staged,
    FileIdentityKind target,
    FileIdentityKind backup,
    IngestBackupState backupState,
    bool databaseHasNewVersion,
    IngestRecoveryAction expected)
{
    Assert.Equal(expected, IngestRecoveryDecision.Decide(
        Evidence(
            stage, disposition, source, staged, target, backup,
            backupState, databaseHasNewVersion)));
}

public static TheoryData<
    IngestOperationStage,
    IngestDisposition,
    FileIdentityKind,
    FileIdentityKind,
    FileIdentityKind,
    FileIdentityKind,
    IngestBackupState,
    bool,
    IngestRecoveryAction> Cases => new()
{
    { IngestOperationStage.Created, IngestDisposition.Create,
      FileIdentityKind.ExpectedSource, FileIdentityKind.Absent,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      IngestBackupState.NotRequired, false,
      IngestRecoveryAction.ResumeExecution },
    { IngestOperationStage.Staged, IngestDisposition.KeepBoth,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      IngestBackupState.NotRequired, false,
      IngestRecoveryAction.ResumeExecution },
    { IngestOperationStage.Staged, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.ExpectedOld, FileIdentityKind.Absent,
      IngestBackupState.Pending, false,
      IngestRecoveryAction.ResumeExecution },
    { IngestOperationStage.Staged, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.Absent, FileIdentityKind.ExpectedBackup,
      IngestBackupState.Created, false,
      IngestRecoveryAction.ResumeExecution },
    { IngestOperationStage.Staged, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      FileIdentityKind.Absent, FileIdentityKind.ExpectedBackup,
      IngestBackupState.Created, false,
      IngestRecoveryAction.RestoreBackup },
    { IngestOperationStage.FileCommitted, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.ExpectedNew, FileIdentityKind.ExpectedBackup,
      IngestBackupState.Created, false,
      IngestRecoveryAction.CommitDatabase },
    { IngestOperationStage.DatabaseCommitted, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      FileIdentityKind.ExpectedNew, FileIdentityKind.ExpectedBackup,
      IngestBackupState.Created, true,
      IngestRecoveryAction.Cleanup },
    { IngestOperationStage.FileCommitted, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.Unknown, FileIdentityKind.ExpectedBackup,
      IngestBackupState.Created, false,
      IngestRecoveryAction.Block },
    { IngestOperationStage.FileCommitted, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.ExpectedNew, FileIdentityKind.Unknown,
      IngestBackupState.Created, false,
      IngestRecoveryAction.Block },
    { IngestOperationStage.Staged, IngestDisposition.ReplaceUnregistered,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.ExpectedOld, FileIdentityKind.Absent,
      IngestBackupState.Pending, false,
      IngestRecoveryAction.ResumeExecution },
    { IngestOperationStage.FileCommitted, IngestDisposition.Create,
      FileIdentityKind.ExpectedSource, FileIdentityKind.ExpectedStaged,
      FileIdentityKind.Unknown, FileIdentityKind.Absent,
      IngestBackupState.NotRequired, false,
      IngestRecoveryAction.Block },
    { IngestOperationStage.DatabaseCommitted, IngestDisposition.ReplaceRegistered,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      FileIdentityKind.ExpectedNew, FileIdentityKind.Absent,
      IngestBackupState.Deleted, true,
      IngestRecoveryAction.Cleanup },
    { IngestOperationStage.Created, IngestDisposition.Create,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      FileIdentityKind.Absent, FileIdentityKind.Absent,
      IngestBackupState.NotRequired, false,
      IngestRecoveryAction.AbandonForRetry }
};
~~~

Add this service-level guard for committed database evidence without a persisted asset ID:

~~~csharp
[Fact]
public async Task Database_committed_without_operation_asset_id_is_blocked()
{
    fixture.Operation = fixture.Operation with
    {
        Stage = IngestOperationStage.DatabaseCommitted,
        AssetId = null
    };

    await fixture.Service.ReconcileAsync(Token);

    Assert.Equal(IngestOperationStage.RecoveryBlocked, fixture.Store.Saved.Stage);
    Assert.Equal("recovery.asset_identity_missing", fixture.Store.Saved.ErrorCode);
    Assert.Equal(0, fixture.Storage.MutationCount);
}
~~~

Repeated reconciliation of any completed action must return the same asset ID and counts; include that assertion in `IngestRecoveryServiceTests`. Every evidence combination not represented by a safe rule returns `Block`.

- [ ] **Step 2: Run decision tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~IngestRecoveryDecisionTests
~~~

Expected: FAIL because the pure decision type does not exist.

- [ ] **Step 3: Implement the decision as an exhaustive expression**

~~~csharp
public enum IngestRecoveryAction
{
    ResumeExecution,
    RestoreBackup,
    CommitDatabase,
    Cleanup,
    AbandonForRetry,
    Block
}

public sealed record IngestRecoveryEvidence(
    IngestOperationStage Stage,
    IngestDisposition Disposition,
    ObservedFileIdentity Source,
    ObservedFileIdentity Staged,
    ObservedFileIdentity Target,
    ObservedFileIdentity Backup,
    IngestBackupState BackupState,
    bool DatabaseHasNewVersion)
{
    public bool RequiresBackup => Disposition is
        IngestDisposition.ReplaceRegistered or
        IngestDisposition.ReplaceUnregistered;
}

public static IngestRecoveryAction Decide(IngestRecoveryEvidence e) => e switch
{
    { DatabaseHasNewVersion: true,
      RequiresBackup: false,
      Target.Kind: FileIdentityKind.ExpectedNew }
        => IngestRecoveryAction.Cleanup,
    { DatabaseHasNewVersion: true,
      RequiresBackup: true,
      Target.Kind: FileIdentityKind.ExpectedNew,
      BackupState: IngestBackupState.Created,
      Backup.Kind: FileIdentityKind.ExpectedBackup }
        => IngestRecoveryAction.Cleanup,
    { DatabaseHasNewVersion: true,
      RequiresBackup: true,
      Target.Kind: FileIdentityKind.ExpectedNew,
      BackupState: IngestBackupState.Deleted,
      Backup.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.Cleanup,
    { Stage: IngestOperationStage.FileCommitted,
      RequiresBackup: false,
      DatabaseHasNewVersion: false,
      Target.Kind: FileIdentityKind.ExpectedNew }
        => IngestRecoveryAction.CommitDatabase,
    { Stage: IngestOperationStage.FileCommitted,
      RequiresBackup: true,
      DatabaseHasNewVersion: false,
      Target.Kind: FileIdentityKind.ExpectedNew,
      BackupState: IngestBackupState.Created,
      Backup.Kind: FileIdentityKind.ExpectedBackup }
        => IngestRecoveryAction.CommitDatabase,
    { RequiresBackup: true,
      Backup.Kind: FileIdentityKind.ExpectedBackup,
      Target.Kind: FileIdentityKind.Absent,
      Staged.Kind: FileIdentityKind.ExpectedStaged }
        => IngestRecoveryAction.ResumeExecution,
    { RequiresBackup: true,
      Source.Kind: FileIdentityKind.ExpectedSource,
      Staged.Kind: FileIdentityKind.ExpectedStaged,
      Target.Kind: FileIdentityKind.ExpectedOld,
      Backup.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.ResumeExecution,
    { RequiresBackup: true,
      Backup.Kind: FileIdentityKind.ExpectedBackup,
      Target.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.RestoreBackup,
    { RequiresBackup: false,
      DatabaseHasNewVersion: false,
      Staged.Kind: FileIdentityKind.ExpectedStaged,
      Target.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.ResumeExecution,
    { Stage: IngestOperationStage.Created,
      Disposition: IngestDisposition.Create,
      DatabaseHasNewVersion: false,
      Source.Kind: FileIdentityKind.ExpectedSource,
      Staged.Kind: FileIdentityKind.Absent,
      Target.Kind: FileIdentityKind.Absent,
      Backup.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.ResumeExecution,
    { Stage: IngestOperationStage.Created,
      Disposition: IngestDisposition.KeepBoth,
      DatabaseHasNewVersion: false,
      Source.Kind: FileIdentityKind.ExpectedSource,
      Staged.Kind: FileIdentityKind.Absent,
      Backup.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.ResumeExecution,
    { Stage: IngestOperationStage.Created,
      RequiresBackup: true,
      DatabaseHasNewVersion: false,
      Source.Kind: FileIdentityKind.ExpectedSource,
      Staged.Kind: FileIdentityKind.Absent,
      Target.Kind: FileIdentityKind.ExpectedOld,
      Backup.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.ResumeExecution,
    { Stage: IngestOperationStage.Created,
      DatabaseHasNewVersion: false,
      Source.Kind: FileIdentityKind.Absent,
      Staged.Kind: FileIdentityKind.Absent,
      Target.Kind: FileIdentityKind.Absent or FileIdentityKind.ExpectedOld,
      Backup.Kind: FileIdentityKind.Absent }
        => IngestRecoveryAction.AbandonForRetry,
    _ => IngestRecoveryAction.Block
};
~~~

Refine the expression until all explicit theory rows pass; do not add a permissive default.

- [ ] **Step 4: Write failing restart integration tests**

For every `IngestFaultPoint`, build a fresh temporary database/folder fixture, run until the injected exception, dispose the host without cleanup, build a second host over the same roots, call startup, and assert:

~~~csharp
Assert.True(OldOrNewVerifiedCopyExists());
Assert.Equal(ExpectedAssetCount, await CountAssetsAsync());
Assert.Equal(ExpectedAssetId, await ReadReplacementAssetIdAsync());
Assert.False(await HasDuplicateOperationAssetAsync());
Assert.False(await HasOrphanFormalFileAsync());
~~~

Corrupt target/backup/database-version cases assert `RecoveryBlocked` and byte-for-byte preservation of every observed file.

- [ ] **Step 5: Execute recovery actions idempotently**

`IngestRecoveryService.ReconcileAsync` performs, per operation:

1. load current operation and database asset evidence;
2. inspect guarded storage evidence;
3. call the pure decision;
4. execute only that action;
5. reload before any following action rather than assuming in-memory state.

`ResumeExecution` calls executor `ResumeAsync`, which starts at the persisted stage and never recreates the operation. `CommitDatabase` calls the idempotent disposition-aware `CommitAssetAsync`. `RestoreBackup` verifies and restores, marks backup restored, clears the active operation, and returns the inbox item to ready. `Cleanup` calls cleanup service. `Block` stores the previous stage, stable error code `recovery.identity_ambiguous`, and inbox `RecoveryBlocked`.

`RetryAsync` on a recovery-blocked item first repeats read-only evidence inspection. It resumes only when the decision is no longer `Block`.

- [ ] **Step 6: Verify recovery and startup sequencing**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestRecoveryDecisionTests|FullyQualifiedName~IngestRecoveryServiceTests|FullyQualifiedName~StartupCoordinatorTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~IngestCrashRecoveryFlowTests
~~~

Expected: all matrix, service, startup-order, and real restart tests pass.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src apps/media-desktop/tests
git commit -m "feat(media-desktop): recover interrupted replacements"
~~~

---

### Task 12: Add Finite Delayed Cleanup and Immediate Retry

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/CleanupRetrySchedule.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestCleanupSignal.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestCleanupService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/IngestCleanupWorker.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestCleanupServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/CleanupRetryScheduleTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Workers/IngestCleanupWorkerTests.cs`

**Interfaces:**
- Produces: `CleanupRetrySchedule.DelayAfterFailure(int failureCount)`.
- Adds store methods `LoadDueCleanupAsync`, `FindNextCleanupDueAsync`, `ScheduleCleanupRetryAsync`, and `ResetCleanupRetryAsync`.
- Adds: `IIngestRecoveryService.RetryCleanupAsync(Guid, CancellationToken)` while existing `RetryAsync` remains the recovery recheck operation.
- Produces: one hosted worker and channel signal; no polling of user directories.
- Changes: explicit retry performs cleanup only and never replays file/database commit.

- [ ] **Step 1: Write failing schedule tests**

~~~csharp
[Theory]
[InlineData(1, 30)]
[InlineData(2, 120)]
[InlineData(3, 600)]
[InlineData(4, 3600)]
public void Failure_count_maps_to_exact_delay(int count, int seconds)
{
    Assert.Equal(TimeSpan.FromSeconds(seconds),
        CleanupRetrySchedule.DelayAfterFailure(count));
}

[Fact]
public void Fifth_failure_stops_the_current_automatic_round()
{
    Assert.Null(CleanupRetrySchedule.DelayAfterFailure(5));
}
~~~

- [ ] **Step 2: Write failing worker and isolation tests**

~~~csharp
[Fact]
public async Task Due_cleanup_failure_schedules_next_attempt_without_blocking_sibling()
{
    store.Due = [LockedOperation, DeletableOperation];

    await worker.RunDueAsync(Token);

    Assert.Equal(1, store.ScheduleCalls[LockedOperation.Id]);
    Assert.Contains(DeletableOperation.Id, store.Completed);
}

[Fact]
public async Task Manual_retry_ignores_future_due_time_but_only_runs_cleanup()
{
    await recovery.RetryCleanupAsync(CleanupPendingItemId, Token);

    Assert.Equal(1, cleanup.CallCount);
    Assert.Equal(0, executor.ResumeCallCount);
}
~~~

Add locked source, locked processing output, locked backup, source-changed, missing source, cancellation, next-startup reactivation, and signal coalescing tests with `TimeProvider` under test control.

- [ ] **Step 3: Run cleanup tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CleanupRetryScheduleTests|FullyQualifiedName~IngestCleanupServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~IngestCleanupWorkerTests
~~~

Expected: FAIL because schedule/store/worker behavior is missing.

- [ ] **Step 4: Persist and apply exact retry timing**

On a cleanup failure, increment `CleanupAttemptCount`, set the stable error code, and set `NextCleanupAttemptAtUtc = now + delay`; after the fourth scheduled retry fails, persist null next time and remain `CleanupPending`. On application startup, set exhausted cleanup-pending operations due once for the new run. Manual retry clears only the due timestamp before calling cleanup; it does not create an operation or call file/database commit.

Extend the recovery contract without breaking existing test fakes, then override it in the real service:

~~~csharp
public interface IIngestRecoveryService
{
    Task ReconcileAsync(CancellationToken cancellationToken);
    Task<RecoveryResult> RetryAsync(Guid inboxItemId, CancellationToken cancellationToken);

    Task<RecoveryResult> RetryCleanupAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken) =>
        RetryAsync(inboxItemId, cancellationToken);
}
~~~

The default preserves source compatibility only. `IngestRecoveryService.RetryCleanupAsync` must require `CleanupPending`, ignore a future due timestamp, invoke only `IIngestCleanupService`, and return `recovery.cleanup_not_retryable` for every other stage.

Add these exact scheduling boundaries to `IngestContracts.cs`:

~~~csharp
public interface IIngestCleanupSignal
{
    void Signal();
    Task WaitAsync(CancellationToken cancellationToken);
}

public interface IIngestCleanupWorkStore
{
    Task<IReadOnlyList<IngestOperationRecord>> LoadDueCleanupAsync(
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<DateTimeOffset?> FindNextCleanupDueAsync(CancellationToken cancellationToken);
    Task ScheduleCleanupRetryAsync(
        Guid operationId, string errorCode, int failureCount,
        DateTimeOffset? nextAttemptAtUtc, CancellationToken cancellationToken);
    Task ResetCleanupRetryAsync(Guid operationId, CancellationToken cancellationToken);
}
~~~

Implement `IIngestCleanupWorkStore` on `EfIngestStore` and register it as the same singleton instance used for `IIngestStore`; do not create two repository objects with independent state.

Each successful cleanup entry immediately persists `CompletedAtUtc`, clears its last error, and calls fault point `AfterCleanupEntry`. A later retry loads only incomplete entries.

- [ ] **Step 5: Implement the worker wait loop**

~~~csharp
public async Task RunDueAsync(CancellationToken cancellationToken)
{
    IReadOnlyList<IngestOperationRecord> due = await store.LoadDueCleanupAsync(
        timeProvider.GetUtcNow(), cancellationToken);
    foreach (IngestOperationRecord operation in due)
    {
        try { await cleanup.CleanupAsync(operation, folders, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { logger.LogError(error, "IngestCleanupWorkerFailed OperationId={OperationId}", operation.Id); }
    }
}
~~~

The hosted loop waits for `IMediaRuntimeGate`, then waits until the earlier of the channel signal or `FindNextCleanupDueAsync`. It does not enumerate inbox/library roots to discover work.

- [ ] **Step 6: Register, verify, and commit**

~~~csharp
services.AddSingleton<IIngestCleanupSignal, IngestCleanupSignal>();
services.AddHostedService<IngestCleanupWorker>();
~~~

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CleanupRetryScheduleTests|FullyQualifiedName~IngestCleanupServiceTests|FullyQualifiedName~IngestRecoveryServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestCleanupWorkerTests|FullyQualifiedName~IngestCrashRecoveryFlowTests"
git add apps/media-desktop/src apps/media-desktop/tests
git commit -m "feat(media-desktop): retry delayed ingest cleanup"
~~~

---

### Task 13: Project Recovery and Cleanup State into the Right Pane

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxQueryContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/RecoveryLocationContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfRecoveryLocationResolver.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Services/IRecoveryRevealService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Services/WindowsRecoveryRevealService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxInspectorViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/EfInboxQueryStoreRecoveryTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Services/WindowsRecoveryRevealServiceTests.cs`

**Interfaces:**
- Produces: `RecoverySummaryDto` on inbox details.
- Produces: guarded `IRecoveryLocationResolver.ResolveAsync(Guid inboxItemId, ...)` and Desktop reveal adapter.
- Adds: “重新检查”, “立即重试”, and “打开文件位置” actions with distinct command routing.
- Guarantees: every store/recovery/cleanup transition publishes `InboxActivityKind.ItemChanged` so selected details refresh.

- [ ] **Step 1: Write failing query projection tests**

~~~csharp
[Fact]
public async Task Recovery_blocked_details_expose_stage_reason_and_actions()
{
    await SeedRecoveryBlockedAsync(IngestOperationStage.Staged,
        "recovery.identity_ambiguous");

    InboxDetailsDto details = (await store.GetDetailsAsync(ItemId, Token))!;

    Assert.Equal(IngestOperationStage.Staged, details.Recovery!.BlockedFromStage);
    Assert.Equal("recovery.identity_ambiguous", details.Recovery.ErrorCode);
    Assert.True(details.AllowedActions.HasFlag(InboxAction.RecheckRecovery));
}
~~~

Also test cleanup-pending attempt count/next time and completed assets showing no recovery summary.

- [ ] **Step 2: Write failing ViewModel refresh and command tests**

~~~csharp
[Fact]
public async Task Activity_after_cleanup_completion_refreshes_selected_inspector()
{
    await viewModel.LoadAsync(Token);
    await viewModel.SelectAsync(viewModel.Items.Single(), Token);
    query.SetCompleted();

    activity.Publish(InboxActivityKind.ItemChanged);
    await dispatcher.DrainAsync();

    Assert.Equal(InboxItemState.Completed, viewModel.Inspector.State);
    Assert.Equal(string.Empty, viewModel.Inspector.RecoveryStatusText);
}

[Fact]
public async Task Retry_buttons_route_to_distinct_safe_operations()
{
    await viewModel.RecheckRecoveryCommand.ExecuteAsync(null); // calls RetryAsync
    await viewModel.RetryCleanupCommand.ExecuteAsync(null);

    Assert.Equal(1, recovery.RecheckCalls);
    Assert.Equal(1, recovery.CleanupRetryCalls);
}
~~~

- [ ] **Step 3: Run query/Desktop tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~EfInboxQueryStoreRecoveryTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~WindowsRecoveryRevealServiceTests"
~~~

Expected: FAIL because recovery summary and commands do not exist.

- [ ] **Step 4: Add query and inspector projection**

~~~csharp
public sealed record RecoverySummaryDto(
    Guid OperationId,
    IngestOperationStage Stage,
    IngestOperationStage? BlockedFromStage,
    string? ErrorCode,
    int CleanupAttemptCount,
    DateTimeOffset? NextCleanupAttemptAtUtc);
~~~

Map status text precisely:

- `RecoveryBlocked`: “无法自动恢复，请重新检查。”
- `CleanupPending` with a due time: “入库完成，等待再次清理。”
- `CleanupPending` without a due time: “入库完成，清理需要手动重试。”
- no summary: empty recovery text.

Do not reuse “排队中” for ingest, recovery, or cleanup states.

- [ ] **Step 5: Add guarded reveal and right-pane commands**

Use these exact boundaries:

~~~csharp
public sealed record RecoveryLocationDto(string TrustedFullPath);

public interface IRecoveryLocationResolver
{
    Task<RecoveryLocationDto?> ResolveAsync(
        Guid inboxItemId,
        CancellationToken cancellationToken);
}

public interface IRecoveryRevealService
{
    Task<bool> RevealAsync(Guid inboxItemId, CancellationToken cancellationToken);
}
~~~

The application resolver returns one existing, guarded path in this priority: persisted backup for `RecoveryBlocked`, formal target, effective input source. It rejects paths outside the three configured roots and never returns a missing path. The Windows service starts `explorer.exe /select,<path>` through the existing controlled process-starter pattern and returns false when the application resolver returns null.

Add stable IDs:

~~~xml
AutomationProperties.AutomationId="RecheckIngestRecoveryButton"
AutomationProperties.AutomationId="RetryIngestCleanupButton"
AutomationProperties.AutomationId="OpenRecoveryLocationButton"
~~~

Show “重新检查” only for `RecheckRecovery`, “立即重试” only for `RetryCleanup`, and “打开文件位置” when the resolver reports an available controlled location.

- [ ] **Step 6: Publish activity after every durable change**

Inject `IInboxActivityNotifier` into conflict resolution, execution, recovery, and cleanup. Publish once after each public operation completes or becomes blocked; the existing `InboxViewModel.OnInboxActivity` dispatcher then reloads cards and the selected inspector. Do not update the inspector directly from workers.

- [ ] **Step 7: Verify and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~EfInboxQueryStoreRecoveryTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests|FullyQualifiedName~WindowsRecoveryRevealServiceTests|FullyQualifiedName~DesktopCompositionRootTests"
git add apps/media-desktop/src apps/media-desktop/tests
git commit -m "feat(media-desktop): surface ingest recovery status"
~~~

---

### Task 14: Prove Crash Recovery in the Stage 4B Offline Package

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage4PackageProbe.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage4PackageProbeOptions.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/DiagnosticIngestFaultInjector.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage4PackageProbeOptionsTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage4LoggingPrivacyTests.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Modify: `apps/media-desktop/README.md`
- Create: `docs/media-desktop-stage4b-test-guide.md`

**Interfaces:**
- Adds probe modes `crash-seed` and `crash-resume` plus explicit `--stage4-package-probe-fault`.
- Diagnostic injector may terminate only the probe process after ownership validation; normal Desktop composition always uses the no-op injector.
- Produces final package `dist/TraceFold-MediaDesktop-Stage4B-win-x64.zip`.

- [ ] **Step 1: Write failing option and ownership tests**

Test that:

- `conflict` mode rejects a fault argument;
- `crash-seed` requires one named `IngestFaultPoint`;
- `crash-resume` rejects a fault argument and requires the exact persisted owner token/manifest;
- normal command lines remain `NotRequested`;
- any extra root entry, reparse point, changed token, changed manifest, non-temp root, or unknown point returns `Invalid` before host creation.

~~~csharp
[Fact]
public void Crash_seed_requires_a_known_fault_point()
{
    Stage4PackageProbeOptionResult result = Stage4PackageProbeOptions.Parse(
        Arguments("crash-seed", fault: "AfterBackupCreated"));

    Assert.Equal(Stage4PackageProbeOptionState.Ready, result.State);
    Assert.Equal(IngestFaultPoint.AfterBackupCreated, result.Options!.FaultPoint);
}
~~~

- [ ] **Step 2: Run option tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Stage4PackageProbeOptionsTests
~~~

Expected: FAIL until crash modes and validation are implemented.

- [ ] **Step 3: Implement an ownership-gated diagnostic terminator**

`DiagnosticIngestFaultInjector` is constructed only inside `Stage4PackageProbe.RunAsync` after root, owner token, and scenario manifest validation. At the selected point it flushes the probe manifest/result prefix and calls `Environment.FailFast("Stage4 diagnostic crash")`. It has no DI registration in `DesktopCompositionRoot`, no environment-variable selector, and no UI command.

- [ ] **Step 4: Implement seed/resume probe phases**

`crash-seed` creates a registered replacement conflict, persists the selected resolution, and exits through the diagnostic injector. The smoke script expects a nonzero process exit and must not treat that expected diagnostic crash as a package failure.

`crash-resume` opens the same data/inbox/library/work roots, runs normal startup recovery with the no-op injector, verifies asset ID, target hash, source/backup cleanup state, absence of duplicate assets, and no unknown formal files, then writes the final JSON result.

Run representative package crashes at `AfterBackupCreated` and `AfterFileCommitted`; the real integration matrix from Task 11 remains responsible for every fault point.

- [ ] **Step 5: Add Stage 4 logging privacy regression**

Force conflict detection, revalidation failure, replacement failure, recovery blocking, and cleanup failure with unique marker strings for a full path, filename, display name, author, series, tag, SHA-256, and probe token. Capture structured logs and assert each marker is absent while case/operation IDs, stage, and stable error code remain:

~~~csharp
Assert.DoesNotContain(PathMarker, renderedLog, StringComparison.Ordinal);
Assert.DoesNotContain(DisplayNameMarker, renderedLog, StringComparison.Ordinal);
Assert.DoesNotContain(HashMarker, renderedLog, StringComparison.Ordinal);
Assert.Contains(operationId.ToString(), renderedLog, StringComparison.OrdinalIgnoreCase);
Assert.Contains("recovery.identity_ambiguous", renderedLog, StringComparison.Ordinal);
~~~

Run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Stage4LoggingPrivacyTests
~~~

Expected: the privacy regression passes without weakening diagnostic IDs/stages/error codes.

- [ ] **Step 6: Extend 4B smoke and manual guide**

Smoke order:

1. native window, single instance, no browser;
2. conflict probe from 4A;
3. crash-seed/resume after backup;
4. crash-seed/resume after file commit;
5. package-local MP4/M4A flow;
6. clean close with zero TraceFold/FFmpeg residue.

The Chinese 4B guide covers forced termination during replacement, automatic continuation/restoration, `RecoveryBlocked` after deliberate external change, locked source/output/backup cleanup, the exact retry sequence, “立即重试”, “重新检查”, “打开文件位置”, and isolation between two items.

- [ ] **Step 7: Commit the verified Stage 4B source**

~~~powershell
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
git add apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage4LoggingPrivacyTests.cs scripts apps/media-desktop/README.md docs/media-desktop-stage4b-test-guide.md
git commit -m "test(media-desktop): package stage four recovery checkpoint"
git status --short --branch
~~~

Expected: build and every test pass; source worktree is clean; the following package can identify this exact commit.

- [ ] **Step 8: Publish and smoke Stage 4B**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 4B -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage4B-win-x64' -StageCheckpoint 4B
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage4B-win-x64.zip'
~~~

Expected: package version identifies the Step 7 commit; smoke proves both conflict behavior and two real process-termination recoveries; package-local FFmpeg is used; no browser or child-process residue remains. Do not add probe roots, result JSON, ZIP files, published binaries, databases, logs, or crash dumps.

- [ ] **Step 9: Stop for the 4B manual checkpoint**

Hand the Stage 4B ZIP, SHA-256, automated evidence, and `docs/media-desktop-stage4b-test-guide.md` to the user. Do not write final acceptance evidence in Task 15 until the user reports the 4B manual result or explicitly waives manual verification.

---

### Task 15: Run Full Regression, Acceptance Evidence, and Final Review

**Files:**
- Modify only if verification exposes a defect: the file owned by the task that introduced it.
- Create: `docs/media-desktop-stage4-acceptance.md`

**Interfaces:**
- Consumes: every Task 1–14 deliverable.
- Produces: reproducible Stage 4A and 4B acceptance evidence with exact test counts, commits, package paths, ZIP hashes, probe outcomes, and limitations.

- [ ] **Step 1: Run a fresh offline restore, Release build, and every test**

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: 0 warnings, 0 errors, and every test passes. Record exact per-project totals; do not predict the new total in the acceptance document before running this command.

- [ ] **Step 2: Run the safety-focused suites again**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ConflictCaseTests|FullyQualifiedName~IngestOperationTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Conflict|FullyQualifiedName~IngestRecovery|FullyQualifiedName~IngestCleanup|FullyQualifiedName~IngestFault"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Stage4MigrationTests|FullyQualifiedName~ConflictResolutionFlowTests|FullyQualifiedName~IngestCrashRecoveryFlowTests|FullyQualifiedName~IngestCleanupWorkerTests|FullyQualifiedName~ControlledMediaStorageTests|FullyQualifiedName~Stage4LoggingPrivacyTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ConflictDialog|FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~Stage4PackageProbeOptionsTests"
~~~

Expected: conflict transitions, target choice, path guards, migration, every fault point, finite cleanup, UI reopening, and diagnostic ownership all pass.

- [ ] **Step 3: Rebuild and smoke the final 4B package**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -StageCheckpoint 4B -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage4B-win-x64' -StageCheckpoint 4B
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage4B-win-x64.zip'
~~~

Expected: final package smoke passes with restricted `PATH`, package-local tools, conflict cases, representative crash/restart, native window, single instance, no browser, and no residual process.

- [ ] **Step 4: Inspect repository and package hygiene**

~~~powershell
git status --short
git diff --check main...HEAD
Get-ChildItem -LiteralPath 'dist\TraceFold-MediaDesktop-Stage4B-win-x64' -Recurse -File |
    Select-Object FullName,Length
~~~

Confirm the package contains no source, PDB, user database, logs, work files, probe results, crash dumps, Python, Node, browsers, or tool outside `tools/ffmpeg`. Confirm Git contains no `dist` files, generated databases, logs, temporary recovery files, or FFmpeg executables.

- [ ] **Step 5: Review every safety invariant against the specification**

Check explicitly:

- Closing/`Esc` never resolves a case.
- One active case and one active operation exist per inbox item.
- Same-name is default, content-only requires selection, and Replace affects one target.
- Duplicate SHA assets are supported and all matches are returned.
- Display name never changes physical naming rules.
- Registered Replace preserves asset ID and filename while replacing all metadata.
- Unmanaged Replace creates an asset only after a verified backup.
- No file mutation occurs before persisted recovery evidence.
- Backup remains until target and database verify.
- Recovery has no permissive default and is idempotent across repeated startup.
- Ambiguous identities preserve files and become `RecoveryBlocked`.
- Cleanup is per-entry, guarded, finite, isolated, and manually retryable.
- MP4/M4A originals remain until successful formal commit and are then removed through the same manifest.
- Desktop contains no hash, suffix, overwrite, path, or restore rule.
- Production contains no diagnostic fault selector.
- Conflict/recovery/cleanup logs contain no paths, filenames, metadata values, hashes, or owner tokens.
- Stage 2/3 scanning, metadata, taxonomy, preview, processing, library, shutdown, and packaging remain green.

If review finds a defect, add a focused failing test, make the smallest fix in the owning component, rerun the affected project and the full solution, then commit with `fix(media-desktop): <specific behavior>`.

- [ ] **Step 6: Write and commit acceptance evidence**

`docs/media-desktop-stage4-acceptance.md` records:

- 4A accepted commit, package path, ZIP SHA-256, automated counts, and manual result;
- 4B final commit, package path, ZIP SHA-256, automated counts, and manual result;
- migration result from a Stage 3 database;
- every fault-point integration result and the two package process-termination probes;
- cleanup retry timings and locked-file results;
- package-local FFmpeg/FFprobe version and hashes;
- restricted-`PATH`, native window, single instance, no browser, clean exit, and zero residual process results;
- known limitation that no clean Windows virtual machine is available, retaining clean Windows 11 x64 verification as a future release gate.

~~~powershell
git add docs/media-desktop-stage4-acceptance.md
git commit -m "docs(media-desktop): record stage four acceptance"
git status --short --branch
~~~

Expected: the feature worktree is clean and ready for the branch-finishing workflow.

## Spec Coverage

- Unified persistent case and one ingest state machine: Tasks 1–4, 6, and 7.
- Name/content/combined conflicts, registered/unmanaged matches, and all duplicate hashes: Tasks 3, 4, 6, and 9.
- Close/reopen, Windows-style preview comparison, and single-target selection: Tasks 6, 8, and 9.
- Skip source retention: Tasks 4, 7, and 9.
- Keep Both suffix race, independent asset, and unchanged display name: Tasks 5, 7, and 9.
- Registered Replace identity plus incoming metadata: Tasks 3, 5, 7, and 9.
- Unmanaged Replace with backup then asset creation: Tasks 5, 7, and 9.
- Revalidation and case invalidation after external change: Tasks 6, 7, and 9.
- Duplicate-hash migration with non-unique index: Tasks 3, 4, and 15.
- Operation-scoped backup, same-volume commit, restore, and no-loss invariants: Tasks 3, 5, 7, 10, and 11.
- Startup recovery and ambiguity stop: Tasks 10, 11, 13, and 14.
- Fault injection at every durable boundary: Tasks 10, 11, 14, and 15.
- Finite cleanup backoff, immediate retry, multi-source cleanup, and item isolation: Tasks 7, 12, 13, and 15.
- Right-pane status, recheck, retry, reveal, and automatic refresh: Tasks 8 and 13.
- Images, videos, and processed MP4/M4A regression: Tasks 9, 14, and 15.
- 4A and 4B offline packages, manual guides, hashes, and evidence: Tasks 9, 14, and 15.
- No Stage 5 batch/filter/recovery-center scope: Global Constraints and final invariant review in Task 15.

## Execution Checkpoints

1. After Task 3: inspect the migration, active-case uniqueness, operation backup columns, and non-unique SHA index before implementing file mutation.
2. After Task 5: review direct-child/descendant guards, reparse rejection, suffix race behavior, and backup/restore byte preservation.
3. After Task 7: review all four dispositions in the single executor and confirm asset replay uses persisted `AssetId`, never SHA uniqueness.
4. After Task 9: stop and deliver the Stage 4A package and manual checklist; wait for user acceptance before Phase B.
5. After Task 11: review the complete evidence matrix and inspect that every unspecified combination blocks without mutation.
6. After Task 12: use controlled time tests to confirm exactly four delays and no sibling blockage.
7. After Task 14: inspect the diagnostic ownership gate and prove normal UI composition can only resolve the no-op fault injector.
8. After Task 15: use `superpowers:requesting-code-review`, then `superpowers:finishing-a-development-branch`; do not merge, push, or publish elsewhere without the user's choice.
