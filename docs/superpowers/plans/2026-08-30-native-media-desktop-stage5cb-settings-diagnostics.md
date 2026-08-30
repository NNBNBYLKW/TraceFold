# Native Media Desktop Stage 5C-B Settings Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a low-friction, read-only system diagnostics tab to Settings that checks the active folders, SQLite database, bundled FFmpeg/FFprobe, background-work counts, and runtime/log environment; isolates failures; and produces a privacy-safe summary without changing media, settings, jobs, or business data.

**Architecture:** Application owns immutable diagnostic contracts, deterministic orchestration, severity aggregation, stable failures, and the pure redacted formatter. Infrastructure owns five explicitly registered probes and their filesystem, SQLite, process, task-count, manifest, and runtime readers. Desktop owns session-only presentation state, one-run-at-a-time lifecycle, native tabs, focus, clipboard, and registered log-folder actions. Package diagnostics reuse only public application services inside an ownership-guarded temporary tree.

**Tech Stack:** .NET 10 LTS, C# 14, WinUI 3, Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2, EF Core SQLite 10.0.11, Microsoft.Extensions.Hosting 10.0.11, Serilog, xUnit v3, bundled FFmpeg/FFprobe 2025-06-23-git-e6298e0759-full_build-www.gyan.dev, PowerShell packaging scripts.

**Spec:** `docs/superpowers/specs/2026-08-30-native-media-desktop-stage5cb-settings-diagnostics-design.md`

## Global Constraints

- Execute in the existing isolated `codex/native-media-desktop-stage5cb-settings-diagnostics` worktree at `T:\TraceFold5CB`. The approved specification commit is `50bcc6b`; implementation starts from the committed plan immediately following it.
- Target Windows 11 x64. Keep `TraceFold.Media.Desktop` unpackaged, self-contained, single-instance, native-windowed, and fully offline.
- Do not browse, download dependencies, update pinned packages, or run `git pull`. Restore only from `T:\Windows\Documents\GitHub\TraceFold\apps\media-desktop\.packages` with failed remote sources ignored.
- Diagnostics are observational. Do not add a migration, history table, repair command, cleanup command, task retry, media read/hash, or settings mutation. The only filesystem write allowed by a diagnostic is a dedicated random zero-length write probe that is removed in the same operation.
- Keep all five probes independent. A probe exception or timeout becomes a stable diagnostic item while successful results from other probes remain visible.
- Production probe timeouts are 5 seconds for folders/storage, processing tools, and runtime/logs; 15 seconds for database and background work; and 20 seconds for the complete run.
- UI may display full local paths. Copied summaries and structured log properties must not contain paths, usernames, hostnames, media names, metadata, hashes, database/log filenames, task/asset/taxonomy IDs, arbitrary exception messages, or probe-file names.
- Background diagnostics return counts only: queued, running, interrupted, failed, and ingest recovery/cleanup pending. They do not return task or file rows.
- `SettingsViewModel` remains responsible for folders, scan preferences, and the existing brief processing-tool status. Put diagnostic lifecycle and presentation in a separate session-scoped `SystemDiagnosticsViewModel`.
- Desktop never imports EF Core, SQLite, raw filesystem mutation, or arbitrary process paths for diagnostics. Safe actions are limited to the typed actions `NavigateToFolderSettings`, `OpenRegisteredLogFolder`, and `CopyErrorCode`.
- Keep the Stage 5C-A package intact. Stage 5C-B uses `StageCheckpoint 5C-B`, informational version `4.0.0-stage5cb+<commit>`, and separate `dist/TraceFold-MediaDesktop-Stage5C-B-win-x64(.zip)` paths.
- Package probes use only direct children of an ownership-marked system temporary root. Reject a wrong token, wrong root prefix, non-empty unexpected root, or reparse point before writing or cleanup.
- The current 810 passing tests are the minimum regression baseline. Every task follows red-green-refactor, runs its focused tests, and ends in one reviewable commit.
- Do not commit `dist`, package probe output, generated databases/logs, temporary probe files, FFmpeg binaries, `.packages`, or ignored `.superpowers` artifacts. Do not create an acceptance result until the user reports the manual checklist.

## Before Task 1

- [ ] Verify the isolated worktree, plan commit, cache junction, and cleanliness:

```powershell
Set-Location 'T:\TraceFold5CB'
git status --short --branch
git rev-parse HEAD
git rev-parse --show-toplevel
git rev-parse --git-dir
git rev-parse --git-common-dir
Get-Item apps/media-desktop/.packages | Select-Object FullName,LinkType,Target
```

Expected: the branch is `codex/native-media-desktop-stage5cb-settings-diagnostics`, the tree is clean, the plan is committed on top of `50bcc6b`, the worktree Git directory differs from the common Git directory, and `.packages` targets the repository-owned cache.

- [ ] Restore offline and prove the 810-test baseline before editing:

```powershell
$traceFoldPackageCache = 'T:\Windows\Documents\GitHub\TraceFold\apps\media-desktop\.packages'
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source $traceFoldPackageCache -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers --ignore-failed-sources
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
```

Expected: restore uses the local cache, Release build reports 0 warnings and 0 errors, and exactly 810 tests pass. Stop and report an unexplained baseline change before implementation.

## File and Responsibility Map

- `Application/Diagnostics/SystemDiagnosticsContracts.cs`: immutable enums, facts, safe actions, items, snapshots, options, probe/service/formatter ports, and background summary port.
- `Application/Diagnostics/SystemDiagnosticsService.cs`: concurrent orchestration, timeout and exception isolation, cancellation, deterministic sorting, overall status, and privacy-safe structured logging.
- `Application/Diagnostics/DiagnosticSummaryFormatter.cs`: deterministic text output that admits only `Shareable` facts.
- `Infrastructure/Diagnostics/FolderAndDiskDiagnosticProbe.cs`: saved-folder validation, safe write check, overlap detection, per-volume capacity caching, and folder/storage items.
- `Infrastructure/Diagnostics/DatabaseDiagnosticProbe.cs`: read-only open/migration/`PRAGMA quick_check` observations.
- `Infrastructure/Diagnostics/ProcessingToolDiagnosticProbe.cs`: separate controlled `-version` runs for package-local FFmpeg and FFprobe.
- `Infrastructure/Diagnostics/BackgroundWorkDiagnosticProbe.cs`: count-only projection over processing and ingest operation states.
- `Infrastructure/Diagnostics/RuntimeAndLogDiagnosticProbe.cs`: assembly/runtime/OS/manifest/log-root facts and safe log write check.
- `Desktop/Services/DiagnosticDesktopServices.cs`: clipboard and registered-log-root adapters; never accepts an arbitrary path from a result item.
- `Desktop/Pages/SystemDiagnosticsViewModel.cs`: session snapshot, groups/items/actions, single-run gate, refresh/cancel, copy, and navigation event.
- `Desktop/Pages/SettingsPage.xaml(.cs)`: two native settings tabs, diagnostics cards, lifecycle activation, and folder-target focus.
- `Desktop/Diagnostics/PackageProbe/Stage5CBDiagnosticsProbeScenario.cs`: guarded, read-only package proof.
- `scripts/publish-media-desktop.ps1` and `scripts/smoke-media-desktop-package.ps1`: independent Stage 5C-B packaging and evidence.
- `docs/media-desktop-stage5cb-diagnostics-test-guide.md`: offline manual acceptance guide; no acceptance record until user results exist.

---

## Phase A — Application Contracts, Redaction, and Orchestration

### Task 1: Define the Diagnostic Contract Boundary

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Diagnostics/SystemDiagnosticsContracts.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/SystemDiagnosticsContractTests.cs`

**Interfaces:**
- Produces: `SystemDiagnosticSeverity`, `SystemDiagnosticCategory`, `SystemDiagnosticFactSensitivity`, `SystemDiagnosticActionKind`, and `SystemDiagnosticActionTarget`.
- Produces: immutable `SystemDiagnosticFact`, `SystemDiagnosticAction`, `SystemDiagnosticItem`, `SystemDiagnosticsSnapshot`, `SystemDiagnosticProbeDescriptor`, and `SystemDiagnosticsOptions`.
- Produces: `ISystemDiagnosticProbe`, `ISystemDiagnosticsService`, `IDiagnosticSummaryFormatter`, `BackgroundWorkSummary`, and `IBackgroundWorkSummaryStore`.
- Enforces: stable IDs/keys, nonnegative ordering, typed safe actions, and production timeout defaults.

- [ ] **Step 1: Write failing contract-invariant tests**

```csharp
[Fact]
public void Production_options_match_the_approved_timeout_budget()
{
    SystemDiagnosticsOptions options = SystemDiagnosticsOptions.Production;

    Assert.Equal(TimeSpan.FromSeconds(5), options.FastProbeTimeout);
    Assert.Equal(TimeSpan.FromSeconds(15), options.DatabaseProbeTimeout);
    Assert.Equal(TimeSpan.FromSeconds(15), options.BackgroundProbeTimeout);
    Assert.Equal(TimeSpan.FromSeconds(20), options.WholeRunTimeout);
}

[Fact]
public void Item_rejects_an_arbitrary_action_target()
{
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        SystemDiagnosticAction.Create(
            SystemDiagnosticActionKind.OpenRegisteredLogFolder,
            (SystemDiagnosticActionTarget)99,
            "打开日志目录"));
}
```

Also test blank diagnostic/fact IDs, duplicate fact keys, negative order, an error item without an error code, and invalid action-kind/target combinations. Valid combinations are: folder navigation with `InboxDirectory`, `LibraryDirectory`, or `WorkDirectory`; log opening with `None`; error-code copy with `None`.

- [ ] **Step 2: Run the focused test and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~SystemDiagnosticsContractTests
```

Expected: FAIL because `TraceFold.Media.Application.Diagnostics` does not exist.

- [ ] **Step 3: Add the exact public contract shape**

```csharp
public enum SystemDiagnosticSeverity { Healthy, Attention, Unavailable }
public enum SystemDiagnosticCategory
{
    FoldersAndStorage,
    Database,
    ProcessingTools,
    BackgroundWork,
    RuntimeAndLogs
}
public enum SystemDiagnosticFactSensitivity { Shareable, LocalOnly }
public enum SystemDiagnosticActionKind
{
    NavigateToFolderSettings,
    OpenRegisteredLogFolder,
    CopyErrorCode
}
public enum SystemDiagnosticActionTarget
{
    None,
    InboxDirectory,
    LibraryDirectory,
    WorkDirectory
}

public sealed record SystemDiagnosticFact(
    string Key,
    string Label,
    string Value,
    SystemDiagnosticFactSensitivity Sensitivity,
    int Order);

public sealed record SystemDiagnosticAction(
    SystemDiagnosticActionKind Kind,
    SystemDiagnosticActionTarget Target,
    string Label);

public sealed record SystemDiagnosticItem(
    string Id,
    SystemDiagnosticCategory Category,
    SystemDiagnosticSeverity Severity,
    int Order,
    string Title,
    string Conclusion,
    string? Impact,
    string? Guidance,
    string? ErrorCode,
    IReadOnlyList<SystemDiagnosticFact> Facts,
    IReadOnlyList<SystemDiagnosticAction> Actions);

public sealed record SystemDiagnosticsSnapshot(
    Guid RunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    SystemDiagnosticSeverity OverallSeverity,
    IReadOnlyList<SystemDiagnosticItem> Items);
```

Use validated `Create(...)` factories (or validated constructors) rather than relying on callers. Add `SystemDiagnosticProbeDescriptor` with `Id`, `Category`, `Order`, failure severity/title/impact/guidance, and `TimeoutErrorCode`. `ISystemDiagnosticProbe` exposes `Descriptor` and `CheckAsync`. `ISystemDiagnosticsService.CheckAsync` returns one snapshot. `IDiagnosticSummaryFormatter.Format` accepts a snapshot and a `TimeZoneInfo`. `SystemDiagnosticsOptions.GetProbeTimeout(category)` maps database/background to 15 seconds and the other categories to 5 seconds. `BackgroundWorkSummary` exposes exactly five integer counts and rejects negatives.

- [ ] **Step 4: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~SystemDiagnosticsContractTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Diagnostics/SystemDiagnosticsContracts.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/SystemDiagnosticsContractTests.cs
git commit -m "feat(media-desktop): define diagnostic contracts"
```

### Task 2: Build the Deterministic Redacted Summary Formatter

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Diagnostics/DiagnosticSummaryFormatter.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/DiagnosticSummaryFormatterTests.cs`

**Interfaces:**
- Consumes: validated snapshots and `SystemDiagnosticFactSensitivity` from Task 1.
- Produces: `DiagnosticSummaryFormatter : IDiagnosticSummaryFormatter`.
- Guarantees: category/item/fact ordering, stable plain text, and a strict allowlist of `Shareable` facts.

- [ ] **Step 1: Write failing determinism and privacy-sentinel tests**

```csharp
[Fact]
public void Format_includes_only_shareable_facts_in_deterministic_order()
{
    SystemDiagnosticsSnapshot snapshot = Snapshot([
        Item("runtime", SystemDiagnosticCategory.RuntimeAndLogs, 20, [
            Fact("path", "日志目录", @"C:\Users\PRIVATE\secret.log",
                SystemDiagnosticFactSensitivity.LocalOnly, 0),
            Fact("dotnet", ".NET", ".NET 10.0.8",
                SystemDiagnosticFactSensitivity.Shareable, 1)]),
        Item("folders", SystemDiagnosticCategory.FoldersAndStorage, 10, [
            Fact("space", "剩余空间", "120 GiB",
                SystemDiagnosticFactSensitivity.Shareable, 0)])]);

    string first = new DiagnosticSummaryFormatter().Format(
        snapshot, TimeZoneInfo.Utc);
    string second = new DiagnosticSummaryFormatter().Format(
        snapshot, TimeZoneInfo.Utc);

    Assert.Equal(first, second);
    Assert.Contains("folders", first, StringComparison.Ordinal);
    Assert.Contains("120 GiB", first, StringComparison.Ordinal);
    Assert.Contains(".NET 10.0.8", first, StringComparison.Ordinal);
    Assert.DoesNotContain("PRIVATE", first, StringComparison.OrdinalIgnoreCase);
    Assert.True(first.IndexOf("folders", StringComparison.Ordinal)
        < first.IndexOf("runtime", StringComparison.Ordinal));
}
```

Add one table-driven test containing sentinels for username, UNC host, drive path, database filename, log filename, media filename, author/series/tag, SHA-256, task/asset/taxonomy GUID, exception message, and probe filename. Put every sentinel in a `LocalOnly` fact and prove none enters the summary. Prove the diagnostic run ID and stable error code do enter it.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~DiagnosticSummaryFormatterTests
```

Expected: FAIL because the formatter does not exist.

- [ ] **Step 3: Implement the formatter as a pure projection**

```csharp
public sealed class DiagnosticSummaryFormatter : IDiagnosticSummaryFormatter
{
    public string Format(SystemDiagnosticsSnapshot snapshot, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(timeZone);
        var lines = new List<string>
        {
            "TraceFold 系统诊断摘要",
            $"运行标识：{snapshot.RunId:D}",
            $"检查时间：{TimeZoneInfo.ConvertTime(snapshot.CompletedAtUtc, timeZone):yyyy-MM-dd HH:mm:ss zzz}",
            $"总体状态：{SeverityText(snapshot.OverallSeverity)}"
        };

        foreach (SystemDiagnosticItem item in snapshot.Items
                     .OrderBy(item => item.Category)
                     .ThenByDescending(item => item.Severity)
                     .ThenBy(item => item.Order)
                     .ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            lines.Add($"[{item.Category}] {item.Title}：{SeverityText(item.Severity)}");
            if (item.ErrorCode is not null)
                lines.Add($"错误代码：{item.ErrorCode}");
            foreach (SystemDiagnosticFact fact in item.Facts
                         .Where(fact => fact.Sensitivity == SystemDiagnosticFactSensitivity.Shareable)
                         .OrderBy(fact => fact.Order)
                         .ThenBy(fact => fact.Key, StringComparer.Ordinal))
                lines.Add($"{fact.Label}：{fact.Value}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
```

Do not parse or redact strings heuristically. Exclusion is based only on the sensitivity enum, so new facts are private by default at their creation site and formatter behavior stays auditable.

- [ ] **Step 4: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~DiagnosticSummaryFormatterTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Diagnostics/DiagnosticSummaryFormatter.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/DiagnosticSummaryFormatterTests.cs
git commit -m "feat(media-desktop): format redacted diagnostics"
```

### Task 3: Orchestrate Probes with Failure and Timeout Isolation

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Application/Diagnostics/SystemDiagnosticsService.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/SystemDiagnosticsServiceTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/SystemDiagnosticsLoggingPrivacyTests.cs`

**Interfaces:**
- Consumes: all registered `ISystemDiagnosticProbe` instances, `SystemDiagnosticsOptions`, `TimeProvider`, and `ILogger<SystemDiagnosticsService>`.
- Produces: one immutable, deterministically ordered snapshot per run.
- Guarantees: concurrent probe starts, per-category timeout, 20-second whole-run envelope, user-cancellation propagation, no partial snapshot on page cancellation, and privacy-safe logging.

- [ ] **Step 1: Write failing aggregation, isolation, timeout, and cancellation tests**

```csharp
[Fact]
public async Task Check_keeps_successes_when_one_probe_fails_and_orders_results()
{
    var service = Service(
        SuccessfulProbe(Item("runtime.item", SystemDiagnosticCategory.RuntimeAndLogs,
            SystemDiagnosticSeverity.Healthy, 1)),
        ThrowingProbe("database", SystemDiagnosticCategory.Database),
        SuccessfulProbe(Item("folders.item", SystemDiagnosticCategory.FoldersAndStorage,
            SystemDiagnosticSeverity.Attention, 1)));

    SystemDiagnosticsSnapshot result = await service.CheckAsync(
        TestContext.Current.CancellationToken);

    Assert.Equal(SystemDiagnosticSeverity.Unavailable, result.OverallSeverity);
    Assert.Equal(new[] { "folders.item", "database.failure", "runtime.item" },
        result.Items.Select(item => item.Id));
    Assert.Equal("diagnostics.probe.failed",
        result.Items.Single(item => item.Id == "database.failure").ErrorCode);
}

[Fact]
public async Task Database_timeout_uses_its_specific_code()
{
    var options = new SystemDiagnosticsOptions(
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(100));
    var service = Service(options, NeverCompletingProbe(
        Descriptor("database", SystemDiagnosticCategory.Database,
            "diagnostics.database.timeout")));

    SystemDiagnosticsSnapshot result = await service.CheckAsync(
        TestContext.Current.CancellationToken);

    Assert.Equal("diagnostics.database.timeout", Assert.Single(result.Items).ErrorCode);
}
```

Also prove: all probes are started before a gated one completes; severity order is `Unavailable`, `Attention`, `Healthy` inside each category; ties use `Order` then ordinal ID; `CompletedAtUtc >= StartedAtUtc`; a caller-cancelled run throws `OperationCanceledException` and returns no snapshot; and a never-completing probe cannot extend the whole-run envelope. A probe returning zero items, a mismatched category, or an item ID outside its `{descriptor.Id}.` namespace becomes one `diagnostics.probe.failed` item rather than corrupting the page. Unique descriptor IDs plus enforced item namespaces guarantee globally unique automation/result IDs.

- [ ] **Step 2: Write a failing privacy-log test**

Use a recording `ILogger<SystemDiagnosticsService>` and a throwing probe whose exception message contains `C:\Users\PRIVATE\movie.mp4`, a GUID, and a metadata sentinel. Assert recorded state contains run ID, probe ID, phase, duration, severity, and stable error code, but none of the sentinel values or exception message. The logger may receive an exception type name, never `Exception.Message` as a property.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SystemDiagnosticsServiceTests|FullyQualifiedName~SystemDiagnosticsLoggingPrivacyTests"
```

Expected: FAIL because orchestration and DI registration do not exist.

- [ ] **Step 4: Implement one wrapper task per probe**

```csharp
private async Task<IReadOnlyList<SystemDiagnosticItem>> RunProbeAsync(
    Guid runId,
    ISystemDiagnosticProbe probe,
    CancellationToken wholeRunToken,
    CancellationToken callerToken)
{
    using var perProbeSource = CancellationTokenSource.CreateLinkedTokenSource(
        wholeRunToken);
    Task<IReadOnlyList<SystemDiagnosticItem>> checkTask =
        probe.CheckAsync(perProbeSource.Token);
    try
    {
        TimeSpan timeout = options.GetProbeTimeout(probe.Descriptor.Category);
        return await checkTask
            .WaitAsync(timeout, timeProvider, wholeRunToken);
    }
    catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
    {
        perProbeSource.Cancel();
        ObserveLateCompletion(checkTask);
        throw;
    }
    catch (TimeoutException)
    {
        perProbeSource.Cancel();
        ObserveLateCompletion(checkTask);
        return [CreateFailure(probe.Descriptor,
            probe.Descriptor.TimeoutErrorCode ?? "diagnostics.probe.timeout")];
    }
    catch (OperationCanceledException)
    {
        perProbeSource.Cancel();
        ObserveLateCompletion(checkTask);
        return [CreateFailure(probe.Descriptor,
            probe.Descriptor.TimeoutErrorCode ?? "diagnostics.probe.timeout")];
    }
    catch (Exception error)
    {
        LogFailureWithoutMessage(runId, probe.Descriptor.Id, error.GetType().FullName);
        return [CreateFailure(probe.Descriptor, "diagnostics.probe.failed")];
    }
}
```

Create one linked whole-run `CancellationTokenSource` and a `TimeProvider.CreateTimer` whose callback cancels that source after `options.WholeRunTimeout`; dispose the timer after `Task.WhenAll`. Each per-probe timeout must cancel the linked token passed into that probe, and `ObserveLateCompletion` must consume any later fault without retaining result data. Materialize all probe tasks before awaiting, and re-check the caller token before creating the snapshot. Validate descriptor IDs are unique and validate each returned nonempty item set against descriptor category/ID namespace before flattening. Sort by category, descending severity, item order, and ordinal ID. Overall severity is the maximum enum value. Use fixed user-facing failure strings from the descriptor; never show `Exception.Message`.

Register `SystemDiagnosticsOptions.Production`, `DiagnosticSummaryFormatter`/`IDiagnosticSummaryFormatter`, and `SystemDiagnosticsService`/`ISystemDiagnosticsService` in `AddMediaApplication()`.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Application.Tests/TraceFold.Media.Application.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SystemDiagnosticsServiceTests|FullyQualifiedName~SystemDiagnosticsLoggingPrivacyTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Application/Diagnostics/SystemDiagnosticsService.cs apps/media-desktop/src/TraceFold.Media.Application/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/SystemDiagnosticsServiceTests.cs apps/media-desktop/tests/TraceFold.Media.Application.Tests/Diagnostics/SystemDiagnosticsLoggingPrivacyTests.cs
git commit -m "feat(media-desktop): orchestrate diagnostic probes"
```

---

## Phase B — Read-Only Infrastructure Probes

### Task 4: Diagnose Saved Folders and Disk Capacity

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/FolderAndDiskDiagnosticProbe.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Settings/LocalDirectoryProbe.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/FolderAndDiskDiagnosticProbeTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Settings/LocalDirectoryProbeTests.cs`

**Interfaces:**
- Consumes: `IDirectorySettingsStore`, existing `IDirectoryProbe`, and new infrastructure-local `IVolumeSpaceReader`/`VolumeSpace`.
- Produces: exactly three logical folder items; capacity facts are repeated on each logical item while the underlying volume query is deduplicated.
- Guarantees: only saved settings are read, path facts are `LocalOnly`, capacity numbers are `Shareable`, and write probes leave no file.

- [ ] **Step 1: Write failing folder mapping and low-space tests**

```csharp
[Fact]
public async Task Check_uses_saved_folders_and_marks_paths_local_only()
{
    LibraryFolders folders = IsolatedFolders.Create();
    var volumes = new RecordingVolumeSpaceReader(
        new VolumeSpace("T:\\", 100L * GiB, 80L * GiB));
    var probe = ProbeWith(folders, new AlwaysWritableDirectoryProbe(), volumes);

    IReadOnlyList<SystemDiagnosticItem> items = await probe.CheckAsync(
        TestContext.Current.CancellationToken);

    Assert.Equal(new[] { "folders.inbox", "folders.library", "folders.work" },
        items.Select(item => item.Id));
    Assert.All(items.SelectMany(item => item.Facts)
        .Where(fact => fact.Key.EndsWith(".path", StringComparison.Ordinal)),
        fact => Assert.Equal(SystemDiagnosticFactSensitivity.LocalOnly, fact.Sensitivity));
    Assert.Equal(1, volumes.CallCount); // all logical folders share T:\
}

[Theory]
[InlineData(9, 100)]
[InlineData(50, 1000)]
public async Task Check_marks_space_attention_below_either_threshold(
    long freeGiB, long totalGiB)
{
    // free < 10 GiB OR free < 10% total
    SystemDiagnosticItem folder = (await CheckSpace(freeGiB, totalGiB)).First();
    Assert.Equal(SystemDiagnosticSeverity.Attention, folder.Severity);
    Assert.Equal("diagnostics.storage.space_low", folder.ErrorCode);
}
```

Add tests for no saved settings (three `diagnostics.folder.not_configured` items), invalid path (`diagnostics.folder.invalid_path`), missing folder (`diagnostics.folder.missing`), nonwritable folder (`diagnostics.folder.not_writable`), each pairwise same/nested relationship (`diagnostics.folder.unsafe_relationship`), capacity query failure (`diagnostics.storage.query_failed`), exact 10 GiB and exact 10% healthy boundaries, deterministic item order, and the typed focus target for each folder action.

- [ ] **Step 2: Add failing zero-residue tests to `LocalDirectoryProbeTests`**

Cover success, missing directory, an already-cancelled token, and cancellation after the probe file opens. Give `LocalDirectoryProbe` an optional construction-time `Action<string>? probeOpened` infrastructure test seam; invoke it after `FileStream` creation and before `FlushAsync`. The cancellation test cancels its source from this callback. Production DI uses the parameterless/default construction, and the seam is never added to an Application contract. In every case assert no `.tracefold-write-probe-*.tmp` remains.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~FolderAndDiskDiagnosticProbeTests|FullyQualifiedName~LocalDirectoryProbeTests"
```

Expected: FAIL because the folder/storage probe and volume reader do not exist, and the extended residue case is not implemented.

- [ ] **Step 4: Implement saved-folder checks and cache per canonical volume root**

Use `Path.IsPathFullyQualified`, `Path.GetFullPath`, `Path.TrimEndingDirectorySeparator`, and the same pairwise `Path.GetRelativePath` semantics as `DirectorySettingsService`; do not diagnose unsaved `SettingsViewModel` text. `WindowsVolumeSpaceReader` uses `DriveInfo` only after a valid canonical path is available. Cache with `StringComparer.OrdinalIgnoreCase` so one volume is queried once.

```csharp
bool isLow = space.AvailableBytes < 10L * 1024 * 1024 * 1024
    || space.AvailableBytes * 10 < space.TotalBytes;
```

Avoid overflow by casting both capacity values to `decimal` before the 10% comparison. Each folder item uses fixed Chinese title/conclusion/impact/guidance, one `NavigateToFolderSettings` action, and a stable target. Repeat the cached volume total/free facts on every logical folder item. Capacity failure makes the otherwise healthy folder item `Attention` with `diagnostics.storage.query_failed`; folder absence/unwritability/unsafe relationship is `Unavailable`.

Keep `LocalDirectoryProbe.CanWriteAsync` on `FileMode.CreateNew`, `FileShare.None`, `FileOptions.DeleteOnClose`, and an unconditional finally cleanup that suppresses only expected cleanup `IOException`/`UnauthorizedAccessException` after the original result is established. Cancellation propagates after cleanup.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~FolderAndDiskDiagnosticProbeTests|FullyQualifiedName~LocalDirectoryProbeTests"
```

Expected: PASS and all temporary-directory assertions are empty.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/FolderAndDiskDiagnosticProbe.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Settings/LocalDirectoryProbe.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/FolderAndDiskDiagnosticProbeTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Settings/LocalDirectoryProbeTests.cs
git commit -m "feat(media-desktop): diagnose folders and storage"
```

### Task 5: Diagnose SQLite Availability, Migrations, and Integrity

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/DatabaseDiagnosticProbe.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/DatabaseDiagnosticProbeTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<MediaDesktopDbContext>` through a narrow infrastructure `IDatabaseDiagnosticReader` seam.
- Produces: database availability, applied/current migration agreement, and `PRAGMA quick_check` items.
- Guarantees: no `Migrate`, `EnsureCreated`, transaction, table write, repair pragma, or diagnostic persistence.

- [ ] **Step 1: Write failing mapping tests with a fake reader**

```csharp
[Fact]
public async Task Check_reports_migration_mismatch_without_running_migrations()
{
    var reader = new RecordingDatabaseDiagnosticReader(new DatabaseDiagnosticRead(
        CanOpen: true,
        AppliedMigrations: ["InitialDesktopSettings"],
        AvailableMigrations: ["InitialDesktopSettings", "Stage2MediaLibrary"],
        QuickCheckRows: ["ok"]));

    SystemDiagnosticItem item = Assert.Single(await
        new DatabaseDiagnosticProbe(reader).CheckAsync(
            TestContext.Current.CancellationToken));

    Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
    Assert.Equal("diagnostics.database.migration_mismatch", item.ErrorCode);
    Assert.Equal(1, reader.ReadCallCount);
    Assert.Equal(0, reader.WriteCallCount);
}

[Theory]
[InlineData("*** in database main ***")]
[InlineData("row 42 missing from index")]
public async Task Check_requires_quick_check_to_return_only_ok(string result)
{
    SystemDiagnosticItem item = Assert.Single(await CheckQuickCheck([result]));
    Assert.Equal("diagnostics.database.quick_check_failed", item.ErrorCode);
}
```

Add cases for open/read failure (`diagnostics.database.unavailable`), zero or multiple non-`ok` rows, ordinal case-sensitive `ok`, and a fully healthy result. The healthy item exposes migration count and `quick_check=ok` as `Shareable`; the database path is either absent or `LocalOnly`.

- [ ] **Step 2: Write a failing real-SQLite read-only integration test**

Create and migrate `TestMediaDatabase`, seed one inbox row, snapshot the database file length/hash and business row counts, run `EfDatabaseDiagnosticReader`, then assert the same row counts and a healthy result. Use `CreateAtMigrationAsync` to prove an older applied migration reports mismatch without advancing it. Do not attempt to manufacture a corrupted user database; mapping of non-`ok` results belongs to the fake-reader test.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~DatabaseDiagnosticProbeTests
```

Expected: FAIL because the database reader and probe do not exist.

- [ ] **Step 4: Implement the reader with read-only commands**

```csharp
public async Task<DatabaseDiagnosticRead> ReadAsync(CancellationToken token)
{
    await using MediaDesktopDbContext context =
        await contextFactory.CreateDbContextAsync(token);
    await context.Database.OpenConnectionAsync(token);
    string[] applied = [.. await context.Database.GetAppliedMigrationsAsync(token)];
    string[] available = [.. context.Database.GetMigrations()];
    await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
    command.CommandText = "PRAGMA quick_check";
    var rows = new List<string>();
    await using DbDataReader reader = await command.ExecuteReaderAsync(token);
    while (await reader.ReadAsync(token))
        rows.Add(reader.GetString(0));
    return new(true, applied, available, rows);
}
```

Do not call `MigrateAsync`. Compare ordered migration identifiers with `StringComparer.Ordinal`; a difference is unavailable. Catch SQLite/open/read exceptions inside the probe and return `diagnostics.database.unavailable`. Let `OperationCanceledException` propagate so the Application orchestrator can distinguish caller cancellation and `diagnostics.database.timeout`.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~DatabaseDiagnosticProbeTests
```

Expected: PASS; the old-migration fixture remains old after the probe.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/DatabaseDiagnosticProbe.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/DatabaseDiagnosticProbeTests.cs
git commit -m "feat(media-desktop): diagnose sqlite health"
```

### Task 6: Diagnose FFmpeg and FFprobe Independently

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/ProcessingToolDiagnosticProbe.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/BundledMediaToolLocator.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/ProcessingToolDiagnosticProbeTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/BundledMediaToolLocatorTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/FfmpegMediaToolIntegrationTests.cs`

**Interfaces:**
- Consumes: an extended `BundledMediaToolLocator.Inspect()` result, `IExecutableVersionReader`, and a reusable infrastructure `IControlledProcessRunner` implemented by `ControlledProcessRunner`.
- Produces: separate FFmpeg and FFprobe version facts in one `ProcessingTools` diagnostic item.
- Guarantees: package-local resolved paths only, bounded captured output, hidden child processes, kill-and-wait on timeout/cancellation, and no user-media arguments.

- [ ] **Step 1: Write failing result-mapping tests**

```csharp
[Fact]
public async Task Check_requires_both_tools_and_preserves_both_versions()
{
    using ToolRoot root = ToolRoot.WithPlaceholderExecutables();
    var reader = new FakeExecutableVersionReader(new Dictionary<string, ExecutableVersionRead>
    {
        [root.FfmpegPath] = new(0, "ffmpeg version 7.1", false),
        [root.FfprobePath] = new(0, "ffprobe version 7.1", false)
    });

    SystemDiagnosticItem item = Assert.Single(await Probe(root, reader)
        .CheckAsync(TestContext.Current.CancellationToken));

    Assert.Equal(SystemDiagnosticSeverity.Healthy, item.Severity);
    Assert.Contains(item.Facts, fact => fact.Key == "tools.ffmpeg.version"
        && fact.Value == "ffmpeg version 7.1");
    Assert.Contains(item.Facts, fact => fact.Key == "tools.ffprobe.version"
        && fact.Value == "ffprobe version 7.1");
    Assert.All(reader.Arguments, args => Assert.Equal(new[] { "-version" }, args));
}
```

Add cases for locator failure/missing executables, FFmpeg-only and FFprobe-only failure, launch failure, nonzero exit, blank/unrecognized first line, bounded output, and timeout. Verify all failures are `Unavailable` and map to one of: `diagnostics.tools.ffmpeg_missing`, `diagnostics.tools.ffprobe_missing`, `diagnostics.tools.launch_failed`, `diagnostics.tools.version_unknown`, or `diagnostics.tools.timeout`. The impact text must state that ordinary images and files not needing preprocessing remain manageable.

Extend `BundledMediaToolLocatorTests` first: `Inspect()` must distinguish a trusted missing FFmpeg from a trusted missing FFprobe while returning only canonical direct-child candidate paths. Unsafe root/reparse/direct-child cases expose no path. Existing `TryResolve()` delegates to `Inspect()` and still returns a `MediaToolLocation` only when both executables are trusted and present, preserving all processing behavior.

- [ ] **Step 2: Write a failing controlled-process cleanup test**

Test `ControlledProcessRunner` directly with `%ComSpec% /d /c ping -t 127.0.0.1`, cancel after its optional process-start observer records the PID, and assert that exact process tree exits within two seconds. A second runner test emits more than 8 KiB and proves stdout/stderr capture is capped at exactly 8 KiB. `ControlledExecutableVersionReader` is a thin adapter that always passes `-version`. Extend `FfmpegMediaToolIntegrationTests` to retain existing merge/probe cleanup guarantees; do not refactor `FfmpegMediaTool` unless those tests first prove the shared runner preserves its progress-stream behavior.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~ProcessingToolDiagnosticProbeTests|FullyQualifiedName~BundledMediaToolLocatorTests|FullyQualifiedName~FfmpegMediaToolIntegrationTests"
```

Expected: FAIL because independent executable version diagnostics do not exist.

- [ ] **Step 4: Implement a controlled version reader**

Build `ProcessStartInfo` with `UseShellExecute=false`, `CreateNoWindow=true`, redirected stdout/stderr, and `ArgumentList.Add("-version")`. Resolve candidate states and paths only through `BundledMediaToolLocator.Inspect()`; never accept a UI/result path. On cancellation or timeout, kill the process tree, await process exit and reader tasks, then rethrow cancellation or return a typed timeout result. Parse only the first nonblank bounded line. Mark both path facts `LocalOnly` and version facts `Shareable`.

Do not broaden `IMediaTool` or replace `ProcessingDiagnosticsService`; the existing brief Settings card remains backward compatible.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~ProcessingToolDiagnosticProbeTests|FullyQualifiedName~BundledMediaToolLocatorTests|FullyQualifiedName~FfmpegMediaToolIntegrationTests"
```

Expected: PASS with no child test process remaining.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/ProcessingToolDiagnosticProbe.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/MediaTools/BundledMediaToolLocator.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/ProcessingToolDiagnosticProbeTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/BundledMediaToolLocatorTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/MediaTools/FfmpegMediaToolIntegrationTests.cs
git commit -m "feat(media-desktop): diagnose bundled media tools"
```

### Task 7: Summarize Background Work Without File Details

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfBackgroundWorkSummaryStore.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/BackgroundWorkDiagnosticProbe.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/BackgroundWorkDiagnosticProbeTests.cs`

**Interfaces:**
- Consumes: `IBackgroundWorkSummaryStore` from Task 1.
- Produces: one count-only `BackgroundWork` item.
- Defines: recovery/cleanup pending equals `IngestOperationStage.RecoveryBlocked` plus `IngestOperationStage.CleanupPending`; completed/failed/active ingest stages are not silently folded into that number.

- [ ] **Step 1: Write failing EF count-projection tests**

```csharp
[Fact]
public async Task Store_counts_only_the_five_approved_buckets()
{
    await using TestMediaDatabase database = await TestMediaDatabase.CreateAsync();
    await database.MigrateAsync();
    await database.SeedBackgroundWorkFixtureAsync(
        queued: 2, running: 1, interrupted: 3, failed: 4,
        recoveryBlocked: 2, cleanupPending: 1, completedIngest: 5);
    var store = new EfBackgroundWorkSummaryStore(database.CreateContextFactory());

    BackgroundWorkSummary result = await store.LoadAsync(
        TestContext.Current.CancellationToken);

    Assert.Equal(new BackgroundWorkSummary(2, 1, 3, 4, 3), result);
}
```

Query each status with translated `CountAsync` calls (or one database-side grouped projection). Seed actual `ProcessingJobRow`/`IngestOperationRow` dependencies through the test helper; never expose their IDs, paths, filenames, error strings, or rows to the Application contract.

- [ ] **Step 2: Write failing probe severity and privacy tests**

```csharp
[Theory]
[InlineData(5, 2, 0, 0, 0, SystemDiagnosticSeverity.Healthy)]
[InlineData(0, 0, 1, 0, 0, SystemDiagnosticSeverity.Attention)]
[InlineData(0, 0, 0, 1, 0, SystemDiagnosticSeverity.Attention)]
[InlineData(0, 0, 0, 0, 1, SystemDiagnosticSeverity.Attention)]
public async Task Probe_maps_counts_to_severity(
    int queued, int running, int interrupted, int failed, int recovery,
    SystemDiagnosticSeverity expected)
{
    SystemDiagnosticItem item = Assert.Single(await Probe(
        new BackgroundWorkSummary(queued, running, interrupted, failed, recovery))
        .CheckAsync(TestContext.Current.CancellationToken));
    Assert.Equal(expected, item.Severity);
}
```

Assert every fact is a numeric `Shareable` count and no serialized item contains fixture filename/path/ID sentinels. A store exception maps to `Attention` plus `diagnostics.background.query_failed`. Cancellation propagates for orchestrator handling.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~BackgroundWorkDiagnosticProbeTests
```

Expected: FAIL because the count store, probe, and fixture helper do not exist.

- [ ] **Step 4: Implement count-only persistence and fixed presentation facts**

Use `AsNoTracking()` and enum predicates. The probe emits facts `background.processing.queued`, `.running`, `.interrupted`, `.failed`, and `background.ingest.recovery_pending` in that order. Queued/running alone remain healthy; any interrupted/failed/recovery count greater than zero is attention. Do not add row-detail methods to `IBackgroundWorkSummaryStore`.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~BackgroundWorkDiagnosticProbeTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Persistence/EfBackgroundWorkSummaryStore.cs apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/BackgroundWorkDiagnosticProbe.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Support/TestMediaDatabase.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/BackgroundWorkDiagnosticProbeTests.cs
git commit -m "feat(media-desktop): summarize background work"
```

### Task 8: Diagnose Runtime Manifest and Registered Log Root

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/RuntimeAndLogDiagnosticProbe.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/RuntimeAndLogDiagnosticProbeTests.cs`

**Interfaces:**
- Consumes: `IAppDataPathProvider`, `IDirectoryProbe`, and `RuntimeDiagnosticOptions` containing an explicit base directory and immutable `RuntimeProductIdentity` values.
- Produces: product/file/informational version, Stage checkpoint, source commit, Windows/.NET facts, app/log directory facts, and log-root availability.
- Guarantees: manifest read is bounded and schema-validated; full roots are `LocalOnly`; no environment username/hostname is emitted as a fact.

- [ ] **Step 1: Write failing manifest-state tests**

```csharp
[Fact]
public async Task Missing_manifest_is_attention_in_a_development_layout()
{
    using TestDirectory root = TestDirectory.Create();
    SystemDiagnosticItem item = Assert.Single(await Probe(root.Path,
        productInformationalVersion: "4.0.0-stage5cb+abc").CheckAsync(
            TestContext.Current.CancellationToken));

    Assert.Equal(SystemDiagnosticSeverity.Attention, item.Severity);
    Assert.Equal("diagnostics.runtime.manifest_missing", item.ErrorCode);
}

[Fact]
public async Task Manifest_must_match_the_assembly_informational_version()
{
    using TestDirectory root = TestDirectory.Create();
    WriteManifest(root.Path, new
    {
        StageCheckpoint = "5C-B",
        SourceCommit = new string('a', 40),
        InformationalVersion = "4.0.0-stage5cb+different"
    });

    SystemDiagnosticItem item = Assert.Single(await Probe(root.Path,
        productInformationalVersion: "4.0.0-stage5cb+expected").CheckAsync(
            TestContext.Current.CancellationToken));
    Assert.Equal(SystemDiagnosticSeverity.Unavailable, item.Severity);
    Assert.Equal("diagnostics.runtime.manifest_invalid", item.ErrorCode);
}
```

Add valid manifest, malformed/oversized JSON, invalid checkpoint, non-40-character lowercase commit, missing fields, product version facts, log directory missing/unwritable, and write-probe zero-residue cases. A log write problem is `Attention`, not `Unavailable`. Manifest absence is `Attention`; invalid or contradictory manifest is `Unavailable`.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~RuntimeAndLogDiagnosticProbeTests
```

Expected: FAIL because runtime/log diagnostics do not exist.

- [ ] **Step 3: Implement bounded manifest and runtime reads**

Read only `<BaseDirectory>\package-version.json`; reject reparse points, non-leaf entries, and files above exactly 64 KiB. Deserialize a private exact manifest record with case-sensitive property names. Require `StageCheckpoint == "5C-B"`, a lowercase 40-hex `SourceCommit`, and exact ordinal match between manifest and assembly informational versions.

Define `RuntimeProductIdentity(ProductVersion, FileVersion, InformationalVersion)` so tests pass literal identities without generating assemblies. Production DI creates it once from `AssemblyName.Version`, `FileVersionInfo`, and `AssemblyInformationalVersionAttribute`. Use `RuntimeInformation.OSDescription` and `RuntimeInformation.FrameworkDescription`; never call external system commands. Mark version/OS/runtime/checkpoint/commit facts `Shareable`; app data root and logs root are `LocalOnly`. Use the existing `IDirectoryProbe` for log existence/write safety and do not create the log directory during diagnosis.

- [ ] **Step 4: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~RuntimeAndLogDiagnosticProbeTests
```

Expected: PASS with no manifest or write-probe residue beyond the test-owned fixture itself.

- [ ] **Step 5: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/Diagnostics/RuntimeAndLogDiagnosticProbe.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/RuntimeAndLogDiagnosticProbeTests.cs
git commit -m "feat(media-desktop): diagnose runtime and logs"
```

### Task 9: Register Exactly Five Probes and Verify the Composition Boundary

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/SystemDiagnosticsCompositionTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/SystemDiagnosticsReadOnlyIntegrationTests.cs`

**Interfaces:**
- Registers: one `IBackgroundWorkSummaryStore`, one `IVolumeSpaceReader`, one `IDatabaseDiagnosticReader`, one `IControlledProcessRunner`, one `IExecutableVersionReader`, `RuntimeDiagnosticOptions`, and exactly five `ISystemDiagnosticProbe` implementations.
- Consumes: the Application service registration from Task 3.
- Proves: one host can resolve the complete diagnostics graph without Desktop or UI types.

- [ ] **Step 1: Write a failing DI composition test**

```csharp
[Fact]
public void Infrastructure_registers_exactly_one_probe_per_category()
{
    using TestDirectory root = TestDirectory.Create();
    using ServiceProvider provider = new ServiceCollection()
        .AddLogging()
        .AddMediaApplication()
        .AddMediaInfrastructure(root.Path, mediaToolRootOverride: null)
        .BuildServiceProvider();

    ISystemDiagnosticProbe[] probes =
        [.. provider.GetServices<ISystemDiagnosticProbe>()];

    Assert.Equal(5, probes.Length);
    Assert.Equal(Enum.GetValues<SystemDiagnosticCategory>(),
        probes.Select(probe => probe.Descriptor.Category).Order().ToArray());
    Assert.Equal(probes.Length,
        probes.Select(probe => probe.Descriptor.Id).Distinct(StringComparer.Ordinal).Count());
    Assert.NotNull(provider.GetRequiredService<ISystemDiagnosticsService>());
    Assert.NotNull(provider.GetRequiredService<IDiagnosticSummaryFormatter>());
}
```

- [ ] **Step 2: Write a failing read-only integration test**

Create test-owned data/inbox/library/work/log directories and migrate the test database. Save directory settings, seed representative task counts and one media sentinel file, snapshot:

- all business-table row counts;
- directory settings;
- processing/ingest states;
- each existing test-owned file's relative name, size, and SHA-256.

Run the real diagnostics service with real folder/database/background/runtime probes and a deterministic fake tool version reader. Assert all five categories are present, then compare the before/after snapshots exactly. Configure an in-memory logger so no rolling file is created; diagnostics itself must create none. Assert no `.tracefold-write-probe-*.tmp` and no diagnostic history file/table exists.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SystemDiagnosticsCompositionTests|FullyQualifiedName~SystemDiagnosticsReadOnlyIntegrationTests"
```

Expected: FAIL because the probes and their readers are not registered.

- [ ] **Step 4: Add explicit registrations**

```csharp
services.AddSingleton<IVolumeSpaceReader, WindowsVolumeSpaceReader>();
services.AddSingleton<IDatabaseDiagnosticReader, EfDatabaseDiagnosticReader>();
services.AddSingleton<IControlledProcessRunner, ControlledProcessRunner>();
services.AddSingleton<IExecutableVersionReader, ControlledExecutableVersionReader>();
services.AddSingleton<IBackgroundWorkSummaryStore, EfBackgroundWorkSummaryStore>();
services.AddSingleton(RuntimeDiagnosticOptions.FromAssembly(
    AppContext.BaseDirectory,
    Assembly.GetEntryAssembly() ?? typeof(DependencyInjection).Assembly));
services.AddSingleton<ISystemDiagnosticProbe, FolderAndDiskDiagnosticProbe>();
services.AddSingleton<ISystemDiagnosticProbe, DatabaseDiagnosticProbe>();
services.AddSingleton<ISystemDiagnosticProbe, ProcessingToolDiagnosticProbe>();
services.AddSingleton<ISystemDiagnosticProbe, BackgroundWorkDiagnosticProbe>();
services.AddSingleton<ISystemDiagnosticProbe, RuntimeAndLogDiagnosticProbe>();
```

Do not register probes by assembly scanning and do not introduce a dynamic plugin loader in this stage. Keep the registration order aligned with category order even though the service also sorts deterministically.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/TraceFold.Media.Infrastructure.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SystemDiagnosticsCompositionTests|FullyQualifiedName~SystemDiagnosticsReadOnlyIntegrationTests"
```

Expected: PASS and before/after business/media snapshots match exactly.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Infrastructure/DependencyInjection.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/SystemDiagnosticsCompositionTests.cs apps/media-desktop/tests/TraceFold.Media.Infrastructure.Tests/Diagnostics/SystemDiagnosticsReadOnlyIntegrationTests.cs
git commit -m "feat(media-desktop): compose read-only diagnostics"
```

---

## Phase C — Desktop Lifecycle, Safe Actions, and Native Settings Tabs

### Task 10: Add Clipboard and Registered Log-Folder Adapters

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Services/DiagnosticDesktopServices.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Services/DiagnosticDesktopServicesTests.cs`

**Interfaces:**
- Produces: `IDiagnosticClipboardService`, `IRegisteredLogFolderService`, `DiagnosticDesktopActionResult`, `WindowsDiagnosticClipboardService`, and `WindowsRegisteredLogFolderService`.
- Consumes: `IAppDataPathProvider` and existing `IProcessStarter`.
- Guarantees: log opening always derives the path from the registered provider; no caller can supply an arbitrary path.

- [ ] **Step 1: Write failing registered-root safety tests**

```csharp
[Fact]
public async Task OpenLogFolder_uses_only_the_registered_log_root()
{
    using TemporaryDirectory root = TemporaryDirectory.Create();
    string logs = Directory.CreateDirectory(Path.Combine(root.Path, "logs")).FullName;
    var starter = new RecordingProcessStarter();
    var service = new WindowsRegisteredLogFolderService(
        new FixedPathProvider(root.Path, logs), starter);

    DiagnosticDesktopActionResult result = await service.OpenAsync(
        TestContext.Current.CancellationToken);

    Assert.True(result.Succeeded);
    Assert.Equal("explorer.exe", starter.StartInfo!.FileName);
    Assert.Equal(new[] { logs }, starter.StartInfo.ArgumentList);
}

[Fact]
public async Task OpenLogFolder_does_not_launch_when_registered_root_is_missing()
{
    using TemporaryDirectory root = TemporaryDirectory.Create();
    var starter = new RecordingProcessStarter();
    var service = new WindowsRegisteredLogFolderService(
        new FixedPathProvider(root.Path, Path.Combine(root.Path, "missing")), starter);

    DiagnosticDesktopActionResult result = await service.OpenAsync(
        TestContext.Current.CancellationToken);

    Assert.False(result.Succeeded);
    Assert.Equal("diagnostics.logs.open_failed", result.ErrorCode);
    Assert.Null(starter.StartInfo);
}
```

Define the private disposable `TemporaryDirectory` helper in this test file. Add invalid/nonabsolute provider output and process-start failure tests. Assert no API accepts `SystemDiagnosticFact.Value` or a caller path.

- [ ] **Step 2: Write clipboard contract tests**

The WinUI clipboard adapter uses a `DataPackage`, sets plain text, calls `Clipboard.SetContent`, and returns `diagnostics.clipboard.copy_failed` on expected platform/COM failures without logging the copied text. Keep VM tests independent by faking `IDiagnosticClipboardService`; do not require a live interactive clipboard in the unit suite.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~DiagnosticDesktopServicesTests
```

Expected: FAIL because the two desktop adapters do not exist.

- [ ] **Step 4: Implement typed adapters**

`WindowsRegisteredLogFolderService.OpenAsync` re-reads `IAppDataPathProvider.GetPaths().LogsDirectory`, requires a fully qualified existing directory, and starts `explorer.exe` with `UseShellExecute=false` plus exactly one `ArgumentList` entry. `WindowsDiagnosticClipboardService.CopyAsync(string text, ...)` rejects blank input, never logs or caches it, and reports only a stable error code.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~DiagnosticDesktopServicesTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Services/DiagnosticDesktopServices.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Services/DiagnosticDesktopServicesTests.cs
git commit -m "feat(media-desktop): add safe diagnostic actions"
```

### Task 11: Present Session Diagnostics in a Dedicated ViewModel

**Files:**
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SystemDiagnosticsViewModel.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SystemDiagnosticsViewModelTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs`

**Interfaces:**
- Consumes: `ISystemDiagnosticsService`, `IDiagnosticSummaryFormatter`, `IDiagnosticClipboardService`, and `IRegisteredLogFolderService`.
- Produces: session-only group/item/fact/action view models, `ActivateAsync`, `RefreshCommand`, `CopySummaryCommand`, `ExecuteActionCommand`, `CancelCurrentRun`, and a typed `FolderSettingsNavigationRequested` event.
- Guarantees: first activation auto-runs once, one service call at a time, refresh preserves the old snapshot until atomic replacement, and cancellation never replaces it.

- [ ] **Step 1: Write failing activation and single-flight tests**

```csharp
[Fact]
public async Task Activate_runs_once_and_refresh_is_single_flight()
{
    var diagnostics = new GatedDiagnosticsService();
    SystemDiagnosticsViewModel viewModel = Create(diagnostics);

    Task first = viewModel.ActivateAsync(CancellationToken.None);
    await diagnostics.Started.Task;
    Task duplicateActivation = viewModel.ActivateAsync(CancellationToken.None);
    Assert.False(viewModel.RefreshCommand.CanExecute(null));
    Assert.Equal(1, diagnostics.CallCount);

    diagnostics.Complete(HealthySnapshot());
    await Task.WhenAll(first, duplicateActivation);
    await viewModel.ActivateAsync(CancellationToken.None);
    Assert.Equal(1, diagnostics.CallCount);
}
```

Add tests that: initial run has no stale groups; refresh retains old groups and shows “正在重新检查”; completed refresh atomically replaces groups; caller cancellation retains old groups; service failure shows a stable page message without clearing old results; snapshot survives page reactivation in the same VM; copy is disabled while running or without snapshot; duplicate refresh does not queue; and overall/group/item ordering matches the snapshot.

- [ ] **Step 2: Write failing safe-action tests**

```csharp
[Fact]
public async Task Folder_action_raises_only_the_typed_focus_target()
{
    SystemDiagnosticsViewModel viewModel = CreateWithSnapshot(SnapshotWithAction(
        SystemDiagnosticActionKind.NavigateToFolderSettings,
        SystemDiagnosticActionTarget.WorkDirectory));
    SystemDiagnosticActionTarget? target = null;
    viewModel.FolderSettingsNavigationRequested += (_, value) => target = value;

    await viewModel.Groups.Single().Items.Single().Actions.Single()
        .ExecuteCommand.ExecuteAsync(null);

    Assert.Equal(SystemDiagnosticActionTarget.WorkDirectory, target);
}
```

Also verify log action calls `IRegisteredLogFolderService` with no path argument; copy-error action copies only `item.ErrorCode`; copy-summary calls the formatter with `TimeZoneInfo.Local`; copy failure displays `diagnostics.clipboard.copy_failed`; and no copied text is stored as a VM property.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~SystemDiagnosticsViewModelTests
```

Expected: FAIL because the diagnostic presenter does not exist.

- [ ] **Step 4: Implement the run gate and projection**

Use an atomic `_isRunning`/`SemaphoreSlim` gate and a page-lifecycle linked CTS. `ActivateAsync` records that automatic activation was attempted and delegates to the same private run method as refresh. Materialize a complete `SystemDiagnosticsSnapshot` and complete group projection before assigning observable properties on the UI thread. Do not mutate existing group objects during refresh.

Group order is enum order. Item order is unavailable, attention, healthy, then declared order/ID. Healthy item presentation sets `ShowDetails=false`; attention/unavailable sets `ShowDetails=true`. Expose status text and an icon glyph/name as separate properties so XAML never relies on color alone.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~SystemDiagnosticsViewModelTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SystemDiagnosticsViewModel.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SystemDiagnosticsViewModelTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Support/DesktopViewModelFakes.cs
git commit -m "feat(media-desktop): present session diagnostics"
```

### Task 12: Convert Settings to Two Native Tabs with Accessible Diagnostics

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsStructureTests.cs`

**Interfaces:**
- Consumes: existing `SettingsViewModel` plus `SystemDiagnosticsViewModel`.
- Produces: one Settings navigation destination with `TabView` items “文件夹与扫描” and “系统诊断”.
- Guarantees: folder tab is default, diagnostic activation happens on first tab selection, unload cancels work, and navigation actions select/focus the matching folder field.

- [ ] **Step 1: Write failing XAML structure and accessibility tests**

Parse `SettingsPage.xaml` and assert these page-level stable IDs exist exactly once:

```csharp
string[] ids =
[
    "SettingsTabs", "FolderSettingsTab", "SystemDiagnosticsTab",
    "DiagnosticsOverallStatus", "DiagnosticsLastChecked",
    "DiagnosticsRefreshButton", "DiagnosticsCopySummaryButton"
];
```

Assert presentation-VM tests produce group automation IDs `DiagnosticsFoldersGroup`, `DiagnosticsDatabaseGroup`, `DiagnosticsToolsGroup`, `DiagnosticsBackgroundGroup`, and `DiagnosticsRuntimeGroup`. Item/action IDs are exactly `DiagnosticErrorCode-{stableItemId}` and `DiagnosticAction-{stableItemId}-{actionKind}` rather than repeated literal IDs inside a data template.

Assert the `TabView` has `IsAddTabButtonVisible="False"`, both items have `IsClosable="False"`, folder tab precedes diagnostics, the group/item/action templates bind `AutomationProperties.AutomationId`, each actionable control has a nonblank Chinese `AutomationProperties.Name`, status is represented by icon plus text, and the diagnostics item template binds title and status text into an accessible name. Preserve all existing folder/scan/tool IDs.

- [ ] **Step 2: Write failing code-behind lifecycle/focus structure tests**

Assert `SelectionChanged` calls `DiagnosticsViewModel.ActivateAsync`; `Unloaded` calls `CancelCurrentRun`; and the navigation event maps only the three enum targets to `InboxFolderPath.Focus`, `LibraryFolderPath.Focus`, or `WorkFolderPath.Focus`. No result fact/path may be passed to focus or explorer.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SettingsDiagnosticsStructureTests|FullyQualifiedName~DesktopCompositionRootTests|FullyQualifiedName~SettingsViewModelTests"
```

Expected: FAIL because Settings still has one scroll surface and the new graph is unregistered. Existing `SettingsViewModelTests` should remain green.

- [ ] **Step 4: Build the two-tab page**

Move the existing settings `ScrollViewer` unchanged into the first nonclosable `TabViewItem`. The second tab contains:

- a header with overall icon/text, last-check time, refresh, copy summary, and transient action status;
- explanatory text “结果基于已保存设置”;
- five ordered group sections bound to immutable group VMs;
- compact healthy rows;
- expanded attention/unavailable rows with conclusion, impact, guidance, error code, facts, and typed action buttons;
- a progress indicator that does not remove the previous snapshot during refresh.

Use theme resources (`CardBackgroundFillColorDefaultBrush`, text/status brushes) and status glyphs plus words. Do not hardcode light-only backgrounds. All buttons remain keyboard reachable and invoke commands with Enter/Space by native behavior.

In code-behind, keep the existing settings load and auto-scan handlers. Add diagnostics tab selection, page-unload cancellation, and folder-target focus. Catch only at the async-void UI boundary, log a fixed event name without user facts, and assign a stable UI message.

Register both safe desktop services and `SystemDiagnosticsViewModel` as singletons in `DesktopCompositionRoot`; inject both VMs into the singleton `SettingsPage`.

- [ ] **Step 5: Run focused tests and verify green**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SettingsDiagnosticsStructureTests|FullyQualifiedName~DesktopCompositionRootTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~SystemDiagnosticsViewModelTests"
```

Expected: PASS. Existing folder save, auto-scan, and brief processing status behavior remains unchanged.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsStructureTests.cs
git commit -m "feat(media-desktop): add settings diagnostics tab"
```

### Task 13: Close Desktop Layering, Keyboard, and Privacy Regressions

**Files:**
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsStructureTests.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsArchitectureTests.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs`

**Interfaces:**
- Protects: Application/Infrastructure/Desktop ownership and safe UI behavior.
- Verifies: stable automation names/IDs, no duplicate IDs, tab/focus path, typed actions, and no direct EF/SQLite/process/filesystem diagnostics in page/view-model source.

- [ ] **Step 1: Add failing source-boundary assertions**

```csharp
[Theory]
[InlineData("Pages/SettingsViewModel.cs")]
[InlineData("Pages/SystemDiagnosticsViewModel.cs")]
public async Task ViewModels_do_not_import_infrastructure_or_process_apis(string relative)
{
    string source = await File.ReadAllTextAsync(DesktopSource(relative));
    foreach (string forbidden in new[]
    {
        "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite",
        "System.Diagnostics.Process", "DriveInfo", "FileStream", "Directory."
    })
        Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
}
```

Also assert `SettingsPage.xaml.cs` never reads `SystemDiagnosticFact.Value` to open a path; `DiagnosticDesktopServices.cs` is the only Desktop source allowed to reference `Clipboard` for this feature; all dynamic action IDs are derived from stable item/action IDs and contain no path; and XAML contains no click handler that launches arbitrary commands.

- [ ] **Step 2: Add keyboard/accessibility structure assertions**

Verify diagnostics tab, refresh, summary copy, every issue action, and folder targets are focusable; tab selection is not mouse-only; each severity has icon and text; error code text is selectable/readable; duplicate `AutomationId` values are rejected except IDs intentionally inside a data template, where the bound item/action ID must make them unique.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter "FullyQualifiedName~SettingsDiagnosticsArchitectureTests|FullyQualifiedName~SettingsDiagnosticsStructureTests|FullyQualifiedName~DesktopCompositionRootTests"
```

Expected: at least one new guard fails until imports, automation bindings, or focus declarations are tightened.

- [ ] **Step 4: Make the minimum production corrections**

Move every process/filesystem reference reported by the failing guard into the typed service from Task 10. Add the exact group/item/action automation properties defined in Task 12 to the presentation VMs. Use `AutomationProperties.Name` that combines fixed title and fixed status, never paths or local fact values. Do not add UI Automation driver dependencies; this stage stops at stable native accessibility boundaries and manual keyboard verification.

- [ ] **Step 5: Run the complete Desktop project**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
```

Expected: all Desktop tests pass.

- [ ] **Step 6: Commit**

```powershell
git add apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsStructureTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Pages/SettingsDiagnosticsArchitectureTests.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Composition/DesktopCompositionRootTests.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SystemDiagnosticsViewModel.cs apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml apps/media-desktop/src/TraceFold.Media.Desktop/Pages/SettingsPage.xaml.cs apps/media-desktop/src/TraceFold.Media.Desktop/Services/DiagnosticDesktopServices.cs apps/media-desktop/src/TraceFold.Media.Desktop/Composition/DesktopCompositionRoot.cs
git commit -m "test(media-desktop): close diagnostics ui boundaries"
```

Before committing, inspect `git diff --cached --name-only`; only the listed guard tests and production files directly corrected by those guards may be staged.

---

## Phase D — Guarded Package Proof and Delivery

### Task 14: Add the Independent Stage 5C-B Package Probe and Manual Guide

**Files:**
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeContracts.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeOptions.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeRunner.cs`
- Modify: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/PackageProbeEnvironment.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5CBDiagnosticsProbeResult.cs`
- Create: `apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe/Stage5CBDiagnosticsProbeScenario.cs`
- Modify: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/PackageProbeTestSupport.cs`
- Create: `apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5CBDiagnosticsPackageProbeTests.cs`
- Modify: `scripts/publish-media-desktop.ps1`
- Modify: `scripts/smoke-media-desktop-package.ps1`
- Create: `docs/media-desktop-stage5cb-diagnostics-test-guide.md`

**Interfaces:**
- Adds: `PackageProbeScenarioKind.Stage5CBDiagnostics`, exact scenario `stage5cb-diagnostics`, root prefix `tracefold-media-desktop-stage5cb-probe-`, and result filename `stage5cb-diagnostics-probe-result.json`.
- Produces: primitive-only `Stage5CBDiagnosticsProbeResult` fields.
- Extends: packaging `ValidateSet` with exact checkpoint `5C-B` and a version slug that produces `stage5cb` while preserving the package directory's `Stage5C-B` name.

- [ ] **Step 1: Write failing option and ownership tests**

```csharp
[Fact]
public void Stage5CB_exact_scenario_maps_to_the_guarded_request()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5CB();
    PackageProbeOptionResult result = PackageProbeOptions.Parse([
        "--package-probe-scenario", "stage5cb-diagnostics",
        "--package-probe-token", root.TokenText,
        "--package-probe-root", root.Path]);

    Assert.Equal(PackageProbeOptionState.Ready, result.State);
    Assert.Equal(PackageProbeScenarioKind.Stage5CBDiagnostics,
        result.Request!.Scenario);
}
```

Copy the existing wrong-token, wrong-prefix, non-empty unexpected root, missing fixture, reparse-root, and cancelled-token cases. They must fail before scenario writes, and cleanup may remove only a validated direct-child root with the matching marker.

- [ ] **Step 2: Write a failing end-to-end probe test**

```csharp
[Fact]
public async Task Stage5CB_probe_proves_read_only_diagnostics_and_redaction()
{
    using OwnedProbeRoot root = OwnedProbeRoot.CreateStage5CB();

    int exitCode = await PackageProbeRunner.RunAsync(
        PackageProbeRequests.Stage5CBDiagnostics(root),
        TestContext.Current.CancellationToken);
    Stage5CBDiagnosticsProbeResult result = root.ReadStage5CBResult();

    Assert.Equal(0, exitCode);
    Assert.True(result.Succeeded, result.ErrorCode);
    Assert.True(result.AllCategoriesObserved);
    Assert.True(result.FailureIsolationPassed);
    Assert.True(result.RedactionPassed);
    Assert.True(result.BusinessDataUnchanged);
    Assert.True(result.SettingsUnchanged);
    Assert.True(result.BackgroundStateUnchanged);
    Assert.True(result.MediaFilesUnchanged);
    Assert.True(result.TemporaryArtifactsClean);
    Assert.True(result.ToolProcessesClean);
    Assert.Null(result.ErrorCode);
}
```

In the unit-test output, assert `PackagedEnvironmentHealthy` is false when the test output intentionally lacks the release manifest/tools; it is an observation rather than a probe-harness failure. Assert the result JSON contains primitives only and excludes root path, owner token, `.db`, `.log`, media filenames, metadata sentinels, hashes, and IDs.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~Stage5CBDiagnosticsPackageProbeTests
```

Expected: FAIL because Stage 5C-B package contracts and scenario do not exist.

- [ ] **Step 4: Implement the guarded diagnostics scenario**

Inside its owned root, create runtime directories, start the host, initialize/migrate its test database, save three test-owned folders, seed count states and harmless media fixture files, then capture business/settings/state/file snapshots. Resolve the five real probes and run the real `ISystemDiagnosticsService`.

For failure isolation, instantiate a second `SystemDiagnosticsService` with the same five resolved probes plus one package-only synthetic probe whose unique descriptor throws a sentinel exception. Assert the stable `diagnostics.probe.failed` item appears and every successful real-probe category remains. Format the snapshot through `IDiagnosticSummaryFormatter`; assert the owned root, token, fixture names, metadata sentinel, hash sentinel, IDs, and exception message are absent.

After the run, compare all before/after snapshots, enumerate zero `.tracefold-write-probe-*.tmp` files, verify no unexpected internal diagnostic files/history exist, and compare package-local FFmpeg/FFprobe process IDs before/after with a bounded wait until no newly observed process remains. `PackagedEnvironmentHealthy` is true only when all five real release-package probes and the overall status are healthy; unit-test layouts may report it false while still proving the harness invariants. Write only this primitive result:

```csharp
public sealed record Stage5CBDiagnosticsProbeResult(
    bool Succeeded,
    bool AllCategoriesObserved,
    bool PackagedEnvironmentHealthy,
    bool FailureIsolationPassed,
    bool RedactionPassed,
    bool BusinessDataUnchanged,
    bool SettingsUnchanged,
    bool BackgroundStateUnchanged,
    bool MediaFilesUnchanged,
    bool TemporaryArtifactsClean,
    bool ToolProcessesClean,
    string? ErrorCode);
```

Do not invoke the Desktop clipboard, explorer, tab, or repair actions from the headless package probe.

- [ ] **Step 5: Extend publish and smoke scripts without aliasing 5C-B to 5C**

In both scripts add `5C-B` to `ValidateSet`. Keep:

```powershell
$packageName = 'TraceFold-MediaDesktop-Stage5C-B-win-x64'
$versionStageSlug = $StageCheckpoint.Replace('-', '').ToLowerInvariant()
$informationalVersion = "4.0.0-stage$versionStageSlug+$sourceCommit"
```

The smoke script must select only `stage5cb-diagnostics`, wait at most 60 seconds, require every boolean above including `PackagedEnvironmentHealthy`, inspect no private value in the JSON, check no package FFmpeg/FFprobe process remains, and continue the existing native-window, single-instance, restricted-`PATH`, no-browser, log creation, invalid-root exit, and clean-shutdown checks. Keep the existing `5C` taxonomy branch unchanged.

- [ ] **Step 6: Write the manual guide before packaging**

`docs/media-desktop-stage5cb-diagnostics-test-guide.md` must identify the exact package/checkpoint and include expected results for:

1. Settings defaults to “文件夹与扫描”.
2. Keyboard tab switching and accessible names.
3. First diagnostics entry auto-runs exactly once.
4. Overall status/time and five-group order.
5. Refresh retains old results and duplicate refresh does not queue.
6. Saved folder paths/capacity are shown; unsaved drafts are not treated as active.
7. FFmpeg and FFprobe versions appear separately.
8. Database open/migration/`quick_check` are healthy.
9. Background work contains counts only.
10. A controlled copied-package missing-folder/tool fault affects only its item and leaves ordinary file management available as described.
11. Impact, guidance, stable code, and no repeated dialogs.
12. Folder navigation selects the folder tab and focuses the correct row.
13. Open logs opens only the registered application log root.
14. Copied summary excludes username, paths, filenames, metadata, hashes, and IDs.
15. Before/after business/media facts are unchanged.
16. No write-probe file or child tool/application process remains after exit.
17. Restricted-`PATH` offline launch opens a native window, not a browser.
18. Clean-machine verification remains explicitly “未执行” when no second Windows 11 x64 machine/VM exists.

Do not create `docs/media-desktop-stage5cb-diagnostics-acceptance.md` in this task.

- [ ] **Step 7: Run the focused package tests and script syntax checks**

```powershell
dotnet test apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/TraceFold.Media.Desktop.Tests.csproj -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --filter FullyQualifiedName~Stage5CBDiagnosticsPackageProbeTests
$publishParseErrors = $null
$smokeParseErrors = $null
$publishTokens = [System.Management.Automation.PSParser]::Tokenize((Get-Content scripts/publish-media-desktop.ps1 -Raw), [ref]$publishParseErrors)
$smokeTokens = [System.Management.Automation.PSParser]::Tokenize((Get-Content scripts/smoke-media-desktop-package.ps1 -Raw), [ref]$smokeParseErrors)
if ($publishTokens.Count -eq 0 -or $smokeTokens.Count -eq 0 -or $publishParseErrors.Count -gt 0 -or $smokeParseErrors.Count -gt 0) { throw 'PowerShell tokenization failed.' }
```

Expected: tests PASS and both scripts tokenize without parser errors.

- [ ] **Step 8: Commit**

```powershell
git add apps/media-desktop/src/TraceFold.Media.Desktop/Diagnostics/PackageProbe apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/PackageProbeTestSupport.cs apps/media-desktop/tests/TraceFold.Media.Desktop.Tests/Diagnostics/Stage5CBDiagnosticsPackageProbeTests.cs scripts/publish-media-desktop.ps1 scripts/smoke-media-desktop-package.ps1 docs/media-desktop-stage5cb-diagnostics-test-guide.md
git commit -m "feat(media-desktop): add stage 5c-b package proof"
```

### Task 15: Review, Verify, Publish, and Hand Off Stage 5C-B

**Files:**
- Review: every file changed since the plan commit.
- Generate, do not commit: `dist/TraceFold-MediaDesktop-Stage5C-B-win-x64/`
- Generate, do not commit: `dist/TraceFold-MediaDesktop-Stage5C-B-win-x64.zip`

**Interfaces:**
- Verifies: specification coverage, test growth above 810, privacy, layering, read-only behavior, native package smoke, exact version/commit, and clean source tree.
- Produces: ZIP path, byte size, SHA-256, informational version, source commit, automated test count, smoke evidence, and an explicit pending manual-acceptance statement.

- [ ] **Step 1: Invoke the required code-review workflow**

Use `superpowers:requesting-code-review` against the full diff from the committed plan to current `HEAD`. The review must specifically inspect:

- diagnostic contract invariants and deterministic ordering;
- caller cancellation versus per-probe/whole-run timeout mapping;
- process kill/wait and temporary-file cleanup;
- absence of writes/migrations/history/repairs;
- summary/log privacy sentinels;
- background count-only boundary;
- typed registered-folder/clipboard actions;
- tab lifecycle/focus/accessibility;
- `5C` versus `5C-B` package separation.

Address every confirmed issue with focused red-green tests and a separate fix commit. Do not accept a review suggestion that weakens an approved invariant merely to simplify implementation.

- [ ] **Step 2: Run clean offline restore, Release build, and full suite**

```powershell
git status --short
dotnet restore apps/media-desktop/TraceFold.MediaDesktop.slnx --source $traceFoldPackageCache -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers --ignore-failed-sources --force-evaluate
dotnet build apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-build --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers --logger "console;verbosity=normal"
```

Expected: source tree is clean before generated package output; build has 0 warnings/errors; all tests pass; total is greater than 810. Record the exact test count from output.

- [ ] **Step 3: Run focused privacy and read-only gates again**

```powershell
dotnet test apps/media-desktop/TraceFold.MediaDesktop.slnx -c Release --no-restore -p:RestorePackagesPath=$traceFoldPackageCache --disable-build-servers --filter "FullyQualifiedName~DiagnosticsLoggingPrivacy|FullyQualifiedName~DiagnosticSummaryFormatter|FullyQualifiedName~SystemDiagnosticsReadOnly|FullyQualifiedName~Stage5CBDiagnostics"
```

Expected: PASS, with no temp probe files or child processes remaining.

- [ ] **Step 4: Assemble a verified local FFmpeg source tree from the retained Stage 5C-A package**

The publish script expects the pinned upstream layout (`bin`, `LICENSE`, `README.txt`). Build it in a uniquely named system-temp directory from the already verified retained offline package; do not download anything:

```powershell
$traceFoldPreviousTools = 'T:\Windows\Documents\GitHub\TraceFold\dist\TraceFold-MediaDesktop-Stage5C-win-x64\tools\ffmpeg'
$traceFoldFfmpegSource = Join-Path ([IO.Path]::GetTempPath()) "tracefold-stage5cb-ffmpeg-source-$([Guid]::NewGuid().ToString('N'))"
$traceFoldFfmpegBin = Join-Path $traceFoldFfmpegSource 'bin'
New-Item -ItemType Directory -Path $traceFoldFfmpegBin | Out-Null
Copy-Item -LiteralPath (Join-Path $traceFoldPreviousTools 'ffmpeg.exe') -Destination $traceFoldFfmpegBin
Copy-Item -LiteralPath (Join-Path $traceFoldPreviousTools 'ffprobe.exe') -Destination $traceFoldFfmpegBin
Copy-Item -LiteralPath (Join-Path $traceFoldPreviousTools 'LICENSE') -Destination $traceFoldFfmpegSource
Copy-Item -LiteralPath (Join-Path $traceFoldPreviousTools 'README.txt') -Destination $traceFoldFfmpegSource
```

Before publish, compare both executable size/hash values with `apps/media-desktop/tools/ffmpeg/win-x64/manifest.json`. Stop if either differs.

- [ ] **Step 5: Publish and smoke the independent package**

```powershell
try {
    & .\scripts\publish-media-desktop.ps1 -StageCheckpoint '5C-B' -FfmpegSourceRoot $traceFoldFfmpegSource -NugetPackageSource $traceFoldPackageCache
    & .\scripts\smoke-media-desktop-package.ps1 -StageCheckpoint '5C-B' -PackageDirectory 'T:\TraceFold5CB\dist\TraceFold-MediaDesktop-Stage5C-B-win-x64'
}
finally {
    $traceFoldResolvedFfmpegSource = [IO.Path]::GetFullPath($traceFoldFfmpegSource)
    $traceFoldTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $traceFoldExpectedLeaf = [IO.Path]::GetFileName($traceFoldResolvedFfmpegSource)
    if (-not $traceFoldResolvedFfmpegSource.StartsWith($traceFoldTempBase, [StringComparison]::OrdinalIgnoreCase) -or
        -not $traceFoldExpectedLeaf.StartsWith('tracefold-stage5cb-ffmpeg-source-', [StringComparison]::Ordinal)) {
        throw 'Refusing to clean an unexpected FFmpeg staging path.'
    }
    Remove-Item -LiteralPath $traceFoldResolvedFfmpegSource -Recurse -Force
}
```

Expected smoke output includes native window, single instance, offline startup, no browser, clean exit, five diagnostic categories, failure isolation, redaction, unchanged business/media facts, clean temporary storage, and no tool process residue.

- [ ] **Step 6: Capture immutable delivery evidence**

```powershell
$traceFoldPackageDirectory = 'T:\TraceFold5CB\dist\TraceFold-MediaDesktop-Stage5C-B-win-x64'
$traceFoldZip = 'T:\TraceFold5CB\dist\TraceFold-MediaDesktop-Stage5C-B-win-x64.zip'
$traceFoldManifest = Get-Content (Join-Path $traceFoldPackageDirectory 'package-version.json') -Raw | ConvertFrom-Json
$traceFoldZipItem = Get-Item -LiteralPath $traceFoldZip
$traceFoldZipHash = Get-FileHash -LiteralPath $traceFoldZip -Algorithm SHA256
$traceFoldSourceCommit = git rev-parse HEAD
$traceFoldManifest
$traceFoldZipItem | Select-Object FullName,Length,LastWriteTime
$traceFoldZipHash
$traceFoldSourceCommit
git status --short
```

Expected:

- `StageCheckpoint` is exactly `5C-B`;
- `InformationalVersion` is exactly `4.0.0-stage5cb+<40-char source commit>`;
- manifest `SourceCommit` equals `git rev-parse HEAD`;
- ZIP exists with a recorded byte size and SHA-256;
- only ignored `dist` output is present and no source/document change is uncommitted;
- the retained `TraceFold-MediaDesktop-Stage5C-win-x64.zip` still exists and was not overwritten.

- [ ] **Step 7: Apply `superpowers:verification-before-completion` and hand off**

Re-read the final command output instead of relying on earlier runs. Report:

- source branch and commit;
- automated test count and Release build result;
- package directory/ZIP absolute paths;
- ZIP bytes and SHA-256;
- package checkpoint/informational version/source commit;
- smoke checks passed;
- manual guide absolute path;
- clean-machine test and all manual acceptance items remain pending until the user performs them.

Do not claim Stage 5C-B accepted and do not create an acceptance document yet.

---

## Final Specification Coverage Checklist

- [ ] One Settings navigation item and two native tabs; folder/scan is default.
- [ ] Auto-run once, manual refresh, single-flight gate, cancellation, and session-only retention.
- [ ] Five explicitly registered probes plus one overall status; deterministic category/severity/order/ID sorting.
- [ ] 5/15/20-second timeout policy and isolated exception/timeout results.
- [ ] Saved folders only, path safety, per-volume capacity, 10 GiB/10% attention thresholds, zero write-probe residue.
- [ ] SQLite open, exact migration agreement, and read-only `PRAGMA quick_check`; no migration or write.
- [ ] Separate package-local FFmpeg/FFprobe versions; bounded capture and child-process cleanup.
- [ ] Queued/running/interrupted/failed/recovery-cleanup counts only; no task/file details.
- [ ] Assembly/package/Windows/.NET/app/log facts and manifest consistency; log failure is attention.
- [ ] Full paths visible locally but excluded from summary; formatter admits only `Shareable` facts.
- [ ] Structured logs contain only run/probe/phase/duration/count/severity/stable-code fields and no private sentinels.
- [ ] Only typed folder navigation, registered log root, and error-code copy actions.
- [ ] Stable Chinese accessibility names, automation IDs, keyboard path, focus return, and icon-plus-text states.
- [ ] Business rows, settings, job states, and media file snapshots unchanged by diagnostics.
- [ ] Independent `5C-B` package/version/scenario/result paths; Stage 5C-A output retained.
- [ ] Automated suite grows beyond 810 and passes; manual acceptance remains pending user execution.
