# Native Media Desktop Stage 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manually triggered, persistent, extensible preprocessing queue whose first built-in processor merges a same-basename MP4/M4A pair, validates the result, previews it, and hands it to the existing safe ingest flow without deleting either original before formal commit.

**Architecture:** Keep the existing Desktop → Application → Domain and Desktop → Infrastructure dependency direction. Domain owns item/job transitions, Application owns processor contracts, planning and orchestration, Infrastructure owns SQLite, controlled workspaces and owned FFmpeg/FFprobe processes, and Desktop only binds commands and projected state.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, xUnit v3, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev.

**Spec:** docs/superpowers/specs/2026-08-23-native-media-desktop-stage3-design.md

## Global Constraints

- Execute in an isolated `codex/native-media-desktop-stage3` worktree created with `superpowers:using-git-worktrees`.
- Target Windows 11 x64; keep `TraceFold.Media.Desktop` unpackaged and self-contained.
- Do not run `git pull`, browse, or download dependencies or tools. Restore only from `apps/media-desktop/.packages` with failed remote sources ignored.
- Processing starts only after an explicit user command. Scan, startup, metadata save, and processing success must not auto-ingest.
- Run at most one processing job at a time. Multiple explicit commands form a persistent FIFO queue ordered by `QueuedAtUtc`, then `Id`.
- Closing the app interrupts the running job and every still-queued job. Startup converts stale `Queued`/`Running` rows to `Interrupted` and never auto-resumes them.
- Keep metadata optional: many authors, zero or one series, many tags, and an optional display name that never changes the physical filename.
- Register built-in processors explicitly through dependency injection. Do not load external DLLs, scripts, plugins, or user-supplied FFmpeg arguments.
- A processor may read registered sources and write only its assigned work directory. It may not delete inbox sources or write the formal library.
- A processed output becomes eligible only after FFprobe reports at least one video stream, at least one audio stream, and a positive duration.
- Original MP4/M4A files remain until the formal file and database asset are committed and verified. Cleanup must verify every path, size, and SHA-256 and be idempotent.
- Production runtime resolves only `tools/ffmpeg` below `AppContext.BaseDirectory`; it never falls back to system `PATH` or downloads tools.
- The controlled offline build source is `C:\Program Files\ffmpeg`, supplied explicitly to the publish command. Accept only the hashes recorded in `apps/media-desktop/tools/ffmpeg/win-x64/manifest.json`.
- All automated filesystem tests use test-owned temporary roots. Never read, move, rename, or delete user-configured media directories.
- Preserve Stage 2 conflict behavior: name or registered-content conflicts become `ConflictBlocked`; do not implement Stage 4 conflict choices.
- Every task follows red-green-refactor, ends with focused tests, and makes one reviewable commit.

## Before Task 1

- [ ] Confirm the execution checkout is the isolated feature worktree and starts clean:

~~~powershell
git status --short --branch
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
~~~

- [ ] Restore from local caches and prove the Stage 2 baseline:

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: Release build reports 0 warnings and 0 errors; all 177 Stage 2 tests pass. If a pinned dependency/tool is absent or the baseline fails, stop and report the exact failure before editing.

## File and Responsibility Map

- `Domain/Inbox`: extend the visible item state machine and allowed actions.
- `Domain/Processing`: durable job status and transition invariants only; no FFmpeg concepts.
- `Application/Processing`: processor descriptors/contracts, registry, planner, commands, executor, recovery, progress and queue signal.
- `Infrastructure/Persistence`: processing plans, inputs, attempts, output, cleanup entries and EF repositories.
- `Infrastructure/MediaTools`: package-local tool resolution, owned process execution, progress parsing and FFprobe JSON parsing.
- `Infrastructure/Filesystem`: guarded per-job work directories and multi-root cleanup.
- `Infrastructure/Workers`: one serial processing worker gated by startup recovery.
- `Application/Ingest`: one ingest path that selects either an ordinary source or a validated processed output.
- `Desktop/Pages`: projected status, progress and state-specific commands; no process or database access.
- `scripts` and `Desktop/Diagnostics`: package import verification, Stage 3 headless flow probe and offline smoke.

---

### Task 1: Add Processing State and Job Invariants

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItemState.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxAction.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Domain/Inbox/InboxItem.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Processing/ProcessingJobStatus.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Domain/Processing/ProcessingJob.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Inbox/InboxItemTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Domain.Tests/Processing/ProcessingJobTests.cs`

**Interfaces:**
- Produces: `InboxItemState.ProcessingQueued`, `Processing`, and `ProcessingInterrupted`.
- Produces: `InboxAction.StartProcessing`, `CancelProcessing`, `StopProcessing`, and `RetryProcessing`.
- Produces: `InboxItem.QueueProcessing()`, `BeginProcessing()`, `CancelQueuedProcessing()`, `InterruptProcessing()`, `CompleteProcessing()`, and `FailProcessing(string, bool)`.
- Produces: `ProcessingJob.Queue(Guid id, Guid inboxItemId, int attempt)`, `Start()`, `Report(string, int)`, `Succeed()`, `Interrupt(string)`, `Fail(string, bool)`, and `Cancel()`.

- [ ] **Step 1: Write failing inbox-processing transition tests**

Add tests with these assertions:

~~~csharp
[Fact]
public void Required_item_can_queue_start_and_complete_processing()
{
    InboxItem item = InboxItem.Discover(Guid.NewGuid());
    item.MarkAnalyzing();
    item.MarkRequiresProcessing("mp4_m4a_merge");

    item.QueueProcessing();
    Assert.Equal(InboxItemState.ProcessingQueued, item.State);
    Assert.True(item.AllowedActions.HasFlag(InboxAction.CancelProcessing));

    item.BeginProcessing();
    Assert.Equal(InboxItemState.Processing, item.State);
    Assert.True(item.AllowedActions.HasFlag(InboxAction.StopProcessing));

    item.CompleteProcessing();
    Assert.Equal(InboxItemState.ReadyToTag, item.State);
    Assert.Null(item.RequiredCapability);
    Assert.True(item.AllowedActions.HasFlag(InboxAction.Ingest));
}

[Fact]
public void Interrupted_processing_preserves_capability_and_requires_manual_retry()
{
    InboxItem item = ProcessingItem();
    item.InterruptProcessing();

    Assert.Equal(InboxItemState.ProcessingInterrupted, item.State);
    Assert.Equal("mp4_m4a_merge", item.RequiredCapability);
    Assert.True(item.AllowedActions.HasFlag(InboxAction.RetryProcessing));
    Assert.False(item.AllowedActions.HasFlag(InboxAction.Ingest));
}

[Fact]
public void Processing_failure_does_not_turn_into_ingest_recovery()
{
    InboxItem item = ProcessingItem();
    item.FailProcessing("processing.ffmpeg_failed", isRetryable: true);

    Assert.Equal(InboxItemState.Failed, item.State);
    Assert.True(item.AllowedActions.HasFlag(InboxAction.RetryProcessing));
    Assert.False(item.AllowedActions.HasFlag(InboxAction.Retry));
}
~~~

Use a private `ProcessingItem()` helper that discovers, analyzes, requires `mp4_m4a_merge`, queues, and starts the item.

- [ ] **Step 2: Write failing durable-job tests**

~~~csharp
[Fact]
public void Progress_is_monotonic_and_bounded()
{
    ProcessingJob job = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
    job.Start();
    job.Report("merge", 40);
    job.Report("verify", 95);

    Assert.Equal(95, job.ProgressPercent);
    Assert.Equal("verify", job.CurrentStepKey);
    Assert.Throws<InvalidOperationException>(() => job.Report("merge", 20));
    Assert.Throws<ArgumentOutOfRangeException>(() => job.Report("merge", 101));
}

[Fact]
public void Interrupted_job_is_not_resumable_in_place()
{
    ProcessingJob job = ProcessingJob.Queue(Guid.NewGuid(), Guid.NewGuid(), 1);
    job.Start();
    job.Interrupt("processing.user_stopped");

    Assert.Equal(ProcessingJobStatus.Interrupted, job.Status);
    Assert.Throws<InvalidOperationException>(job.Start);
}
~~~

- [ ] **Step 3: Run the Domain tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxItemTests|FullyQualifiedName~ProcessingJobTests"
~~~

Expected: FAIL because the new enum values, actions, methods, and processing types do not exist.

- [ ] **Step 4: Implement the state/action additions**

Add enum values without renumbering existing values, and extend `AllowedActions` exactly as follows:

~~~csharp
InboxItemState.RequiresProcessing =>
    InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder |
    InboxAction.StartProcessing,
InboxItemState.ProcessingQueued =>
    InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder |
    InboxAction.CancelProcessing,
InboxItemState.Processing =>
    InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder |
    InboxAction.StopProcessing,
InboxItemState.ProcessingInterrupted =>
    InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder |
    InboxAction.RetryProcessing,
InboxItemState.Failed when IsRetryable && RequiredCapability is not null =>
    InboxAction.Preview | InboxAction.EditMetadata | InboxAction.OpenFolder |
    InboxAction.RetryProcessing,
~~~

Implement transition methods so queue/cancel/interrupt/failure retain `RequiredCapability`, completion clears it, and `ResetForSourceChange()` accepts all three new states. Keep ordinary ingest failure behavior unchanged.

- [ ] **Step 5: Implement the processing job aggregate**

Use this public shape and reject all transitions not represented below:

~~~csharp
public enum ProcessingJobStatus
{
    Queued,
    Running,
    Succeeded,
    Cancelled,
    Interrupted,
    Failed
}

public sealed class ProcessingJob
{
    public Guid Id { get; }
    public Guid InboxItemId { get; }
    public int Attempt { get; }
    public ProcessingJobStatus Status { get; private set; }
    public string? CurrentStepKey { get; private set; }
    public int ProgressPercent { get; private set; }
    public string? ErrorCode { get; private set; }
    public bool IsRetryable { get; private set; }

    public static ProcessingJob Queue(Guid id, Guid inboxItemId, int attempt);
    public void Start();
    public void Report(string stepKey, int progressPercent);
    public void Succeed();
    public void Cancel();
    public void Interrupt(string errorCode);
    public void Fail(string errorCode, bool isRetryable);
}
~~~

`Succeed()` is allowed only from `Running`, sets progress to 100, and clears errors. `Cancel()` is allowed only from `Queued`. `Interrupt()` is allowed from `Queued` or `Running`. A terminal instance cannot return to `Queued`; retry creates a new aggregate.

- [ ] **Step 6: Run Domain tests and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Domain.Tests/TraceFold.Media.Domain.Tests.csproj -c Release --no-restore
git add apps/media-desktop/src/TraceFold.Media.Domain apps/media-desktop/tests/TraceFold.Media.Domain.Tests
git commit -m "feat(media-desktop): add processing state invariants"
~~~

Expected: all Domain tests pass.

---

### Task 2: Define Processor, Planner, Tool, and Queue Contracts

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/MediaProcessorRegistry.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingPlanner.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/MediaProcessorRegistryTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/ProcessingPlannerTests.cs`

**Interfaces:**
- Produces: immutable candidate, source, plan, probe, tool, workspace, job, output, progress and command records in `ProcessingContracts.cs`.
- Produces: `IMediaProcessor`, `IMediaProcessorRegistry`, `IProcessingPlanner`, `IProcessingStore`, `IProcessingWorkspace`, `IMediaTool`, `IProcessingQueueSignal`, `IProcessingExecutionController`, `IProcessingCommandService`, and `IProcessingRecoveryService`.
- Produces: `MediaProcessorRegistry.Resolve(string capability)` and `ProcessingPlanner.Create(ProcessingCandidate candidate)`.

- [ ] **Step 1: Write failing registry tests**

~~~csharp
[Fact]
public void Registry_resolves_the_only_processor_for_a_capability()
{
    var expected = new FakeProcessor("merge.v1", "1", "mp4_m4a_merge", 100);
    var registry = new MediaProcessorRegistry([expected]);

    Assert.Same(expected, registry.Resolve("mp4_m4a_merge"));
}

[Fact]
public void Registry_rejects_duplicate_keys_and_tied_capabilities()
{
    Assert.Throws<InvalidOperationException>(() => new MediaProcessorRegistry(
    [
        new FakeProcessor("same", "1", "a", 100),
        new FakeProcessor("same", "2", "b", 100)
    ]));
    Assert.Throws<InvalidOperationException>(() => new MediaProcessorRegistry(
    [
        new FakeProcessor("one", "1", "shared", 100),
        new FakeProcessor("two", "1", "shared", 100)
    ]));
}
~~~

- [ ] **Step 2: Write failing planner tests**

~~~csharp
[Fact]
public void Planner_creates_a_new_job_and_uses_only_processor_owned_plan_data()
{
    Guid itemId = Guid.NewGuid();
    ProcessingCandidate candidate = Candidate(itemId, "mp4_m4a_merge");
    var processor = new FakeProcessor("merge.v1", "3", "mp4_m4a_merge", 100);
    var planner = new ProcessingPlanner(new MediaProcessorRegistry([processor]));

    PlannedProcessing planned = planner.Create(candidate);

    Assert.Equal(itemId, planned.Plan.InboxItemId);
    Assert.Equal(planned.JobId, planned.Plan.JobId);
    Assert.Equal("merge.v1", planned.Plan.ProcessorKey);
    Assert.Equal("3", planned.Plan.ProcessorVersion);
    Assert.Equal(".tracefold/processing/" + planned.JobId.ToString("N"),
        planned.Plan.WorkDirectoryRelativePath.Replace('\\', '/'));
}
~~~

The fake processor's `CreatePlan` must return a plan containing the supplied `jobId` and the candidate's already-hashed source snapshots; it must not receive a caller-selected output path.

- [ ] **Step 3: Run focused Application tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaProcessorRegistryTests|FullyQualifiedName~ProcessingPlannerTests"
~~~

Expected: FAIL because `Application.Processing` does not exist.

- [ ] **Step 4: Add the exact immutable contracts**

Define these core records and enums; keep each collection typed as `IReadOnlyList<T>`:

~~~csharp
public sealed record MediaProcessorDescriptor(
    string Key, string Version, string Capability, int Priority, string DisplayName);

public sealed record ProcessingCandidateSource(
    Guid SourceFileId, string FullPath, string Filename, string Extension,
    long Size, DateTimeOffset LastWriteTimeUtc, string? Sha256, bool IsPrimary);

public sealed record ProcessingCandidate(
    Guid InboxItemId, InboxItemState State, MediaKind MediaKind,
    string ActualFilename, string RequiredCapability,
    IReadOnlyList<ProcessingCandidateSource> Sources, int PreviousAttempts);

public sealed record ProcessingPlanInput(
    Guid SourceFileId, string Role, string FullPath, string Filename,
    long Size, DateTimeOffset LastWriteTimeUtc, string Sha256);

public sealed record ProcessingPlanDefinition(
    Guid Id, Guid JobId, Guid InboxItemId, string ProcessorKey,
    string ProcessorVersion, string ProcessorDisplayName, string Capability,
    string OutputFilename,
    string WorkDirectoryRelativePath, IReadOnlyList<string> StepKeys,
    IReadOnlyList<ProcessingPlanInput> Inputs);

public sealed record PlannedProcessing(Guid JobId, int Attempt, ProcessingPlanDefinition Plan);
public sealed record QueueProcessingResult(ProcessingJobRecord Job, bool Created);
public sealed record ProcessingProgressUpdate(string StepKey, int Percent);

public sealed record MediaStreamInfo(string CodecType, TimeSpan? Duration);
public sealed record MediaToolAvailability(
    bool Available, string? Version, string? ErrorCode);
public sealed record MediaProbeResult(TimeSpan Duration, IReadOnlyList<MediaStreamInfo> Streams)
{
    public bool HasVideo => Streams.Any(stream => stream.CodecType == "video");
    public bool HasAudio => Streams.Any(stream => stream.CodecType == "audio");
}

public sealed record FfmpegRequest(
    IReadOnlyList<string> Arguments, string OutputPath, TimeSpan ExpectedDuration);
public sealed record FfmpegResult(int ExitCode, string DiagnosticTail);

public sealed record ProcessingWorkspacePaths(
    string RootDirectory, string PartialOutputPath, string FinalOutputPath,
    string FinalOutputRelativePath);

public sealed record ProcessedOutputDraft(
    string FullPath, string RelativePath, long Size,
    DateTimeOffset LastWriteTimeUtc, string Sha256,
    TimeSpan Duration, bool HasVideo, bool HasAudio);

public sealed record ProcessedOutputRecord(
    Guid JobId, Guid InboxItemId, string FullPath, string RelativePath,
    long Size, DateTimeOffset LastWriteTimeUtc, string Sha256,
    TimeSpan Duration, bool HasVideo, bool HasAudio, bool IsValid);

public sealed record ValidatedProcessingOutput(
    ProcessedOutputRecord Output,
    IReadOnlyList<ProcessingPlanInput> Inputs);

public sealed record ProcessingJobRecord(
    Guid Id, Guid InboxItemId, int Attempt, ProcessingJobStatus Status,
    string? CurrentStepKey, int ProgressPercent, string? ErrorCode,
    bool IsRetryable, string CorrelationId, ProcessingPlanDefinition Plan,
    ProcessedOutputRecord? Output);

public class ProcessingException : Exception
{
    public ProcessingException(string errorCode, bool isRetryable)
        : base(errorCode)
    {
        ErrorCode = errorCode;
        IsRetryable = isRetryable;
    }
    public string ErrorCode { get; }
    public bool IsRetryable { get; }
}

public sealed class ProcessingPlanRejectedException : ProcessingException
{
    public ProcessingPlanRejectedException(string errorCode)
        : base(errorCode, false) { }
}

public sealed class ProcessingCapabilityUnavailableException : ProcessingException
{
    public ProcessingCapabilityUnavailableException()
        : base("processing.capability_unavailable", false) { }
}

public sealed class MediaToolException : ProcessingException
{
    public MediaToolException(
        string errorCode, string diagnosticTail, bool isRetryable = true)
        : base(errorCode, isRetryable) => DiagnosticTail = diagnosticTail;
    public string DiagnosticTail { get; }
}

public sealed record ProcessingExecutionContext(
    ProcessingJobRecord Job, LibraryFolders Folders);

public interface IMediaProcessor
{
    MediaProcessorDescriptor Descriptor { get; }
    ProcessingPlanDefinition CreatePlan(Guid jobId, ProcessingCandidate candidate);
    Task<ProcessedOutputDraft> ExecuteAsync(
        ProcessingExecutionContext context,
        IProgress<ProcessingProgressUpdate> progress,
        CancellationToken cancellationToken);
}

public interface IMediaProcessorRegistry
{
    IMediaProcessor Resolve(string capability);
}

public interface IProcessingPlanner
{
    PlannedProcessing Create(ProcessingCandidate candidate);
}

public interface IProcessingStore
{
    Task<ProcessingCandidate?> LoadCandidateAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
    Task<ProcessingJobRecord?> FindActiveAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
    Task<QueueProcessingResult> QueueAsync(
        PlannedProcessing planned, CancellationToken cancellationToken);
    Task<ProcessingJobRecord?> TryClaimNextAsync(CancellationToken cancellationToken);
    Task UpdateProgressAsync(
        Guid jobId, ProcessingProgressUpdate update, CancellationToken cancellationToken);
    Task CompleteAsync(
        Guid jobId, ProcessedOutputDraft output, CancellationToken cancellationToken);
    Task<bool> CancelQueuedAsync(Guid jobId, CancellationToken cancellationToken);
    Task InterruptAsync(
        Guid jobId, string errorCode, CancellationToken cancellationToken);
    Task FailAsync(
        Guid jobId, string errorCode, bool isRetryable,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ProcessingJobRecord>> InterruptRecoverableAsync(
        string errorCode, CancellationToken cancellationToken);
    Task<ValidatedProcessingOutput?> LoadValidatedOutputAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
    Task<Guid?> InvalidateForSourceChangeAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
}
~~~

Define tool/workspace methods explicitly:

~~~csharp
public interface IMediaTool
{
    Task<MediaToolAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
    Task<MediaProbeResult> ProbeAsync(string trustedPath, CancellationToken cancellationToken);
    Task<FfmpegResult> RunFfmpegAsync(
        FfmpegRequest request,
        IProgress<int> progress,
        CancellationToken cancellationToken);
}

public interface IProcessingWorkspace
{
    Task<ProcessingWorkspacePaths> PrepareAsync(
        Guid jobId, LibraryFolders folders, string outputFilename,
        CancellationToken cancellationToken);
    Task<string> PromoteVerifiedAsync(
        Guid jobId, LibraryFolders folders, ProcessingWorkspacePaths paths,
        CancellationToken cancellationToken);
    Task CleanupUnverifiedAsync(
        Guid jobId, LibraryFolders folders, CancellationToken cancellationToken);
}

public interface IProcessingFileVerifier
{
    Task<bool> MatchesAsync(
        LibraryFolders folders, ProcessingPlanInput expected,
        CancellationToken cancellationToken);
}
~~~

- [ ] **Step 5: Implement registry and planner validation**

`MediaProcessorRegistry` materializes processors once, validates non-empty key/version/capability, rejects duplicate keys, and orders same-capability processors by descending priority. If the two highest priorities tie, construction fails; otherwise `Resolve` returns the highest and throws `ProcessingCapabilityUnavailableException` when absent.

`ProcessingPlanner.Create` requires `RequiresProcessing`, a non-empty capability, at least one source, and non-null 64-character hexadecimal SHA-256 values. It creates fresh plan/job IDs, sets attempt to `PreviousAttempts + 1`, and delegates format-specific input roles/output name to the selected processor. The processor copies `Descriptor.DisplayName` into the immutable plan.

- [ ] **Step 6: Run Application tests and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaProcessorRegistryTests|FullyQualifiedName~ProcessingPlannerTests"
git add apps/media-desktop/src/TraceFold.Media.Application/Processing apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing
git commit -m "feat(media-desktop): define preprocessing contracts"
~~~

Expected: focused tests pass; Application still references only Domain.

---

### Task 3: Persist Plans, Attempts, Outputs, and Cleanup Manifests

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/ProcessingPlanRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/ProcessingPlanInputRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/ProcessingJobRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/ProcessedOutputRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/IngestCleanupEntryRow.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfProcessingStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/InboxItemRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/IngestOperationRow.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/MediaDesktopDbContext.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260823120000_Stage3ProcessingPipeline.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/20260823120000_Stage3ProcessingPipeline.Designer.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/Migrations/MediaDesktopDbContextModelSnapshot.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/ProcessingPersistenceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/Stage3MigrationTests.cs`

**Interfaces:**
- Implements: every `IProcessingStore` method from Task 2.
- Produces: SQLite tables `processing_plans`, `processing_plan_inputs`, `processing_jobs`, `processed_outputs`, and `ingest_cleanup_entries`.
- Produces: nullable unique `ProcessingJobRow.ActiveInboxItemId` and `ProcessedOutputRow.ActiveInboxItemId` ownership keys.

- [ ] **Step 1: Write failing migration and persistence tests**

~~~csharp
[Fact]
public async Task Stage3_migration_preserves_stage2_rows_and_adds_processing_tables()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAtMigrationAsync(
        "20260822090000_Stage2MediaLibrary");
    Guid itemId = await database.SeedRequiresProcessingItemAsync();

    await database.Context.Database.MigrateAsync(TestContext.Current.CancellationToken);

    Assert.Equal(InboxItemState.RequiresProcessing,
        await database.Context.InboxItems.Where(row => row.Id == itemId)
            .Select(row => row.State).SingleAsync());
    string[] tables = await database.ReadTableNamesAsync();
    Assert.Contains("processing_jobs", tables);
    Assert.Contains("processed_outputs", tables);
    Assert.Contains("ingest_cleanup_entries", tables);
}

[Fact]
public async Task Queue_is_idempotent_and_claims_only_one_job()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    ProcessingCandidate candidate = await database.SeedHashedPairAsync();
    PlannedProcessing planned = TestPlans.Create(candidate);
    var store = new EfProcessingStore(database.Factory);

    QueueProcessingResult first = await store.QueueAsync(planned, CancellationToken.None);
    QueueProcessingResult duplicate = await store.QueueAsync(planned, CancellationToken.None);
    ProcessingJobRecord? claimed = await store.TryClaimNextAsync(CancellationToken.None);
    ProcessingJobRecord? none = await store.TryClaimNextAsync(CancellationToken.None);

    Assert.True(first.Created);
    Assert.False(duplicate.Created);
    Assert.Equal(first.Job.Id, duplicate.Job.Id);
    Assert.Equal(first.Job.Id, claimed!.Id);
    Assert.Equal(ProcessingJobStatus.Running, claimed.Status);
    Assert.Null(none);
}
~~~

Also test progress monotonicity, successful output invalidating any older output, cancellation only from queued, and `InterruptRecoverableAsync` changing all stale queued/running rows plus their items to interrupted.

- [ ] **Step 2: Run focused Infrastructure tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcessingPersistenceTests|FullyQualifiedName~Stage3MigrationTests"
~~~

Expected: FAIL because rows, migration, and store do not exist.

- [ ] **Step 3: Add normalized rows and EF mappings**

Use these ownership fields:

~~~csharp
internal sealed class ProcessingJobRow
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public ProcessingPlanRow Plan { get; set; } = null!;
    public Guid InboxItemId { get; set; }
    public InboxItemRow InboxItem { get; set; } = null!;
    public Guid? ActiveInboxItemId { get; set; }
    public int Attempt { get; set; }
    public ProcessingJobStatus Status { get; set; }
    public string? CurrentStepKey { get; set; }
    public int ProgressPercent { get; set; }
    public string? ErrorCode { get; set; }
    public bool IsRetryable { get; set; }
    public required string CorrelationId { get; set; }
    public DateTimeOffset QueuedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ProcessedOutputRow? Output { get; set; }
}

internal sealed class ProcessedOutputRow
{
    public Guid JobId { get; set; }
    public ProcessingJobRow Job { get; set; } = null!;
    public Guid InboxItemId { get; set; }
    public Guid? ActiveInboxItemId { get; set; }
    public required string RelativePath { get; set; }
    public long Size { get; set; }
    public DateTimeOffset LastWriteTimeUtc { get; set; }
    public required string Sha256 { get; set; }
    public long DurationTicks { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public bool IsValid { get; set; }
}
~~~

`ProcessingPlanRow` stores processor key/version/display name/capability, output filename, work-relative path and creation time. `ProcessingPlanInputRow` stores source ID, role, snapshotted path/name/size/write time/SHA. `IngestCleanupEntryRow` stores operation ID, cleanup kind (`inbox_source` or `processing_output`), controlled path, size, SHA, and nullable completion time.

Map enum values as lowercase strings, paths to 4096 characters, names to 1024, keys/codes to 128, hashes to 64, and correlation IDs to 64. Add unique indexes on nullable active-item IDs and `(PlanId, Role)`.

- [ ] **Step 4: Implement transactional queue/claim/progress/success operations**

Use one SQLite transaction for every item/job paired transition:

~~~csharp
public async Task<QueueProcessingResult> QueueAsync(
    PlannedProcessing planned, CancellationToken cancellationToken)
{
    await using MediaDesktopDbContext context =
        await contextFactory.CreateDbContextAsync(cancellationToken);
    await using var transaction =
        await context.Database.BeginTransactionAsync(cancellationToken);
    ProcessingJobRow? existing = await context.ProcessingJobs
        .Include(row => row.Plan).ThenInclude(row => row.Inputs)
        .SingleOrDefaultAsync(
            row => row.ActiveInboxItemId == planned.Plan.InboxItemId,
            cancellationToken);
    if (existing is not null)
    {
        return new QueueProcessingResult(ToRecord(existing), Created: false);
    }

    InboxItemRow item = await context.InboxItems.SingleAsync(
        row => row.Id == planned.Plan.InboxItemId, cancellationToken);
    if (item.State is not (InboxItemState.RequiresProcessing
        or InboxItemState.ProcessingInterrupted
        or InboxItemState.Failed))
    {
        throw new InvalidOperationException("The inbox item cannot be queued.");
    }

    DateTimeOffset now = DateTimeOffset.UtcNow;
    var planRow = new ProcessingPlanRow
    {
        Id = planned.Plan.Id,
        InboxItemId = planned.Plan.InboxItemId,
        ProcessorKey = planned.Plan.ProcessorKey,
        ProcessorVersion = planned.Plan.ProcessorVersion,
        ProcessorDisplayName = planned.Plan.ProcessorDisplayName,
        Capability = planned.Plan.Capability,
        OutputFilename = planned.Plan.OutputFilename,
        WorkDirectoryRelativePath = planned.Plan.WorkDirectoryRelativePath,
        StepKeysJson = JsonSerializer.Serialize(planned.Plan.StepKeys),
        CreatedAtUtc = now
    };
    foreach (ProcessingPlanInput input in planned.Plan.Inputs)
    {
        planRow.Inputs.Add(new ProcessingPlanInputRow
        {
            Id = Guid.NewGuid(),
            PlanId = planRow.Id,
            Plan = planRow,
            SourceFileId = input.SourceFileId,
            Role = input.Role,
            FullPath = input.FullPath,
            Filename = input.Filename,
            Size = input.Size,
            LastWriteTimeUtc = input.LastWriteTimeUtc,
            Sha256 = input.Sha256
        });
    }
    var job = new ProcessingJobRow
    {
        Id = planned.JobId,
        PlanId = planRow.Id,
        Plan = planRow,
        InboxItemId = item.Id,
        InboxItem = item,
        ActiveInboxItemId = item.Id,
        Attempt = planned.Attempt,
        Status = ProcessingJobStatus.Queued,
        ProgressPercent = 0,
        CorrelationId = Guid.NewGuid().ToString("N"),
        QueuedAtUtc = now,
        UpdatedAtUtc = now
    };
    context.ProcessingPlans.Add(planRow);
    context.ProcessingJobs.Add(job);
    item.State = InboxItemState.ProcessingQueued;
    item.RequiredCapability = planned.Plan.Capability;
    item.ErrorCode = null;
    item.IsRetryable = false;
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return new QueueProcessingResult(ToRecord(job), Created: true);
}
~~~

`TryClaimNextAsync` selects the oldest `QueuedAtUtc`, then `Id`, changes job to `Running` and item to `Processing` in one transaction. Serialize claims with a private `SemaphoreSlim` because one process owns the queue; keep the database unique constraint as the second line of defense.

`UpdateProgressAsync` performs `Math.Max(existing, incoming)`, rejects non-running jobs, and updates step/progress atomically. `CompleteAsync` invalidates any prior active output for the item, inserts the new output, clears both active ownership keys, sets job succeeded/100, and sets the item `ReadyToTag` with no required capability.

- [ ] **Step 5: Implement interruption, failure, output lookup, and source invalidation**

`CancelQueuedAsync` returns the item to `RequiresProcessing`. `InterruptAsync` accepts queued/running, preserves capability, and sets item `ProcessingInterrupted`. `FailAsync` sets item `Failed`, preserves capability, and records retryability. All terminal methods clear `ActiveInboxItemId`.

`InterruptRecoverableAsync` must update every `Queued`/`Running` row and item in one transaction without inspecting or killing a stored PID and return their materialized records for controlled workspace cleanup. `LoadValidatedOutputAsync` returns `ValidatedProcessingOutput` only for `IsValid && ActiveInboxItemId == inboxItemId`, including the immutable plan inputs. `InvalidateForSourceChangeAsync` sets active output invalid, terminalizes active jobs as interrupted, sets the item to `WaitingForStability` while preserving its metadata draft, and returns the running job ID when the in-memory controller must cancel it.

When materializing an output record, load the current configured work root, combine it with the persisted relative path, and pass the result through `ControlledPathGuard.RequireDescendant`. If settings are missing, the path escapes, or the file identity no longer matches, return no validated output and invalidate the row; never return an unguarded persisted path.

- [ ] **Step 6: Update migration support and run tests**

Extend `TestMediaDatabase` with migration/table helpers and update the model snapshot/designer. Then run:

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcessingPersistenceTests|FullyQualifiedName~Stage3MigrationTests"
~~~

Expected: focused persistence tests pass and Stage 2 seeded rows remain unchanged.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support
git commit -m "feat(media-desktop): persist preprocessing jobs"
~~~

---

### Task 4: Add a Guarded Per-Job Processing Workspace

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledProcessingWorkspace.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ProcessingFileVerifier.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledProcessingWorkspaceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ProcessingFileVerifierTests.cs`

**Interfaces:**
- Implements: `IProcessingWorkspace` from Task 2.
- Implements: `IProcessingFileVerifier` from Task 2.
- Consumes: `LibraryFolders`, `ControlledPathGuard`, and job/output identifiers from trusted application records.
- Produces: `.tracefold/processing/{job-id:N}/merged.partial.mp4` and a final filename from the processor plan.

- [ ] **Step 1: Write failing workspace safety tests**

~~~csharp
[Fact]
public async Task Prepare_creates_only_the_owned_job_directory()
{
    using TestMediaFolders folders = TestMediaFolders.Create();
    Guid jobId = Guid.NewGuid();
    var workspace = new ControlledProcessingWorkspace();

    ProcessingWorkspacePaths paths = await workspace.PrepareAsync(
        jobId, folders.Folders, "paired.mp4", CancellationToken.None);

    Assert.Equal(
        Path.Combine(folders.Work, ".tracefold", "processing", jobId.ToString("N")),
        paths.RootDirectory);
    Assert.Equal(Path.Combine(paths.RootDirectory, "merged.partial.mp4"),
        paths.PartialOutputPath);
    Assert.Equal(Path.Combine(paths.RootDirectory, "paired.mp4"),
        paths.FinalOutputPath);
}

[Theory]
[InlineData("..\\escape.mp4")]
[InlineData("sub\\escape.mp4")]
[InlineData("C:\\escape.mp4")]
public async Task Prepare_rejects_untrusted_output_names(string outputName)
{
    using TestMediaFolders folders = TestMediaFolders.Create();
    await Assert.ThrowsAsync<ControlledPathException>(() =>
        new ControlledProcessingWorkspace().PrepareAsync(
            Guid.NewGuid(), folders.Folders, outputName, CancellationToken.None));
}
~~~

Also test reparse-point refusal, `PromoteVerifiedAsync` using non-overwriting same-directory move, and `CleanupUnverifiedAsync` refusing a directory whose resolved ownership is not exactly the job ID.

Add verifier tests for missing files, size/write-time changes, SHA changes, direct inbox children containing spaces or Chinese characters, and reparse points. An identity mismatch returns `false`; an untrusted path throws `ControlledPathException`.

- [ ] **Step 2: Run the focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ControlledProcessingWorkspaceTests
~~~

Expected: FAIL because the workspace implementation does not exist.

- [ ] **Step 3: Implement guarded prepare/promote/cleanup**

Resolve the root with:

~~~csharp
string processingRoot = ControlledPathGuard.RequireDescendant(
    folders.WorkDirectory,
    Path.Combine(folders.WorkDirectory, ".tracefold", "processing"));
string jobRoot = ControlledPathGuard.RequireDescendant(
    processingRoot,
    Path.Combine(processingRoot, jobId.ToString("N")));
~~~

Require `outputFilename == Path.GetFileName(outputFilename)`, reject rooted names, invalid filename characters, `.` and `..`, and allow only `.mp4` for the first processor's requested output. Reuse the Stage 2 reparse traversal rule before creating, promoting, or recursively deleting.

`PrepareAsync` uses `Directory.CreateDirectory(jobRoot)` only after validating every ancestor and rejects pre-existing partial/final payloads. `PromoteVerifiedAsync` verifies both supplied paths equal the derived job paths and calls `File.Move(partial, final, overwrite: false)`. `CleanupUnverifiedAsync` recursively deletes only the exact derived job directory after a no-reparse-tree walk.

`ProcessingFileVerifier.MatchesAsync` requires the expected path to be a direct child of `folders.InboxDirectory`, checks size/write time before and after hashing, and compares uppercase SHA-256 through `IFileHasher`.

- [ ] **Step 4: Run tests and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ControlledProcessingWorkspaceTests|FullyQualifiedName~ProcessingFileVerifierTests|FullyQualifiedName~ControlledPathGuardTests"
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledProcessingWorkspace.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ProcessingFileVerifier.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledProcessingWorkspaceTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ProcessingFileVerifierTests.cs
git commit -m "feat(media-desktop): guard processing workspaces"
~~~

---

### Task 5: Implement Package-Local FFprobe and Owned FFmpeg Execution

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/MediaToolOptions.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/BundledMediaToolLocator.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/FfprobeJsonParser.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/FfmpegProgressParser.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/FfmpegMediaTool.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/BundledMediaToolLocatorTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/FfprobeJsonParserTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/FfmpegProgressParserTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/FfmpegMediaToolIntegrationTests.cs`

**Interfaces:**
- Implements: `IMediaTool` from Task 2.
- Produces: package-only resolution of `tools/ffmpeg/ffmpeg.exe` and `ffprobe.exe`.
- Consumes: `FfmpegRequest.Arguments`; never accepts a single shell command string.

Define `MediaToolOptions(string RootDirectory)` and `MediaToolLocation(string FfmpegPath, string FfprobePath)` in `MediaToolOptions.cs`; no constructor reads environment variables or `PATH`.

- [ ] **Step 1: Write failing pure parser and locator tests**

~~~csharp
[Fact]
public void Progress_parser_maps_out_time_to_clamped_percent()
{
    var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10));

    Assert.Equal(25, parser.Accept("out_time_us=2500000"));
    Assert.Equal(100, parser.Accept("progress=end"));
}

[Fact]
public void Probe_parser_requires_positive_duration_and_exposes_stream_types()
{
    const string json = """
        {"streams":[{"codec_type":"video"},{"codec_type":"audio"}],
         "format":{"duration":"2.500000"}}
        """;

    MediaProbeResult result = FfprobeJsonParser.Parse(json);

    Assert.Equal(TimeSpan.FromSeconds(2.5), result.Duration);
    Assert.True(result.HasVideo);
    Assert.True(result.HasAudio);
}

[Fact]
public void Locator_never_falls_back_to_path()
{
    using TestDirectory root = TestDirectory.Create();
    var locator = new BundledMediaToolLocator(new MediaToolOptions(root.Path));

    MediaToolLocation? location = locator.TryResolve();

    Assert.Null(location);
}
~~~

- [ ] **Step 2: Run parser/locator tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaToolLocatorTests|FullyQualifiedName~FfprobeJsonParserTests|FullyQualifiedName~FfmpegProgressParserTests"
~~~

Expected: FAIL because media-tool infrastructure does not exist.

- [ ] **Step 3: Implement package-local lookup and parsers**

`MediaToolOptions.RootDirectory` is the directory that directly contains `ffmpeg.exe` and `ffprobe.exe`. Production composition will pass `Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg")`; tests pass an explicit fixture directory.

`BundledMediaToolLocator.TryResolve()` uses fully qualified paths, rejects missing files and reparse points, and returns no fallback. `CheckAvailabilityAsync` runs package-local `ffmpeg.exe -version` with a 10-second linked timeout and returns `processing.tool_missing`, `processing.tool_unavailable`, or the first version line.

`FfprobeJsonParser` uses `System.Text.Json`, parses duration with invariant culture, requires a finite positive value, and normalizes stream types to lowercase. `FfmpegProgressParser` accepts `out_time_us`, ignores malformed lines, keeps progress monotonic, clamps normal updates to 0–99, and returns 100 only for `progress=end`.

- [ ] **Step 4: Write failing owned-process integration tests**

Use explicit test root `C:\Program Files\ffmpeg\bin` and the repository fixtures. Do not inspect `PATH`:

~~~csharp
[Fact]
public async Task Probe_reads_the_fixed_video_and_audio_fixtures()
{
    FfmpegMediaTool tool = TestMediaTools.CreateInstalled();
    MediaProbeResult video = await tool.ProbeAsync(
        TestMediaFiles.PairedMp4, TestContext.Current.CancellationToken);
    MediaProbeResult audio = await tool.ProbeAsync(
        TestMediaFiles.PairedM4a, TestContext.Current.CancellationToken);

    Assert.True(video.HasVideo);
    Assert.True(audio.HasAudio);
    Assert.True(video.Duration > TimeSpan.Zero);
    Assert.True(audio.Duration > TimeSpan.Zero);
}

[Fact]
public async Task Cancellation_kills_only_the_process_started_by_the_tool()
{
    FfmpegMediaTool tool = TestMediaTools.CreateInstalled();
    using var cancellation = new CancellationTokenSource();
    Task<FfmpegResult> run = tool.RunFfmpegAsync(
        TestMediaTools.LongRunningRequest(), new Progress<int>(), cancellation.Token);
    await TestMediaTools.WaitForOwnedProcessAsync();
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    Assert.Empty(TestMediaTools.GetStillRunningOwnedProcesses());
}
~~~

The long-running request must generate test-owned output under a `TestDirectory`; it must not touch user folders.

- [ ] **Step 5: Implement safe process execution**

Build every process with:

~~~csharp
var startInfo = new ProcessStartInfo
{
    FileName = location.FfmpegPath,
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true
};
foreach (string argument in request.Arguments)
{
    startInfo.ArgumentList.Add(argument);
}
startInfo.ArgumentList.Add("-progress");
startInfo.ArgumentList.Add("pipe:1");
startInfo.ArgumentList.Add("-nostats");
startInfo.ArgumentList.Add(request.OutputPath);
~~~

For FFmpeg, add every `request.Arguments`, then `-progress pipe:1 -nostats`, then the separately supplied `request.OutputPath` as shown above. Read stdout/stderr concurrently, retain at most the last 32 KiB of diagnostics, and report parsed progress. Register cancellation against the exact `Process` instance and call `Kill(entireProcessTree: true)` only when that instance has started and has not exited. Await process exit and both readers before returning or rethrowing cancellation.

For FFprobe use arguments `-v error -print_format json -show_streams -show_format <trustedPath>`, a 30-second linked timeout, and the same no-shell process discipline. A non-zero exit throws `MediaToolException("processing.probe_failed", diagnosticTail)`.

- [ ] **Step 6: Run media-tool tests and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MediaTool|FullyQualifiedName~FfprobeJsonParserTests|FullyQualifiedName~FfmpegProgressParserTests"
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools
git commit -m "feat(media-desktop): add owned ffmpeg adapter"
~~~

Expected: parsers, lookup, real fixture probing, and cancellation pass without using system `PATH`.

---

### Task 6: Implement the Built-In MP4/M4A Merge Processor

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/Mp4M4aMergeProcessor.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/Mp4M4aMergeProcessorTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration/Mp4M4aProcessingFlowTests.cs`

**Interfaces:**
- Implements: `IMediaProcessor`.
- Descriptor: key `mp4_m4a_merge`, version `1`, capability `mp4_m4a_merge`, priority `100`, display name `合并视频和音频`.
- Plan roles: exactly one `video` input from primary `.mp4` and one `audio` input from `.m4a`.
- Produces: verified `ProcessedOutputDraft` only after input and output probes pass.

- [ ] **Step 1: Write failing plan-validation tests**

~~~csharp
[Fact]
public void CreatePlan_maps_one_primary_mp4_and_one_m4a()
{
    ProcessingCandidate candidate = TestCandidates.Paired();
    var processor = new Mp4M4aMergeProcessor(
        new FakeMediaTool(), new FakeWorkspace(),
        new FakeProcessingFileVerifier(), new FakeFileHasher());

    ProcessingPlanDefinition plan = processor.CreatePlan(Guid.NewGuid(), candidate);

    Assert.Equal(["video", "audio"], plan.Inputs.Select(input => input.Role));
    Assert.Equal(candidate.ActualFilename, plan.OutputFilename);
    Assert.Equal(["check_sources", "merge", "verify_output"], plan.StepKeys);
}

[Theory]
[InlineData(2, 1)]
[InlineData(1, 0)]
[InlineData(0, 1)]
public void CreatePlan_rejects_ambiguous_or_incomplete_pairs(int mp4Count, int m4aCount)
{
    ProcessingCandidate candidate = TestCandidates.PairCounts(mp4Count, m4aCount);
    Assert.Throws<ProcessingPlanRejectedException>(() =>
        CreateProcessor().CreatePlan(Guid.NewGuid(), candidate));
}
~~~

Although Stage 2 normally groups one pair, the processor must reject duplicates rather than silently choose one.

- [ ] **Step 2: Write failing execution/verification tests with fakes**

~~~csharp
[Fact]
public async Task Execute_reports_fixed_step_ranges_and_returns_verified_output()
{
    var updates = new List<ProcessingProgressUpdate>();
    FakeMediaTool tools = FakeMediaTool.SuccessfulMerge();
    Mp4M4aMergeProcessor processor = CreateProcessor(tools);
    ProcessingExecutionContext context = TestContexts.PairedJob();

    ProcessedOutputDraft output = await processor.ExecuteAsync(
        context, new Progress<ProcessingProgressUpdate>(updates.Add),
        TestContext.Current.CancellationToken);

    Assert.Contains(updates, update => update.StepKey == "check_sources" && update.Percent == 5);
    Assert.Contains(updates, update => update.StepKey == "merge" && update.Percent is >= 5 and <= 95);
    Assert.Contains(updates, update => update.StepKey == "verify_output" && update.Percent == 99);
    Assert.True(output.HasVideo);
    Assert.True(output.HasAudio);
}

[Theory]
[InlineData(true, false)]
[InlineData(false, true)]
public async Task Execute_rejects_output_missing_either_stream(bool video, bool audio)
{
    Mp4M4aMergeProcessor processor = CreateProcessor(
        FakeMediaTool.WithOutputStreams(video, audio));

    ProcessingException error = await Assert.ThrowsAsync<ProcessingException>(() =>
        processor.ExecuteAsync(TestContexts.PairedJob(), new Progress<ProcessingProgressUpdate>(),
            TestContext.Current.CancellationToken));

    Assert.Equal("processing.output_streams_invalid", error.ErrorCode);
}
~~~

- [ ] **Step 3: Run Application tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Mp4M4aMergeProcessorTests
~~~

Expected: FAIL because the processor does not exist.

- [ ] **Step 4: Implement planning and execution**

Build the FFmpeg request with this exact argument order:

~~~csharp
string[] arguments =
[
    "-nostdin", "-n",
    "-i", video.FullPath,
    "-i", audio.FullPath,
    "-map", "0:v:0",
    "-map", "1:a:0",
    "-c", "copy",
    "-movflags", "+faststart"
];
var request = new FfmpegRequest(
    arguments,
    workspace.PartialOutputPath,
    videoProbe.Duration > audioProbe.Duration
        ? videoProbe.Duration
        : audioProbe.Duration);
~~~

Execution sequence:

1. Report `check_sources/0`; use `IProcessingFileVerifier` to verify both files still match path, size, write time and SHA; probe input roles; require video on MP4, audio on M4A, and positive durations; report `check_sources/5`.
2. Prepare the job workspace. Run FFmpeg with expected duration `Max(video.Duration, audio.Duration)`. Map tool progress `0–99` to overall `5–95` and report step `merge`.
3. Probe the partial output, require video/audio/positive duration, recheck both source identities, calculate output SHA, and report `verify_output/99`.
4. Promote the partial file and return a draft using the promoted full/relative path.
5. On cancellation or failure call `CleanupUnverifiedAsync`; never delete either input.

Catch known probe/runner/workspace exceptions and map them to stable `ProcessingException` codes; do not expose raw tool diagnostics as the user message.

- [ ] **Step 5: Add a real fixture integration test**

Copy repository fixtures into `TestMediaFolders.Inbox`, build a real plan, execute with `FfmpegMediaTool` rooted explicitly at `C:\Program Files\ffmpeg\bin`, and assert:

~~~csharp
Assert.True(File.Exists(output.FullPath));
Assert.True(output.HasVideo);
Assert.True(output.HasAudio);
Assert.True(File.Exists(videoSource));
Assert.True(File.Exists(audioSource));
Assert.StartsWith(Path.Combine(folders.Work, ".tracefold", "processing"),
    output.FullPath, StringComparison.OrdinalIgnoreCase);
~~~

The test must use `finally`/`TestMediaFolders.Dispose()` to clean only its owned root.

- [ ] **Step 6: Run processor tests and commit**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Mp4M4aMergeProcessorTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Mp4M4aProcessingFlowTests
git add apps/media-desktop/src/TraceFold.Media.Application/Processing/Mp4M4aMergeProcessor.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/Mp4M4aMergeProcessorTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration/Mp4M4aProcessingFlowTests.cs
git commit -m "feat(media-desktop): merge mp4 m4a pairs"
~~~

Expected: fake behavior and real stream-copy integration pass; original fixture copies remain.

---

### Task 7: Orchestrate Manual Commands, Serial Execution, Recovery, and Source Invalidation

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingQueueSignal.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingExecutionController.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingCommandService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingInvalidationService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingJobExecutor.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingProgressBuffer.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingRecoveryService.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/ProcessingQueueWorker.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxScanContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Intake/ScanInboxService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxScanStore.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/ProcessingCommandServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/ProcessingJobExecutorTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Processing/ProcessingRecoveryServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Workers/ProcessingQueueWorkerTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/ScanInboxServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/EfInboxScanStoreTests.cs`

**Interfaces:**
- Produces: `IProcessingCommandService.StartAsync`, `CancelQueuedAsync`, `StopAsync`, and `RetryAsync`.
- Produces: `IProcessingJobExecutor.ExecuteNextAsync(CancellationToken)` returning `bool` (`true` when a job was claimed).
- Produces: channel-backed `IProcessingQueueSignal.Signal()` / `WaitAsync(CancellationToken)`.
- Produces: `IProcessingInvalidationService.InvalidateAsync(Guid inboxItemId, CancellationToken)`.
- Consumes: Task 2 planner/processor contracts, Task 3 store, Task 4 workspace, Stage 2 `IMediaOperationCoordinator`, `IInboxActivityNotifier`, `IFileHasher`, and directory settings.

- [ ] **Step 1: Write failing manual-command tests**

~~~csharp
[Fact]
public async Task Start_hashes_sources_queues_once_and_signals_the_worker()
{
    var store = FakeProcessingStore.WithCandidate(TestCandidates.UnhashedPair());
    var signal = new FakeProcessingQueueSignal();
    var service = CreateCommandService(store, signal);

    ProcessingCommandResult first = await service.StartAsync(
        store.ItemId, TestContext.Current.CancellationToken);
    ProcessingCommandResult duplicate = await service.StartAsync(
        store.ItemId, TestContext.Current.CancellationToken);

    Assert.Equal(ProcessingCommandOutcome.Queued, first.Outcome);
    Assert.Equal(ProcessingCommandOutcome.AlreadyActive, duplicate.Outcome);
    Assert.Equal(1, store.QueueCount);
    Assert.Equal(1, signal.SignalCount);
    Assert.All(store.QueuedPlan!.Plan.Inputs,
        input => Assert.Matches("^[0-9A-F]{64}$", input.Sha256));
}

[Fact]
public async Task Scan_and_startup_never_call_start_processing()
{
    await scanner.ScanAsync(ScanTrigger.Manual, CancellationToken.None);
    await recovery.ReconcileAsync(CancellationToken.None);

    Assert.Equal(0, processingCommands.StartCalls);
    Assert.Equal(0, processingStore.QueueCount);
}

[Fact]
public async Task Stop_running_cancels_only_the_current_job()
{
    var controller = new FakeProcessingExecutionController();
    var store = FakeProcessingStore.WithRunningJob(Guid.NewGuid());
    var service = CreateCommandService(store, controller: controller);

    ProcessingCommandResult result = await service.StopAsync(
        store.ItemId, TestContext.Current.CancellationToken);

    Assert.Equal(ProcessingCommandOutcome.StopRequested, result.Outcome);
    Assert.Equal([store.ActiveJobId], controller.CancelledJobIds);
}
~~~

Define results exactly:

~~~csharp
public enum ProcessingCommandOutcome
{
    Queued,
    AlreadyActive,
    Cancelled,
    StopRequested,
    NotEligible,
    SourceMissing,
    SourceChanged,
    ToolUnavailable,
    ShuttingDown,
    Failed
}

public sealed record ProcessingCommandResult(
    ProcessingCommandOutcome Outcome, Guid? JobId, string Message);

public interface IProcessingCommandService
{
    Task<ProcessingCommandResult> StartAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
    Task<ProcessingCommandResult> CancelQueuedAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
    Task<ProcessingCommandResult> StopAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
    Task<ProcessingCommandResult> RetryAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
}

public interface IProcessingQueueSignal
{
    void Signal();
    Task WaitAsync(CancellationToken cancellationToken);
}

public interface IProcessingJobExecutor
{
    Task<bool> ExecuteNextAsync(CancellationToken cancellationToken);
}

public interface IProcessingRecoveryService
{
    Task ReconcileAsync(CancellationToken cancellationToken);
}

public interface IProcessingExecutionController
{
    IDisposable Register(Guid jobId, CancellationTokenSource source);
    bool Cancel(Guid jobId);
}

public interface IProcessingInvalidationService
{
    Task InvalidateAsync(Guid inboxItemId, CancellationToken cancellationToken);
}
~~~

- [ ] **Step 2: Write failing executor and recovery tests**

~~~csharp
[Fact]
public async Task Executor_runs_one_claimed_job_and_persists_monotonic_progress()
{
    var store = FakeProcessingStore.WithTwoQueuedJobs();
    ProcessingJobExecutor executor = CreateExecutor(store);

    bool claimed = await executor.ExecuteNextAsync(
        TestContext.Current.CancellationToken);

    Assert.True(claimed);
    Assert.Single(store.CompletedJobs);
    Assert.Single(store.StillQueuedJobs);
    Assert.True(store.ProgressUpdates.SequenceEqual(
        store.ProgressUpdates.OrderBy(update => update.Percent)));
}

[Fact]
public async Task Executor_marks_cancelled_execution_interrupted_then_next_call_claims_next()
{
    var store = FakeProcessingStore.WithTwoQueuedJobs();
    ProcessingJobExecutor executor = CreateExecutor(
        store, processor: FakeProcessor.CancelledByController());

    await executor.ExecuteNextAsync(TestContext.Current.CancellationToken);
    bool secondClaimed = await executor.ExecuteNextAsync(
        TestContext.Current.CancellationToken);

    Assert.Single(store.InterruptedJobs);
    Assert.True(secondClaimed);
}

[Fact]
public async Task Recovery_interrupts_stale_jobs_and_cleans_only_their_unverified_workspaces()
{
    var store = FakeProcessingStore.WithStaleQueuedAndRunningJobs();
    var workspace = new FakeWorkspace();
    var recovery = new ProcessingRecoveryService(store, workspace, settingsStore, notifier);

    await recovery.ReconcileAsync(TestContext.Current.CancellationToken);

    Assert.Equal(2, store.InterruptedJobs.Count);
    Assert.Equal(store.InterruptedJobs.Select(job => job.Id), workspace.CleanedJobIds);
    Assert.Empty(store.QueuedJobs);
}
~~~

- [ ] **Step 3: Run focused tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcessingCommandServiceTests|FullyQualifiedName~ProcessingJobExecutorTests|FullyQualifiedName~ProcessingRecoveryServiceTests"
~~~

Expected: FAIL because command/executor/recovery services do not exist.

- [ ] **Step 4: Implement command service and queue signal**

`StartAsync` uses `IMediaOperationCoordinator.Begin`, checks `IMediaTool.CheckAvailabilityAsync`, loads the current candidate, verifies each source still matches the persisted size/write time, hashes every source, creates a candidate copy with uppercase hashes, calls the planner, queues it, publishes `InboxActivityKind.ItemChanged`, and signals the worker only when a new row was created.

`RetryAsync` calls the same private start path but accepts only `ProcessingInterrupted` or processing `Failed`; it always creates new plan/job IDs. `CancelQueuedAsync` terminalizes only a queued job and does not create a work directory. `StopAsync` reads the active running job and asks `IProcessingExecutionController.Cancel(jobId)`; the executor owns final persistence.

Use a bounded channel with capacity 1 and `DropWrite`, matching `InboxActivityNotifier`, so many queue commands coalesce into one wake-up without losing persisted work.

- [ ] **Step 5: Implement execution controller and job executor**

`ProcessingExecutionController` stores one linked `CancellationTokenSource` per running job. `Register(jobId, source)` returns an `IDisposable` that removes only the same source instance. `Cancel(jobId)` cancels that source and never looks up or kills a PID.

Executor sequence:

~~~csharp
ProcessingJobRecord? job = await store.TryClaimNextAsync(cancellationToken);
if (job is null) return false;
using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
using IDisposable registration = controller.Register(job.Id, jobCancellation);
IProgress<ProcessingProgressUpdate> progress = new Progress<ProcessingProgressUpdate>(
    update => progressBuffer.Publish(job.Id, update));
LibraryFolders? folders = null;
try
{
    IMediaProcessor processor = registry.Resolve(job.Plan.Capability);
    if (processor.Descriptor.Key != job.Plan.ProcessorKey
        || processor.Descriptor.Version != job.Plan.ProcessorVersion)
        throw new ProcessingException("processing.processor_version_unavailable", false);
    folders = await RequireFoldersAsync(jobCancellation.Token);
    ProcessedOutputDraft output = await processor.ExecuteAsync(
        new ProcessingExecutionContext(job, folders), progress, jobCancellation.Token);
    await progressBuffer.FlushAsync(job.Id, jobCancellation.Token);
    await store.CompleteAsync(job.Id, output, jobCancellation.Token);
}
catch (OperationCanceledException)
{
    if (folders is not null)
        await TryCleanupAsync(job.Id, folders);
    await store.InterruptAsync(job.Id, "processing.interrupted", CancellationToken.None);
}
catch (ProcessingException error)
{
    if (folders is not null)
        await TryCleanupAsync(job.Id, folders);
    await store.FailAsync(job.Id, error.ErrorCode, error.IsRetryable, CancellationToken.None);
}
catch (Exception error)
{
    logger.LogError(error,
        "ProcessingJobFailed JobId={JobId} CorrelationId={CorrelationId}",
        job.Id, job.CorrelationId);
    if (folders is not null)
        await TryCleanupAsync(job.Id, folders);
    await store.FailAsync(
        job.Id, "processing.internal_error", isRetryable: true,
        CancellationToken.None);
}
finally
{
    activity.Publish(InboxActivityKind.ItemChanged);
}
return true;
~~~

Implement `TryCleanupAsync` as a private helper that catches/logs workspace cleanup failure so the terminal job state is still persisted. Implement `ProcessingProgressBuffer` as a per-job single-flight async writer rather than launching fire-and-forget database tasks from `Progress<T>`. It retains the highest update, writes in order, and `FlushAsync` awaits the last write before completion.

- [ ] **Step 6: Implement recovery and one serial hosted worker**

`ProcessingRecoveryService.ReconcileAsync` calls `InterruptRecoverableAsync`, obtains validated directory settings, cleans only returned job IDs, publishes one item-changed event, and does not signal the queue.

`ProcessingQueueWorker` waits for `IMediaRuntimeGate`, then loops:

~~~csharp
while (!stoppingToken.IsCancellationRequested)
{
    if (!await executor.ExecuteNextAsync(stoppingToken))
    {
        await signal.WaitAsync(stoppingToken);
    }
}
~~~

Override `StopAsync`: cancel the base worker, await it, then call `InterruptRecoverableAsync(CancellationToken.None)` so queued rows also become interrupted. Never start a second worker instance.

- [ ] **Step 7: Invalidate processed state when inbox sources change**

Extend `ScanPersistenceResult` with `IReadOnlyList<Guid> SourceChangedItemIds`. `EfInboxScanStore` adds an item ID only when a source is added/removed or size/write time changes, not merely when stability count increments.

When an unchanged `RequiresProcessing` group belongs to a `ReadyToTag` item with one active valid `ProcessedOutputRow`, preserve `ReadyToTag` instead of reapplying `RequiresProcessing`. Use:

~~~csharp
bool hasValidatedOutput = item.ProcessingJobs.Any(job =>
    job.Output is { IsValid: true, ActiveInboxItemId: not null });
bool scanManagedState = item.State is InboxItemState.WaitingForStability
    or InboxItemState.Analyzing or InboxItemState.RequiresProcessing
    or InboxItemState.SourceMissing
    || (item.State == InboxItemState.ReadyToTag && !hasValidatedOutput);
~~~

After persistence, `ScanInboxService` calls `IProcessingInvalidationService.InvalidateAsync` for each changed item. The service invalidates store output/state and asks the execution controller to cancel any returned running job ID. It does not know MP4/M4A rules.

- [ ] **Step 8: Run command, scan, persistence, and worker tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Processing|FullyQualifiedName~ScanInboxServiceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProcessingQueueWorkerTests|FullyQualifiedName~EfInboxScanStoreTests"
~~~

Expected: only explicit start queues, FIFO is serial, stop advances the queue, startup never resumes, and source changes invalidate/cancel.

- [ ] **Step 9: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Processing apps/media-desktop/src/TraceFold.Media.Application/Intake apps/media-desktop/src/TraceFold.Media.Infrastructure/Workers/ProcessingQueueWorker.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxScanStore.cs apps/media-desktop/tests
git commit -m "feat(media-desktop): execute preprocessing queue"
~~~

---

### Task 8: Project Processing State into Queries, Preview, and the Three-Column UI

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Intake/InboxQueryContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfPreviewPathStore.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxCardViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxInspectorViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxPage.xaml.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Intake/InboxQueryServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/PreviewPathStoreTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/InboxViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaCardViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/MediaWorkspaceStructureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`

**Interfaces:**
- Produces: `ProcessingSummaryDto` on `InboxCardDto` and `InboxDetailsDto`.
- Consumes: `IProcessingCommandService`; the ViewModel never sees `IMediaTool`, EF rows, or paths.
- Preview rule: active validated processed output first; otherwise the registered primary inbox source.

- [ ] **Step 1: Write failing query and preview tests**

~~~csharp
[Fact]
public async Task Query_projects_processing_step_progress_and_actions()
{
    await SeedRunningJobAsync(step: "merge", progress: 63);

    InboxDetailsDto details = (await store.GetDetailsAsync(
        itemId, TestContext.Current.CancellationToken))!;

    Assert.Equal("合并音视频", details.Processing!.StepText);
    Assert.Equal(63, details.Processing.ProgressPercent);
    Assert.True(details.AllowedActions.HasFlag(InboxAction.StopProcessing));
}

[Fact]
public async Task Preview_uses_validated_output_after_processing_success()
{
    string output = await SeedSuccessfulProcessedOutputAsync();

    PreviewPathRecord preview = (await store.FindAsync(
        new MetadataOwner(MetadataOwnerKind.InboxItem, itemId),
        TestContext.Current.CancellationToken))!;

    Assert.Equal(output, preview.TrustedFullPath);
}
~~~

Define:

~~~csharp
public sealed record ProcessingSummaryDto(
    Guid JobId, ProcessingJobStatus Status, string ProcessorDisplayName,
    string StepText, int ProgressPercent, string? ErrorCode, bool IsRetryable);
~~~

- [ ] **Step 2: Write failing ViewModel command tests**

~~~csharp
[Fact]
public async Task Required_item_exposes_start_and_does_not_ingest()
{
    InboxViewModel viewModel = CreateViewModel(
        details: TestDetails.RequiresProcessing());
    await viewModel.LoadAsync(CancellationToken.None);
    await viewModel.SelectAsync(viewModel.Items.Single(), CancellationToken.None);

    Assert.True(viewModel.StartProcessingCommand.CanExecute(null));
    Assert.False(viewModel.IngestCommand.CanExecute(null));
}

[Fact]
public async Task Running_item_exposes_stop_and_card_progress()
{
    InboxViewModel viewModel = CreateViewModel(
        details: TestDetails.Processing("merge", 63));
    await viewModel.LoadAsync(CancellationToken.None);

    InboxCardViewModel card = viewModel.Items.Single();
    Assert.Equal("合并音视频 63%", card.ProgressText);
    Assert.True(viewModel.StopProcessingCommand.CanExecute(null));
}
~~~

- [ ] **Step 3: Run query/ViewModel tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaCardViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PreviewPathStoreTests
~~~

Expected: FAIL because processing summary, commands, and output preview projection are absent.

- [ ] **Step 4: Extend query projection and controlled preview selection**

Map step keys to user text in one Application presentation mapper:

~~~csharp
"check_sources" => "检查源文件",
"merge" => "合并音视频",
"verify_output" => "验证输出",
_ => "正在处理"
~~~

Map visible states to `排队中`, `处理中`, and `已中断`. Add state priorities and allowed actions matching Domain. Query the newest job by attempt and project the processor display name persisted in its immutable plan; do not instantiate processors inside EF queries or duplicate a capability-to-label catalog.

In `EfPreviewPathStore.FindInboxAsync`, join the active valid output and configured work root. If present, resolve `Path.Combine(workRoot, RelativePath)` with `ControlledPathGuard.RequireDescendant`; otherwise resolve the primary source as Stage 2 did. Return only registered paths.

- [ ] **Step 5: Add commands and bound state without a task center**

Inject `IProcessingCommandService` into `InboxViewModel` and add four relay commands. Each command snapshots `SelectedItem.Id`, sets `IsBusy` only around the command call, maps the result to a Chinese message, reloads cards/details, and never calls ingest automatically.

Command predicates:

~~~csharp
private bool CanStartProcessing() => !IsBusy
    && Inspector.AllowedActions.HasFlag(InboxAction.StartProcessing);
private bool CanCancelProcessing() => !IsBusy
    && Inspector.AllowedActions.HasFlag(InboxAction.CancelProcessing);
private bool CanStopProcessing() => !IsBusy
    && Inspector.AllowedActions.HasFlag(InboxAction.StopProcessing);
private bool CanRetryProcessing() => !IsBusy
    && Inspector.AllowedActions.HasFlag(InboxAction.RetryProcessing);
~~~

Add filters for `ProcessingQueued`, `Processing`, and `ProcessingInterrupted`. Card XAML shows a compact step/percentage text and `ProgressBar` only while queued/running. Inspector XAML shows processor name, status, current step, numeric progress, error code, and exactly one state-appropriate processing button near the existing ingest button. Keep the current left navigation, center gallery and right inspector widths; do not add a route or task-center page.

Use stable automation IDs: `InboxStartProcessingButton`, `InboxCancelProcessingButton`, `InboxStopProcessingButton`, `InboxRetryProcessingButton`, `InboxProcessingProgress`, and `InboxProcessingStep`.

- [ ] **Step 6: Run query, preview, and Desktop tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InboxQueryServiceTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PreviewPathStoreTests|FullyQualifiedName~ProcessingPersistenceTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InboxViewModelTests|FullyQualifiedName~MediaCardViewModelTests|FullyQualifiedName~MediaWorkspaceStructureTests"
~~~

Expected: status/progress/buttons project correctly and successful processed video resolves for preview.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Intake apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfInboxQueryStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfPreviewPathStore.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages apps/media-desktop/tests
git commit -m "feat(media-desktop): expose processing controls"
~~~

---

### Task 9: Generalize Formal Ingest to Validated Inputs and Multi-Source Cleanup

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestContracts.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestInputResolver.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestCleanupService.cs`
- Move: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/OrdinaryIngestService.cs` → `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Ingest/IngestRecoveryService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Storage/FileStorageContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledMediaStorage.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs`
- Move: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/OrdinaryIngestServiceTests.cs` → `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestInputResolverTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestCleanupServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Ingest/IngestRecoveryServiceTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Filesystem/ControlledMediaStorageTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Persistence/IngestPersistenceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Integration/ProcessedMediaIngestFlowTests.cs`

**Interfaces:**
- Replaces: `IOrdinaryIngestService` with `IIngestService` using the same `IngestAsync(Guid, CancellationToken)` signature.
- Produces: `PreparedIngestInput` and `IngestCleanupFile` records.
- Produces: persisted per-entry cleanup completion and a reusable `IngestCleanupService.CleanupAsync`.
- Preserves: one conflict, staging, commit and asset transaction path for ordinary and processed media.

- [ ] **Step 1: Write failing input-resolution tests**

~~~csharp
[Fact]
public async Task Ordinary_ready_item_uses_its_single_primary_source()
{
    IngestCandidate candidate = TestIngestCandidates.Ordinary();
    PreparedIngestInput input = await resolver.ResolveAsync(
        candidate, TestContext.Current.CancellationToken);

    Assert.Equal(candidate.Sources.Single().FullPath, input.FullPath);
    Assert.Single(input.CleanupFiles);
    Assert.Equal(IngestCleanupKind.InboxSource, input.CleanupFiles[0].Kind);
}

[Fact]
public async Task Processed_ready_item_uses_output_and_keeps_both_originals_in_cleanup()
{
    IngestCandidate candidate = TestIngestCandidates.ProcessedPair();
    processingStore.ValidatedOutput = TestOutputs.Valid(candidate.InboxItemId);

    PreparedIngestInput input = await resolver.ResolveAsync(
        candidate, TestContext.Current.CancellationToken);

    Assert.Equal(processingStore.ValidatedOutput.Output.FullPath, input.FullPath);
    Assert.Equal(3, input.CleanupFiles.Count);
    Assert.Equal(2, input.CleanupFiles.Count(file =>
        file.Kind == IngestCleanupKind.InboxSource));
    Assert.Single(input.CleanupFiles.Where(file =>
        file.Kind == IngestCleanupKind.ProcessingOutput));
}

[Fact]
public async Task Changed_original_invalidates_processed_input_before_ingest()
{
    IngestCandidate candidate = TestIngestCandidates.ProcessedPair();
    processingStore.ValidatedOutput = TestOutputs.Valid(candidate.InboxItemId);
    fileHasher.Override(candidate.Sources[1].FullPath, new string('F', 64));

    IngestInputException error = await Assert.ThrowsAsync<IngestInputException>(() =>
        resolver.ResolveAsync(candidate, TestContext.Current.CancellationToken));

    Assert.Equal("ingest.source_changed", error.ErrorCode);
}
~~~

Define:

~~~csharp
public enum IngestCleanupKind { InboxSource, ProcessingOutput }
public sealed record IngestCleanupFile(
    IngestCleanupKind Kind, string FullPath, long Size, string Sha256);
public sealed record PreparedIngestInput(
    IngestCleanupKind Kind, string FullPath, long Size, DateTimeOffset LastWriteTimeUtc,
    string Sha256, IReadOnlyList<IngestCleanupFile> CleanupFiles);

public sealed record IngestCleanupEntryRecord(
    Guid Id, Guid OperationId, IngestCleanupFile File, bool Completed);
public sealed record CleanupResult(bool Completed, string? ErrorCode);

public sealed class IngestInputException : InvalidOperationException
{
    public IngestInputException(string errorCode) : base(errorCode) =>
        ErrorCode = errorCode;
    public string ErrorCode { get; }
}

public interface IIngestInputResolver
{
    Task<PreparedIngestInput> ResolveAsync(
        IngestCandidate candidate, CancellationToken cancellationToken);
}

public interface IIngestCleanupService
{
    Task<CleanupResult> CleanupAsync(
        IngestOperationRecord operation, LibraryFolders folders,
        CancellationToken cancellationToken);
}

public interface IIngestService
{
    Task<IngestResult> IngestAsync(
        Guid inboxItemId, CancellationToken cancellationToken);
}
~~~

- [ ] **Step 2: Write failing cleanup-manifest tests**

~~~csharp
[Fact]
public async Task Cleanup_marks_each_entry_only_after_deleted_or_already_missing()
{
    IngestOperationRecord operation = TestOperations.WithThreeCleanupEntries();
    storage.Results.Enqueue(SourceDeleteOutcome.Deleted);
    storage.Results.Enqueue(SourceDeleteOutcome.SourceMissing);
    storage.Results.Enqueue(SourceDeleteOutcome.SourceChanged);

    CleanupResult result = await cleanup.CleanupAsync(
        operation, folders, TestContext.Current.CancellationToken);

    Assert.False(result.Completed);
    Assert.Equal(2, store.CompletedCleanupEntryIds.Count);
    Assert.Equal("ingest.cleanup_source_changed", result.ErrorCode);
}
~~~

- [ ] **Step 3: Run focused ingest tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~IngestInputResolverTests|FullyQualifiedName~IngestCleanupServiceTests|FullyQualifiedName~IngestServiceTests"
~~~

Expected: FAIL because prepared inputs, cleanup manifests, and generalized service do not exist.

- [ ] **Step 4: Implement validated input selection**

`IngestInputResolver` accepts only `ReadyToTag`. For one primary source and no validated output, verify file snapshot and calculate SHA as Stage 2. For a multi-source candidate, require one active valid processed output, verify output path/size/write time/SHA, verify output stream flags, and re-hash every original against the plan inputs returned with `LoadValidatedOutputAsync`.

On any mismatch call `IProcessingInvalidationService.InvalidateAsync` and throw a stable `IngestInputException`; never stage or delete. The output's physical filename remains `candidate.ActualFilename`.

- [ ] **Step 5: Persist and execute a per-file cleanup manifest**

Extend `IngestPreparation` with `PreparedIngestInput Input` and add `IngestPreparation.From(IngestCandidate, PreparedIngestInput)` to copy media/metadata plus the prepared input. Keep `SourcePath/Size/Hash` columns as the final staged input for recovery compatibility. `EfIngestStore.CreateAsync` inserts one `IngestCleanupEntryRow` per cleanup file in the same transaction as the operation.

Add store methods:

~~~csharp
Task MarkCleanupEntryCompletedAsync(
    Guid operationId, Guid entryId, CancellationToken cancellationToken);
Task<IReadOnlyList<IngestCleanupEntryRecord>> LoadCleanupEntriesAsync(
    Guid operationId, CancellationToken cancellationToken);
~~~

Change staging to consume the prepared input so the storage layer can select the correct controlled root, and add guarded cleanup:

~~~csharp
Task<StagedFile> StageAsync(
    Guid operationId, LibraryFolders folders, PreparedIngestInput input,
    CancellationToken cancellationToken);
Task<SourceDeleteResult> DeleteCleanupFileIfMatchesAsync(
    LibraryFolders folders, IngestCleanupFile cleanupFile,
    CancellationToken cancellationToken);
~~~

`StageAsync` requires a direct inbox child for `InboxSource` and a descendant of `work/.tracefold/processing` for `ProcessingOutput`; it then reuses the same copy/flush/hash staging path. For cleanup, `InboxSource` uses the existing quarantine/rehash/restore algorithm. `ProcessingOutput` requires the guarded processing descendant, rejects reparse points, moves to a same-directory unique quarantine name, rehashes, then deletes. On hash/size mismatch restore where possible and return `SourceChanged`. After a processing output is removed, delete its parent job directory only when it is empty, its name parses as the owning job GUID, and its parent is exactly the controlled processing root.

`IngestCleanupService` processes incomplete entries in persisted order, marks `Deleted` and `SourceMissing` complete, stops on `SourceChanged` or exception, and only then calls `CleanupTemporaryFilesAsync` and `IIngestStore.CompleteAsync`.

- [ ] **Step 6: Refactor the orchestration without duplicating the flow**

Rename the service/interface to `IngestService`/`IIngestService`. Replace the current `Sources.Count == 1` validation/hash block with:

~~~csharp
PreparedIngestInput input = await inputResolver.ResolveAsync(
    candidate, operationToken);
ConflictKind? conflict = await ResolveConflictAsync(
    folders, candidate.ActualFilename, input.Sha256, operationToken);
IngestOperationRecord operation = await store.CreateAsync(
    IngestPreparation.From(candidate, input), operationToken);
StagedFile staged = await storage.StageAsync(
    operation.Id, folders, input, operationToken);
~~~

Keep the existing commit and asset transaction. After `VerifyCommittedAsync`, call `IngestCleanupService.CleanupAsync`; return `CleanupPending` unless every entry and temporary artifact is complete. Do not branch on `mp4_m4a_merge` anywhere in ingest.

Update `IngestRecoveryService` to call the same cleanup service for `DatabaseCommitted` and `CleanupPending`. `Created`/`Staged` recovery validates the final input snapshot, not an assumed inbox source.

- [ ] **Step 7: Add a real processed-ingest integration test**

The test must scan copied fixtures, queue/execute the real processor, call formal ingest, then assert:

~~~csharp
Assert.False(File.Exists(inboxMp4));
Assert.False(File.Exists(inboxM4a));
Assert.True(File.Exists(Path.Combine(folders.Library, "paired.mp4")));
Assert.False(File.Exists(processedOutputPath));
Assert.Equal(InboxItemState.Completed, await database.ReadItemStateAsync(itemId));
~~~

Add a second test that forces formal commit failure and asserts both originals and the processed output remain.

- [ ] **Step 8: Run ingest, storage, persistence, and integration tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Ingest"
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ControlledMediaStorageTests|FullyQualifiedName~IngestPersistenceTests|FullyQualifiedName~ProcessedMediaIngestFlowTests|FullyQualifiedName~OrdinaryMediaFlowTests"
~~~

Expected: ordinary ingest remains green; processed success cleans three inputs only after commit; failures preserve originals.

- [ ] **Step 9: Commit**

~~~powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Ingest apps/media-desktop/src/TraceFold.Media.Application/Storage apps/media-desktop/src/TraceFold.Media.Infrastructure/Filesystem/ControlledMediaStorage.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfIngestStore.cs apps/media-desktop/tests
git commit -m "feat(media-desktop): ingest validated processed media"
~~~

---

### Task 10: Wire Startup, Shutdown, DI, Tool Health, and Logging Privacy

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Processing/ProcessingDiagnosticsService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/Startup/StartupCoordinator.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Lifecycle/AppShutdownCoordinator.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/App.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsViewModel.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/InboxViewModel.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Startup/StartupCoordinatorTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Lifecycle/AppShutdownCoordinatorTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsViewModelTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Logging/Stage3LoggingPrivacyTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/ArchitectureTests.cs`

**Interfaces:**
- Registers: exactly one built-in processor and one serial hosted worker.
- Startup order: database migration → directory validation → ingest recovery → processing recovery → runtime gate open.
- Shutdown order: metadata flush → stop new media commands/drain foreground operations → host stops worker/owned FFmpeg → dispose host.
- Tool unavailable is a processing diagnostic, not an application startup failure.
- Produces: `IProcessingDiagnosticsService.CheckAsync(CancellationToken)` returning availability/version/error without exposing tool paths to ViewModels.

- [ ] **Step 1: Write failing startup/shutdown/DI tests**

~~~csharp
[Fact]
public async Task Startup_interrupts_processing_before_opening_runtime_gate()
{
    await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

    Assert.Equal(
        ["database", "directories", "ingest-recovery", "processing-recovery", "gate-open"],
        calls);
}

[Fact]
public async Task Shutdown_stops_the_host_after_media_drain()
{
    await shutdown.StopAsync(TestContext.Current.CancellationToken);

    Assert.Equal(["metadata", "media-drain", "host-stop"], calls);
}

[Fact]
public void Composition_has_one_processor_and_one_processing_worker()
{
    using IHost host = DesktopCompositionRoot.BuildHost(testPaths);
    Assert.Single(host.Services.GetServices<IMediaProcessor>());
    Assert.IsType<Mp4M4aMergeProcessor>(
        host.Services.GetServices<IMediaProcessor>().Single());
}
~~~

Architecture tests must assert Domain has no Application/Infrastructure/Desktop references, Application has no Infrastructure/Desktop references, and no ViewModel constructor accepts `IMediaTool`, `IProcessingStore`, `MediaDesktopDbContext`, or `Process`.

- [ ] **Step 2: Run focused lifecycle tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~StartupCoordinatorTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AppShutdownCoordinatorTests|FullyQualifiedName~SettingsViewModelTests"
~~~

Expected: FAIL until processing recovery, commands, worker and tool health are composed.

- [ ] **Step 3: Register services with one clear owner**

Application registrations:

~~~csharp
services.AddSingleton<IMediaProcessor, Mp4M4aMergeProcessor>();
services.AddSingleton<IMediaProcessorRegistry, MediaProcessorRegistry>();
services.AddSingleton<IProcessingPlanner, ProcessingPlanner>();
services.AddSingleton<IProcessingQueueSignal, ProcessingQueueSignal>();
services.AddSingleton<IProcessingExecutionController, ProcessingExecutionController>();
services.AddSingleton<IProcessingCommandService, ProcessingCommandService>();
services.AddSingleton<IProcessingInvalidationService, ProcessingInvalidationService>();
services.AddSingleton<IProcessingJobExecutor, ProcessingJobExecutor>();
services.AddSingleton<IProcessingRecoveryService, ProcessingRecoveryService>();
services.AddSingleton<IProcessingDiagnosticsService, ProcessingDiagnosticsService>();
services.AddSingleton<IIngestInputResolver, IngestInputResolver>();
services.AddSingleton<IIngestCleanupService, IngestCleanupService>();
services.AddSingleton<IIngestService, IngestService>();
~~~

Infrastructure registrations:

~~~csharp
services.AddSingleton<IProcessingStore, EfProcessingStore>();
services.AddSingleton<IProcessingWorkspace, ControlledProcessingWorkspace>();
services.AddSingleton<IProcessingFileVerifier, ProcessingFileVerifier>();
services.AddSingleton(new MediaToolOptions(
    Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg")));
services.AddSingleton<BundledMediaToolLocator>();
services.AddSingleton<IMediaTool, FfmpegMediaTool>();
services.AddHostedService<ProcessingQueueWorker>();
~~~

Remove `IOrdinaryIngestService` registration and update all consumers to `IIngestService`. Do not register a second worker or dynamic processor discovery.

- [ ] **Step 4: Integrate startup, shutdown, and tool diagnostics**

Define the diagnostic boundary exactly as follows; its implementation delegates to `IMediaTool.CheckAvailabilityAsync` without exposing executable paths:

~~~csharp
public sealed record ProcessingDiagnosticsDto(
    bool Available, string? Version, string? ErrorCode);

public interface IProcessingDiagnosticsService
{
    Task<ProcessingDiagnosticsDto> CheckAsync(CancellationToken cancellationToken);
}
~~~

Inject `IProcessingRecoveryService` into `StartupCoordinator` and call it after ingest recovery but before `runtimeGate.Open()`. Tool availability is not a startup gate; Settings displays `可用：<version>` or `不可用：<stable code>`.

`InboxViewModel` loads the same diagnostic into `ProcessingAvailable`. Add `&& ProcessingAvailable` to `CanStartProcessing` and `CanRetryProcessing`; tool absence therefore disables those commands while refresh, metadata, ordinary ingest, and library browsing remain enabled. Add a ViewModel test that returns `processing.tool_missing` and asserts start/retry are disabled but `RefreshCommand` remains enabled.

Keep `AppShutdownCoordinator`'s existing outer ordering. Host shutdown cancels `ProcessingQueueWorker`, whose `StopAsync` interrupts running/queued jobs and awaits owned-process termination. Increase the default shutdown timeout from 5 to 15 seconds and test the timeout path; do not force-kill `TraceFold.Media.exe` from inside the app.

- [ ] **Step 5: Add privacy and stable-error tests**

Capture logs from a failing fake FFmpeg run whose source filename and metadata contain a sentinel. Assert logs include correlation ID, processor key, tool version, and stable error code, but not metadata display name/author/tag text or binary contents. Controlled source/work paths may appear; raw FFmpeg diagnostic output must be truncated to 32 KiB.

- [ ] **Step 6: Run lifecycle, architecture, and privacy tests**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~StartupCoordinatorTests
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AppShutdownCoordinatorTests|FullyQualifiedName~SettingsViewModelTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Architecture.Tests/TraceFold.Media.Architecture.Tests.csproj -c Release --no-restore
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Stage3LoggingPrivacyTests
~~~

Expected: recovery precedes workers, shutdown owns child termination, tool failure is isolated, and dependency boundaries remain intact.

- [ ] **Step 7: Commit**

~~~powershell
git add apps/media-desktop/src apps/media-desktop/tests
git commit -m "feat(media-desktop): integrate processing lifecycle"
~~~

---

### Task 11: Bundle Verified FFmpeg Tools and Add a Stage 3 Package Probe

**Files:**
- Create: `apps/media-desktop/tools/ffmpeg/win-x64/manifest.json`
- Create: `apps/media-desktop/tools/ffmpeg/win-x64/THIRD-PARTY-NOTICES.txt`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/TraceFold.Media.Desktop.csproj`
- Move: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage2PackageProbe.cs` → `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage3PackageProbe.cs`
- Move: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage2PackageProbeOptions.cs` → `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/Stage3PackageProbeOptions.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Program.cs`
- Move: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage2PackageProbeOptionsTests.cs` → `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage3PackageProbeOptionsTests.cs`
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Modify: `apps/media-desktop/README.md`
- Create: `docs/media-desktop-stage3-test-guide.md`

**Interfaces:**
- Publish parameter: mandatory `-FfmpegSourceRoot` pointing at the controlled Gyan distribution root.
- Package paths: `tools/ffmpeg/ffmpeg.exe`, `ffprobe.exe`, `LICENSE`, `README.txt`, and `THIRD-PARTY-NOTICES.txt`.
- Probe arguments: `--stage3-package-probe-root` and `--stage3-package-probe-token`.
- Output: `dist/TraceFold-MediaDesktop-Stage3-win-x64.zip` and SHA-256.

- [ ] **Step 1: Add the pinned manifest and notices**

Create `manifest.json` with exact values:

~~~json
{
  "distribution": "FFmpeg 64-bit static Windows build from www.gyan.dev",
  "version": "2025-06-23-git-e6298e0759-full_build-www.gyan.dev",
  "license": "GPL v3",
  "sourceCommit": "https://github.com/FFmpeg/FFmpeg/commit/e6298e0759",
  "files": {
    "ffmpeg.exe": {
      "size": 153995264,
      "sha256": "D5A3CDFF713F3DE9C3EC6CD0DD1BCD046BB0EB97712BD32D340E39D1F88DEF05"
    },
    "ffprobe.exe": {
      "size": 153799680,
      "sha256": "E6C17A4AE25E03275ADB00F664448D5D37ADDB88AE82372BAF1DC8BBDC7C3BB2"
    }
  }
}
~~~

The notice identifies FFmpeg/Gyan, version, GPL v3, source commit, and states that the package includes the unmodified distribution `LICENSE` and `README.txt`. Do not claim a different license or omit the upstream source reference.

- [ ] **Step 2: Write failing package/probe tests**

Update the existing option-parser tests to Stage 3 names and add an allowed fixture directory containing exactly `paired.mp4` and `paired.m4a` with hashes:

~~~text
paired.mp4 B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3
paired.m4a 187F3BE0AB2C07C6A967840BEBFA0EB1646227B52179387F74AB4D8BBC64AAC1
~~~

`Stage3PackageProbeOptions.Parse` rejects any extra file, reparse point, nested root, changed fixture hash, missing owner marker, or pre-existing data/inbox/library/work/result entry.

The headless flow test must assert scan → manual queue command → worker/executor completion → video/audio probe → formal ingest, then verify originals and work output are gone and `library/paired.mp4` exists.

- [ ] **Step 3: Run probe tests and verify red**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Stage3PackageProbeOptionsTests
~~~

Expected: FAIL until renamed probe and Stage 3 flow are implemented.

- [ ] **Step 4: Implement the controlled Stage 3 headless probe**

The smoke script creates a direct temp child, writes owner token, creates `fixtures`, and copies only the two repository fixtures. The probe validates root ownership and both hashes, creates its four test directories, copies fixtures into inbox, builds and starts a host with package-local tools, runs startup, scans until stable, and calls `IProcessingCommandService.StartAsync`. The registered serial worker consumes the signal; the probe polls the persisted job/details with a 30-second deadline until it reaches `Succeeded`, then resolves/probes preview and calls `IIngestService.IngestAsync`. It never calls the executor concurrently with the hosted worker.

Write `stage3-probe-result.json` with success, job ID, actual filename, output stream booleans, and stable error code. The normal UI path never calls the probe. `Program` returns 2 for invalid probe arguments and never opens a window in probe mode.

- [ ] **Step 5: Make publish import and verify tools before promotion**

Change the script parameter and package constants:

~~~powershell
param([Parameter(Mandatory)][string] $FfmpegSourceRoot)
$packageName = 'TraceFold-MediaDesktop-Stage3-win-x64'
~~~

Resolve the source root once; require it is fully qualified, non-reparse, and contains `bin\ffmpeg.exe`, `bin\ffprobe.exe`, `LICENSE`, and `README.txt`. Read the repository manifest, verify executable sizes and `Get-FileHash -Algorithm SHA256`, then copy files into staging `tools\ffmpeg`. Copy the repository notice. Fail before touching the current `dist` package on any mismatch.

Remove FFmpeg/FFprobe from the forbidden-executable list and replace that rule with exact relative-path, size, hash, and license checks. Keep PDB, user data, source, database, logs, work directories, Python, Node, and browsers forbidden.

Add this conditional content mapping to the Desktop project and pass `-p:TraceFoldFfmpegSourceRoot=$FfmpegSourceRoot` to publish:

~~~xml
<ItemGroup Condition="'$(TraceFoldFfmpegSourceRoot)' != ''">
  <Content Include="$(TraceFoldFfmpegSourceRoot)\bin\ffmpeg.exe"
           Link="tools\ffmpeg\ffmpeg.exe"
           CopyToOutputDirectory="PreserveNewest"
           CopyToPublishDirectory="PreserveNewest" />
  <Content Include="$(TraceFoldFfmpegSourceRoot)\bin\ffprobe.exe"
           Link="tools\ffmpeg\ffprobe.exe"
           CopyToOutputDirectory="PreserveNewest"
           CopyToPublishDirectory="PreserveNewest" />
  <Content Include="$(TraceFoldFfmpegSourceRoot)\LICENSE"
           Link="tools\ffmpeg\LICENSE"
           CopyToPublishDirectory="PreserveNewest" />
  <Content Include="$(TraceFoldFfmpegSourceRoot)\README.txt"
           Link="tools\ffmpeg\README.txt"
           CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
~~~

The publish script copies the repository notice after `dotnet publish`. Source builds without the property remain valid with processing disabled.

- [ ] **Step 6: Extend offline smoke without relying on system PATH**

Before launching the package, set the child process environment `PATH` to `$env:SystemRoot\System32` only. Confirm the probe result reports a tool path under `$packageRoot\tools\ffmpeg`; reject any path outside the package.

After normal-window single-instance checks, run the Stage 3 probe, assert merged formal media contains video/audio, verify both source fixtures and processing workspace were cleaned, close the main window, and assert no package `TraceFold.Media` or package-owned `ffmpeg` process remains.

Use the existing owner-marker and direct-temp-child cleanup rules. Never enumerate then delete through another shell.

- [ ] **Step 7: Run tests, publish, smoke, and inspect the archive**

~~~powershell
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage3-win-x64'
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage3-win-x64.zip'
~~~

Expected: all tests pass; smoke reports native window, single instance, package-local tools, processed flow, clean exit, and no browser/child-process residue.

- [ ] **Step 8: Update README and manual test guide**

README documents Stage 3 behavior, source verification, explicit FFmpeg source parameter, package paths and commands. The Chinese test guide follows the 15 design-spec acceptance steps, includes expected result after each action, and begins with a warning to use copied fixtures and isolated directories only.

- [ ] **Step 9: Commit**

~~~powershell
git add apps/media-desktop/tools apps/media-desktop/src/TraceFold.Media.Desktop apps/media-desktop/tests/TraceFold.Media.Desktop.Tests scripts apps/media-desktop/README.md docs/media-desktop-stage3-test-guide.md
git commit -m "build(media-desktop): publish stage three package"
~~~

Do not add `dist`, FFmpeg binaries, generated databases, logs, work directories, or probe results to Git.

---

### Task 12: Run Full Regression, Acceptance Evidence, and Final Review

**Files:**
- Modify only if verification finds a defect: files owned by the task that introduced it.
- Create: `docs/media-desktop-stage3-acceptance.md`

**Interfaces:**
- Consumes: all previous task deliverables.
- Produces: one reproducible acceptance record with commands, counts, package path, ZIP SHA-256, tool version/hashes, and unresolved limitations.

- [ ] **Step 1: Run clean Release restore, build, and all tests**

~~~powershell
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source apps/media-desktop/.packages --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore --disable-build-servers
~~~

Expected: 0 warnings, 0 errors, and every test passes. Record the exact new total in the acceptance document; do not predict it here.

- [ ] **Step 2: Run real fixture and shutdown-focused tests again**

~~~powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Mp4M4a|FullyQualifiedName~ProcessedMediaIngestFlowTests|FullyQualifiedName~ProcessingQueueWorkerTests"
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~AppShutdownCoordinatorTests|FullyQualifiedName~Stage3PackageProbeOptionsTests"
~~~

Expected: real output has video/audio, failure keeps originals, stop/close leaves no owned child, and package probe is safe.

- [ ] **Step 3: Rebuild and smoke the final offline package**

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-media-desktop.ps1 -FfmpegSourceRoot 'C:\Program Files\ffmpeg'
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-media-desktop-package.ps1 -PackageDirectory 'dist\TraceFold-MediaDesktop-Stage3-win-x64'
Get-FileHash -Algorithm SHA256 'dist\TraceFold-MediaDesktop-Stage3-win-x64.zip'
~~~

Expected: package smoke passes with restricted `PATH`, package-local tool paths, and no residual app/FFmpeg process.

- [ ] **Step 4: Inspect repository and package hygiene**

~~~powershell
git status --short
git diff --check main...HEAD
Get-ChildItem -LiteralPath 'dist\TraceFold-MediaDesktop-Stage3-win-x64' -Recurse -File |
    Select-Object FullName,Length
~~~

Confirm no source, PDB, user database, media fixture, logs, work files, Python, Node, browser executable, or tool outside `tools\ffmpeg` exists in the package. Confirm Git contains only source/docs/manifests/notices, not `dist` or 300 MiB FFmpeg binaries.

- [ ] **Step 5: Perform final code review against the spec**

Review these explicit invariants:

- No scan, startup, or metadata path queues processing.
- No ViewModel or ingest service contains MP4/M4A merge argument logic.
- Only one worker claims jobs, and queue ordering is deterministic.
- Stop/close terminate only the exact owned process instance.
- Startup does not auto-resume queued/running rows.
- Output validation checks video, audio and positive duration.
- Pre-ingest revalidates both originals and the output.
- No original is deleted before committed target verification.
- Cleanup is per-entry, path guarded and idempotent.
- Stage 2 ordinary media and conflict behavior remain green.
- Production never searches `PATH` or downloads tools.

If review finds a defect, add a focused failing test, make the smallest fix, rerun the affected project and the full suite, and commit with `fix(media-desktop): <specific behavior>`.

- [ ] **Step 6: Write acceptance evidence and commit**

`docs/media-desktop-stage3-acceptance.md` records:

- branch/commit under test;
- exact Release build and test counts;
- focused integration outcomes;
- package directory and ZIP path;
- ZIP SHA-256;
- FFmpeg/FFprobe version and pinned hashes;
- restricted-`PATH` smoke result;
- manual checklist path;
- known limitation that no clean VM was available, with clean Windows 11 x64 verification retained as a future release gate.

~~~powershell
git add docs/media-desktop-stage3-acceptance.md
git commit -m "docs(media-desktop): record stage three acceptance"
git status --short --branch
~~~

Expected: working tree is clean and the feature branch is ready for the branch-finishing workflow.

## Spec Coverage

- Processor contract, explicit registration, planner and no dynamic plugins: Tasks 2, 6, 10.
- Immutable plans, persistent FIFO queue and single execution: Tasks 3 and 7.
- Manual start, cancel, stop, interrupt, retry and no auto-resume: Tasks 1, 7, 8, 10.
- Current step plus numeric progress without a task center: Tasks 6 and 8.
- Guarded work directory and no source deletion by processors: Tasks 4 and 6.
- Package-local owned FFmpeg/FFprobe, progress and stream validation: Tasks 5, 6, 11.
- Source/output identity invalidation: Tasks 3, 6, 7, 9.
- Processed preview and actual/display name semantics: Task 8.
- One formal ingest flow and post-commit MP4/M4A cleanup: Task 9.
- Tool-unavailable isolation, startup/shutdown and logging privacy: Task 10.
- Offline ZIP, licenses, restricted-`PATH` proof and human checklist: Tasks 11 and 12.
- Full Stage 2 regression and acceptance evidence: Task 12.

## Execution Checkpoints

1. After Task 3: inspect the migration and uniqueness/ownership rules before adding external processes.
2. After Task 6: review real FFmpeg arguments, process ownership, output validation, and work-path guards.
3. After Task 7: manually review queue ordering, stop semantics, source invalidation and startup non-resume.
4. After Task 9: run the processed-ingest failure test and inspect that both originals survive every pre-commit failure.
5. After Task 11: inspect tool hashes/licenses and run package smoke before any human media test.
6. After Task 12: use `superpowers:requesting-code-review`, then `superpowers:finishing-a-development-branch`; do not merge or publish elsewhere without the user's choice.
