using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using TraceFold.Media.Application.Diagnostics;
using TraceFold.Media.Desktop.Services;

namespace TraceFold.Media.Desktop.Pages;

public sealed class SystemDiagnosticsViewModel : ObservableObject
{
    private readonly ISystemDiagnosticsService _diagnostics;
    private readonly IDiagnosticSummaryFormatter _formatter;
    private readonly IDiagnosticClipboardService _clipboard;
    private readonly IRegisteredLogFolderService _logs;
    private readonly object _runGate = new();
    private Task _activeRun = Task.CompletedTask;
    private CancellationTokenSource? _currentRun;
    private SystemDiagnosticsSnapshot? _snapshot;
    private bool _activationAttempted;
    private bool _isRunning;
    private IReadOnlyList<SystemDiagnosticGroupViewModel> _groups = [];
    private string _overallStatusText = "尚未检查";
    private string _overallIconGlyph = "\uE946";
    private string _lastCheckedText = "尚未检查";
    private string _runStatusText = string.Empty;
    private string _pageMessage = string.Empty;

    public SystemDiagnosticsViewModel(
        ISystemDiagnosticsService diagnostics,
        IDiagnosticSummaryFormatter formatter,
        IDiagnosticClipboardService clipboard,
        IRegisteredLogFolderService logs)
    {
        _diagnostics = diagnostics;
        _formatter = formatter;
        _clipboard = clipboard;
        _logs = logs;
        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            CanRefresh);
        CopySummaryCommand = new AsyncRelayCommand(
            CopySummaryAsync,
            CanCopySummary);
        ExecuteActionCommand =
            new AsyncRelayCommand<SystemDiagnosticActionViewModel>(
                ExecuteActionAsync,
                CanExecuteAction);
    }

    public event EventHandler<SystemDiagnosticActionTarget>?
        FolderSettingsNavigationRequested;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand CopySummaryCommand { get; }

    public IAsyncRelayCommand<SystemDiagnosticActionViewModel>
        ExecuteActionCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public IReadOnlyList<SystemDiagnosticGroupViewModel> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public string OverallStatusText
    {
        get => _overallStatusText;
        private set => SetProperty(ref _overallStatusText, value);
    }

    public string OverallIconGlyph
    {
        get => _overallIconGlyph;
        private set => SetProperty(ref _overallIconGlyph, value);
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        private set => SetProperty(ref _lastCheckedText, value);
    }

    public string RunStatusText
    {
        get => _runStatusText;
        private set => SetProperty(ref _runStatusText, value);
    }

    public string PageMessage
    {
        get => _pageMessage;
        private set
        {
            if (SetProperty(ref _pageMessage, value))
            {
                OnPropertyChanged(nameof(HasPageMessage));
            }
        }
    }

    public bool HasPageMessage => !string.IsNullOrWhiteSpace(PageMessage);

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        lock (_runGate)
        {
            if (_activationAttempted)
            {
                return _activeRun;
            }

            _activationAttempted = true;
            return StartRunLocked(cancellationToken);
        }
    }

    public void CancelCurrentRun()
    {
        lock (_runGate)
        {
            _currentRun?.Cancel();
        }
    }

    private bool CanRefresh() => !IsRunning;

    private bool CanCopySummary() => !IsRunning && _snapshot is not null;

    private bool CanExecuteAction(SystemDiagnosticActionViewModel? action) =>
        !IsRunning && action is not null;

    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        lock (_runGate)
        {
            return IsRunning
                ? _activeRun
                : StartRunLocked(cancellationToken);
        }
    }

    private Task StartRunLocked(CancellationToken cancellationToken)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        _currentRun = run;
        bool refreshing = _snapshot is not null;
        PageMessage = string.Empty;
        RunStatusText = refreshing ? "正在重新检查" : "正在检查";
        IsRunning = true;
        Task task = CompleteRunAsync(run);
        _activeRun = task;
        return task;
    }

    private async Task CompleteRunAsync(CancellationTokenSource run)
    {
        try
        {
            SystemDiagnosticsSnapshot snapshot =
                await _diagnostics.CheckAsync(run.Token);
            IReadOnlyList<SystemDiagnosticGroupViewModel> groups =
                Project(snapshot);
            if (run.IsCancellationRequested)
            {
                return;
            }

            _snapshot = snapshot;
            Groups = groups;
            (OverallStatusText, OverallIconGlyph) =
                Presentation(snapshot.OverallSeverity);
            LastCheckedText =
                $"最近检查：{snapshot.CompletedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            PageMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Cancellation intentionally preserves the last complete snapshot.
        }
        catch (Exception)
        {
            PageMessage = "诊断检查失败：diagnostics.run_failed";
        }
        finally
        {
            lock (_runGate)
            {
                if (ReferenceEquals(_currentRun, run))
                {
                    _currentRun = null;
                    RunStatusText = string.Empty;
                    IsRunning = false;
                }
            }

            run.Dispose();
        }
    }

    private async Task CopySummaryAsync(CancellationToken cancellationToken)
    {
        SystemDiagnosticsSnapshot? snapshot = _snapshot;
        if (snapshot is null || IsRunning)
        {
            return;
        }

        try
        {
            string summary = _formatter.Format(snapshot, TimeZoneInfo.Local);
            DiagnosticDesktopActionResult result = await _clipboard.CopyAsync(
                summary,
                cancellationToken);
            PageMessage = result.Succeeded
                ? "已复制诊断摘要。"
                : result.ErrorCode ?? "diagnostics.clipboard.copy_failed";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            PageMessage = "diagnostics.clipboard.copy_failed";
        }
    }

    private async Task ExecuteActionAsync(
        SystemDiagnosticActionViewModel? action,
        CancellationToken cancellationToken)
    {
        if (action is null || IsRunning)
        {
            return;
        }

        switch (action.Kind)
        {
            case SystemDiagnosticActionKind.NavigateToFolderSettings:
                FolderSettingsNavigationRequested?.Invoke(this, action.Target);
                break;
            case SystemDiagnosticActionKind.OpenRegisteredLogFolder:
                ApplyActionResult(await _logs.OpenAsync(cancellationToken));
                break;
            case SystemDiagnosticActionKind.CopyErrorCode:
                if (!string.IsNullOrWhiteSpace(action.ErrorCode))
                {
                    ApplyActionResult(await _clipboard.CopyAsync(
                        action.ErrorCode,
                        cancellationToken));
                }

                break;
            default:
                throw new InvalidOperationException(
                    "The diagnostic action kind is unsupported.");
        }
    }

    private void ApplyActionResult(DiagnosticDesktopActionResult result)
    {
        PageMessage = result.Succeeded
            ? string.Empty
            : result.ErrorCode ?? "diagnostics.action.failed";
    }

    private IReadOnlyList<SystemDiagnosticGroupViewModel> Project(
        SystemDiagnosticsSnapshot snapshot) =>
        snapshot.Items
            .GroupBy(item => item.Category)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                SystemDiagnosticItemViewModel[] items =
                [.. group
                    .OrderBy(item => SeverityOrder(item.Severity))
                    .ThenBy(item => item.Order)
                    .ThenBy(item => item.Id, StringComparer.Ordinal)
                    .Select(ProjectItem)];
                SystemDiagnosticSeverity severity =
                    items.Max(item => item.Severity);
                (string status, string icon) = Presentation(severity);
                return new SystemDiagnosticGroupViewModel(
                    group.Key,
                    CategoryTitle(group.Key),
                    severity,
                    status,
                    icon,
                    items);
            })
            .ToArray();

    private SystemDiagnosticItemViewModel ProjectItem(SystemDiagnosticItem item)
    {
        (string status, string icon) = Presentation(item.Severity);
        SystemDiagnosticFactViewModel[] facts =
        [.. item.Facts
            .OrderBy(fact => fact.Order)
            .ThenBy(fact => fact.Key, StringComparer.Ordinal)
            .Select(fact => new SystemDiagnosticFactViewModel(
                fact.Key,
                fact.Label,
                fact.Value,
                fact.Sensitivity))];
        SystemDiagnosticActionViewModel[] actions =
        [.. item.Actions.Select(action =>
        {
            var projected = new SystemDiagnosticActionViewModel(
                item.Id,
                action.Kind,
                action.Target,
                action.Label,
                item.ErrorCode,
                ExecuteProjectedActionAsync,
                () => !IsRunning);
            return projected;
        })];
        return new SystemDiagnosticItemViewModel(
            item.Id,
            item.Severity,
            status,
            icon,
            item.Title,
            item.Conclusion,
            item.Impact,
            item.Guidance,
            item.ErrorCode,
            item.Severity != SystemDiagnosticSeverity.Healthy,
            facts,
            actions);
    }

    private Task ExecuteProjectedActionAsync(
        SystemDiagnosticActionViewModel action) =>
        ExecuteActionCommand.ExecuteAsync(action);

    private void NotifyCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CopySummaryCommand.NotifyCanExecuteChanged();
        ExecuteActionCommand.NotifyCanExecuteChanged();
        foreach (SystemDiagnosticActionViewModel action in Groups
                     .SelectMany(group => group.Items)
                     .SelectMany(item => item.Actions))
        {
            action.NotifyCanExecuteChanged();
        }
    }

    public void ReportUiBoundaryFailure() =>
        PageMessage = "诊断检查失败：diagnostics.run_failed";

    private static int SeverityOrder(SystemDiagnosticSeverity severity) =>
        severity switch
        {
            SystemDiagnosticSeverity.Unavailable => 0,
            SystemDiagnosticSeverity.Attention => 1,
            SystemDiagnosticSeverity.Healthy => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(severity))
        };

    private static (string Status, string Icon) Presentation(
        SystemDiagnosticSeverity severity) =>
        severity switch
        {
            SystemDiagnosticSeverity.Healthy => ("正常", "\uE73E"),
            SystemDiagnosticSeverity.Attention => ("需要留意", "\uE7BA"),
            SystemDiagnosticSeverity.Unavailable => ("不可用", "\uEA39"),
            _ => throw new ArgumentOutOfRangeException(nameof(severity))
        };

    private static string CategoryTitle(SystemDiagnosticCategory category) =>
        category switch
        {
            SystemDiagnosticCategory.FoldersAndStorage => "目录与磁盘",
            SystemDiagnosticCategory.Database => "数据库",
            SystemDiagnosticCategory.ProcessingTools => "前置处理工具",
            SystemDiagnosticCategory.BackgroundWork => "后台状态",
            SystemDiagnosticCategory.RuntimeAndLogs => "日志与运行环境",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
}

public sealed record SystemDiagnosticGroupViewModel(
    SystemDiagnosticCategory Category,
    string Title,
    SystemDiagnosticSeverity Severity,
    string StatusText,
    string IconGlyph,
    IReadOnlyList<SystemDiagnosticItemViewModel> Items)
{
    public string AutomationId => Category switch
    {
        SystemDiagnosticCategory.FoldersAndStorage => "DiagnosticsFoldersGroup",
        SystemDiagnosticCategory.Database => "DiagnosticsDatabaseGroup",
        SystemDiagnosticCategory.ProcessingTools => "DiagnosticsToolsGroup",
        SystemDiagnosticCategory.BackgroundWork => "DiagnosticsBackgroundGroup",
        SystemDiagnosticCategory.RuntimeAndLogs => "DiagnosticsRuntimeGroup",
        _ => throw new ArgumentOutOfRangeException(nameof(Category))
    };
}

public sealed record SystemDiagnosticItemViewModel(
    string Id,
    SystemDiagnosticSeverity Severity,
    string StatusText,
    string IconGlyph,
    string Title,
    string Conclusion,
    string? Impact,
    string? Guidance,
    string? ErrorCode,
    bool ShowDetails,
    IReadOnlyList<SystemDiagnosticFactViewModel> Facts,
    IReadOnlyList<SystemDiagnosticActionViewModel> Actions)
{
    public string AccessibleName => $"{Title}，{StatusText}";

    public string AutomationId => $"DiagnosticItem-{Id}";

    public string ErrorCodeAutomationId => $"DiagnosticErrorCode-{Id}";

    public Visibility DetailsVisibility => ShowDetails
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed record SystemDiagnosticFactViewModel(
    string Key,
    string Label,
    string Value,
    SystemDiagnosticFactSensitivity Sensitivity);

public sealed class SystemDiagnosticActionViewModel
{
    internal SystemDiagnosticActionViewModel(
        string stableItemId,
        SystemDiagnosticActionKind kind,
        SystemDiagnosticActionTarget target,
        string label,
        string? errorCode,
        Func<SystemDiagnosticActionViewModel, Task> execute,
        Func<bool> canExecute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableItemId);
        Kind = kind;
        Target = target;
        Label = label;
        ErrorCode = errorCode;
        AutomationId = $"DiagnosticAction-{stableItemId}-{kind}";
        ExecuteCommand = new AsyncRelayCommand(
            () => execute(this),
            canExecute);
    }

    public SystemDiagnosticActionKind Kind { get; }

    public SystemDiagnosticActionTarget Target { get; }

    public string Label { get; }

    public string AutomationId { get; }

    public string AutomationName => Label;

    public IAsyncRelayCommand ExecuteCommand { get; }

    internal string? ErrorCode { get; }

    internal void NotifyCanExecuteChanged() =>
        ExecuteCommand.NotifyCanExecuteChanged();
}
