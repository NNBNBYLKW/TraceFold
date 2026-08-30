using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Desktop.Pages;
using TraceFold.Media.Desktop.Services;
using TraceFold.Media.Desktop.Tests.Support;
using Xunit;

namespace TraceFold.Media.Desktop.Tests.Pages;

public sealed class SystemDiagnosticsViewModelTests
{
    [Fact]
    public async Task Activate_runs_once_and_refresh_is_single_flight()
    {
        var diagnostics = new GatedDiagnosticsService();
        SystemDiagnosticsViewModel viewModel = Create(diagnostics);

        Task first = viewModel.ActivateAsync(CancellationToken.None);
        await diagnostics.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Task duplicateActivation = viewModel.ActivateAsync(CancellationToken.None);

        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Equal(1, diagnostics.CallCount);
        Assert.Empty(viewModel.Groups);
        Assert.Equal("正在检查", viewModel.RunStatusText);

        diagnostics.Complete(HealthySnapshot());
        await Task.WhenAll(first, duplicateActivation);
        await viewModel.ActivateAsync(CancellationToken.None);

        Assert.Equal(1, diagnostics.CallCount);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_retains_old_groups_until_atomic_replacement()
    {
        SystemDiagnosticsSnapshot initial = Snapshot(Item(
            "folders.inbox",
            SystemDiagnosticCategory.FoldersAndStorage,
            SystemDiagnosticSeverity.Healthy,
            order: 0));
        SystemDiagnosticsSnapshot replacement = Snapshot(Item(
            "database.sqlite",
            SystemDiagnosticCategory.Database,
            SystemDiagnosticSeverity.Attention,
            order: 0));
        var diagnostics = new SequencedDiagnosticsService(initial);
        SystemDiagnosticsViewModel viewModel = Create(diagnostics);
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<SystemDiagnosticGroupViewModel> oldGroups = viewModel.Groups;
        GatedDiagnosticsService refresh = diagnostics.EnqueueGate();

        Task run = viewModel.RefreshCommand.ExecuteAsync(null);
        await refresh.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Same(oldGroups, viewModel.Groups);
        Assert.Equal("正在重新检查", viewModel.RunStatusText);
        Assert.False(viewModel.CopySummaryCommand.CanExecute(null));

        refresh.Complete(replacement);
        await run;

        Assert.NotSame(oldGroups, viewModel.Groups);
        Assert.Equal(SystemDiagnosticCategory.Database,
            Assert.Single(viewModel.Groups).Category);
    }

    [Fact]
    public async Task Cancellation_retains_the_previous_snapshot()
    {
        SystemDiagnosticsSnapshot initial = HealthySnapshot();
        var diagnostics = new SequencedDiagnosticsService(initial);
        SystemDiagnosticsViewModel viewModel = Create(diagnostics);
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<SystemDiagnosticGroupViewModel> oldGroups = viewModel.Groups;
        GatedDiagnosticsService refresh = diagnostics.EnqueueGate();
        Task run = viewModel.RefreshCommand.ExecuteAsync(null);
        await refresh.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        viewModel.CancelCurrentRun();
        await run;

        Assert.Same(oldGroups, viewModel.Groups);
        Assert.False(viewModel.IsRunning);
        Assert.Equal(1, refresh.CancellationCount);
    }

    [Fact]
    public async Task Service_failure_shows_stable_message_without_clearing_results()
    {
        var diagnostics = new SequencedDiagnosticsService(HealthySnapshot());
        SystemDiagnosticsViewModel viewModel = Create(diagnostics);
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<SystemDiagnosticGroupViewModel> oldGroups = viewModel.Groups;
        diagnostics.EnqueueFailure(new InvalidOperationException(
            "C:\\private\\secret-file.mp4"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(oldGroups, viewModel.Groups);
        Assert.Equal("诊断检查失败：diagnostics.run_failed", viewModel.PageMessage);
        Assert.DoesNotContain("secret-file", viewModel.PageMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completed_snapshot_survives_page_reactivation()
    {
        var diagnostics = new SequencedDiagnosticsService(HealthySnapshot());
        SystemDiagnosticsViewModel viewModel = Create(diagnostics);

        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<SystemDiagnosticGroupViewModel> groups = viewModel.Groups;
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Same(groups, viewModel.Groups);
        Assert.Equal(1, diagnostics.CallCount);
    }

    [Fact]
    public async Task Copy_summary_is_disabled_without_snapshot_and_while_running()
    {
        var diagnostics = new GatedDiagnosticsService();
        SystemDiagnosticsViewModel viewModel = Create(diagnostics);

        Assert.False(viewModel.CopySummaryCommand.CanExecute(null));
        Task run = viewModel.ActivateAsync(CancellationToken.None);
        await diagnostics.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.False(viewModel.CopySummaryCommand.CanExecute(null));
        diagnostics.Complete(HealthySnapshot());
        await run;
        Assert.True(viewModel.CopySummaryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Projection_orders_groups_and_items_and_exposes_text_plus_icon()
    {
        SystemDiagnosticsSnapshot snapshot = Snapshot(
            Item("runtime.good", SystemDiagnosticCategory.RuntimeAndLogs,
                SystemDiagnosticSeverity.Healthy, order: 0),
            Item("folders.good", SystemDiagnosticCategory.FoldersAndStorage,
                SystemDiagnosticSeverity.Healthy, order: 0),
            Item("database.attention", SystemDiagnosticCategory.Database,
                SystemDiagnosticSeverity.Attention, order: 2),
            Item("database.unavailable", SystemDiagnosticCategory.Database,
                SystemDiagnosticSeverity.Unavailable, order: 9),
            Item("database.healthy", SystemDiagnosticCategory.Database,
                SystemDiagnosticSeverity.Healthy, order: 0));
        SystemDiagnosticsViewModel viewModel = Create(
            new SequencedDiagnosticsService(snapshot));

        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                SystemDiagnosticCategory.FoldersAndStorage,
                SystemDiagnosticCategory.Database,
                SystemDiagnosticCategory.RuntimeAndLogs
            },
            viewModel.Groups.Select(group => group.Category));
        SystemDiagnosticGroupViewModel database = viewModel.Groups[1];
        Assert.Equal(
            new[]
            {
                SystemDiagnosticSeverity.Unavailable,
                SystemDiagnosticSeverity.Attention,
                SystemDiagnosticSeverity.Healthy
            },
            database.Items.Select(item => item.Severity));
        Assert.False(string.IsNullOrWhiteSpace(database.StatusText));
        Assert.False(string.IsNullOrWhiteSpace(database.IconGlyph));
        Assert.True(database.Items[0].ShowDetails);
        Assert.False(database.Items[2].ShowDetails);
    }

    [Fact]
    public async Task Folder_action_raises_only_the_typed_focus_target()
    {
        SystemDiagnosticsViewModel viewModel = await CreateWithActionAsync(
            SystemDiagnosticActionKind.NavigateToFolderSettings,
            SystemDiagnosticActionTarget.WorkDirectory);
        SystemDiagnosticActionTarget? target = null;
        viewModel.FolderSettingsNavigationRequested += (_, value) => target = value;

        await SingleAction(viewModel).ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(SystemDiagnosticActionTarget.WorkDirectory, target);
    }

    [Fact]
    public async Task Log_action_calls_registered_service_without_a_path()
    {
        var logs = new RecordingRegisteredLogFolderService();
        SystemDiagnosticsViewModel viewModel = await CreateWithActionAsync(
            SystemDiagnosticActionKind.OpenRegisteredLogFolder,
            SystemDiagnosticActionTarget.None,
            logs: logs);

        await SingleAction(viewModel).ExecuteCommand.ExecuteAsync(null);

        Assert.Equal(1, logs.CallCount);
    }

    [Fact]
    public async Task Copy_error_action_copies_only_the_item_error_code()
    {
        var clipboard = new RecordingDiagnosticClipboardService();
        SystemDiagnosticsViewModel viewModel = await CreateWithActionAsync(
            SystemDiagnosticActionKind.CopyErrorCode,
            SystemDiagnosticActionTarget.None,
            clipboard: clipboard);

        await SingleAction(viewModel).ExecuteCommand.ExecuteAsync(null);

        Assert.Equal("diagnostics.test.problem", clipboard.CopiedText);
    }

    [Fact]
    public async Task Copy_summary_uses_local_time_zone_and_does_not_store_text()
    {
        var clipboard = new RecordingDiagnosticClipboardService();
        var formatter = new RecordingDiagnosticSummaryFormatter(
            "shareable summary only");
        SystemDiagnosticsViewModel viewModel = Create(
            new SequencedDiagnosticsService(HealthySnapshot()),
            clipboard,
            formatter: formatter);
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);

        await viewModel.CopySummaryCommand.ExecuteAsync(null);

        Assert.Equal("shareable summary only", clipboard.CopiedText);
        Assert.Same(TimeZoneInfo.Local, formatter.TimeZone);
        Assert.DoesNotContain(
            viewModel.GetType().GetProperties(),
            property => property.Name.Contains("CopiedText",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Copy_failure_displays_only_the_stable_error_code()
    {
        var clipboard = new RecordingDiagnosticClipboardService
        {
            Result = DiagnosticDesktopActionResult.Failed(
                "diagnostics.clipboard.copy_failed")
        };
        SystemDiagnosticsViewModel viewModel = Create(
            new SequencedDiagnosticsService(HealthySnapshot()),
            clipboard);
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);

        await viewModel.CopySummaryCommand.ExecuteAsync(null);

        Assert.Equal("diagnostics.clipboard.copy_failed", viewModel.PageMessage);
    }

    private static SystemDiagnosticsViewModel Create(
        ISystemDiagnosticsService diagnostics,
        RecordingDiagnosticClipboardService? clipboard = null,
        RecordingRegisteredLogFolderService? logs = null,
        RecordingDiagnosticSummaryFormatter? formatter = null) =>
        new(
            diagnostics,
            formatter ?? new RecordingDiagnosticSummaryFormatter("summary"),
            clipboard ?? new RecordingDiagnosticClipboardService(),
            logs ?? new RecordingRegisteredLogFolderService());

    private static async Task<SystemDiagnosticsViewModel> CreateWithActionAsync(
        SystemDiagnosticActionKind kind,
        SystemDiagnosticActionTarget target,
        RecordingDiagnosticClipboardService? clipboard = null,
        RecordingRegisteredLogFolderService? logs = null)
    {
        SystemDiagnosticAction action = SystemDiagnosticAction.Create(
            kind,
            target,
            "执行");
        SystemDiagnosticItem item = Item(
            "runtime.problem",
            SystemDiagnosticCategory.RuntimeAndLogs,
            SystemDiagnosticSeverity.Attention,
            order: 0,
            actions: [action]);
        SystemDiagnosticsViewModel viewModel = Create(
            new SequencedDiagnosticsService(Snapshot(item)),
            clipboard,
            logs);
        await viewModel.ActivateAsync(TestContext.Current.CancellationToken);
        return viewModel;
    }

    private static SystemDiagnosticActionViewModel SingleAction(
        SystemDiagnosticsViewModel viewModel) =>
        Assert.Single(Assert.Single(viewModel.Groups).Items.Single().Actions);

    private static SystemDiagnosticsSnapshot HealthySnapshot() => Snapshot(Item(
        "folders.inbox",
        SystemDiagnosticCategory.FoldersAndStorage,
        SystemDiagnosticSeverity.Healthy,
        order: 0));

    private static SystemDiagnosticsSnapshot Snapshot(
        params SystemDiagnosticItem[] items)
    {
        DateTimeOffset started = DateTimeOffset.Parse("2026-08-30T00:00:00Z");
        return SystemDiagnosticsSnapshot.Create(
            Guid.NewGuid(),
            started,
            started.AddSeconds(1),
            items.Max(item => item.Severity),
            items);
    }

    private static SystemDiagnosticItem Item(
        string id,
        SystemDiagnosticCategory category,
        SystemDiagnosticSeverity severity,
        int order,
        IReadOnlyList<SystemDiagnosticAction>? actions = null) =>
        SystemDiagnosticItem.Create(
            id,
            category,
            severity,
            order,
            title: id,
            conclusion: severity == SystemDiagnosticSeverity.Healthy
                ? "正常"
                : "需要处理",
            impact: severity == SystemDiagnosticSeverity.Healthy ? null : "有影响",
            guidance: severity == SystemDiagnosticSeverity.Healthy ? null : "请处理",
            errorCode: severity == SystemDiagnosticSeverity.Healthy
                ? null
                : "diagnostics.test.problem",
            facts:
            [
                SystemDiagnosticFact.Create(
                    $"{id}.fact",
                    "事实",
                    "值",
                    SystemDiagnosticFactSensitivity.Shareable,
                    order: 0)
            ],
            actions: actions ?? []);

    private sealed class GatedDiagnosticsService : ISystemDiagnosticsService
    {
        private readonly TaskCompletionSource<SystemDiagnosticsSnapshot> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public int CancellationCount { get; private set; }

        public async Task<SystemDiagnosticsSnapshot> CheckAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            Started.TrySetResult();
            try
            {
                return await _completion.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationCount++;
                throw;
            }
        }

        public void Complete(SystemDiagnosticsSnapshot snapshot) =>
            _completion.TrySetResult(snapshot);
    }

    private sealed class SequencedDiagnosticsService(params object[] responses)
        : ISystemDiagnosticsService
    {
        private readonly Queue<object> _responses = new(responses);

        public int CallCount { get; private set; }

        public GatedDiagnosticsService EnqueueGate()
        {
            var gate = new GatedDiagnosticsService();
            _responses.Enqueue(gate);
            return gate;
        }

        public void EnqueueFailure(Exception failure) => _responses.Enqueue(failure);

        public Task<SystemDiagnosticsSnapshot> CheckAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            object response = _responses.Dequeue();
            return response switch
            {
                SystemDiagnosticsSnapshot snapshot => Task.FromResult(snapshot),
                ISystemDiagnosticsService service => service.CheckAsync(cancellationToken),
                Exception failure => Task.FromException<SystemDiagnosticsSnapshot>(
                    failure),
                _ => throw new InvalidOperationException()
            };
        }
    }
}
